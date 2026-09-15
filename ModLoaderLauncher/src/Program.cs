using System.Runtime.InteropServices;
using System.Text;

namespace ModLoaderLauncher;

/// <summary>
/// Starts Atomcraft with the mod loader injected.
/// </summary>
/// <remarks>
/// <para>
/// The game is created suspended, the hook DLL is loaded into it and its entry point
/// called, and only then is the process resumed. Nothing in the game's own directory is
/// modified, so there is nothing for a Steam integrity check to revert.
/// </para>
/// <para>
/// Starting suspended is what makes this reliable where the dinput8 proxy was not: a
/// suspended process has executed no code, so hostfxr cannot already be loaded and the
/// hook is always in place in time. It does not depend on the game calling into any
/// particular DLL, so it works in headless runs too.
/// </para>
/// </remarks>
internal static class Program
{
    private const string GameExecutable = "AtomCraft.exe";
    private const string HookDll = "dinput8.dll";
    private const string HookExport = "InitializeHook";
    private const string LoaderScript = "GodotMonoModLoader.gd";

    private static int Main(string[] args)
    {
        string? game = null;
        string? hook = null;
        bool wait = true;
        bool script = true;
        List<string> passthrough = [];

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--game" when i + 1 < args.Length: game = args[++i]; break;
                case "--hook" when i + 1 < args.Length: hook = args[++i]; break;
                case "--no-wait": wait = false; break;
                case "--no-script": script = false; break;
                case "--verbose" or "-v": Log.Verbosely = true; break;
                case "--help" or "-h": Usage(); return 0;
                case "--": passthrough.AddRange(args[(i + 1)..]); i = args.Length; break;
                default: passthrough.Add(args[i]); break;
            }
        }

        // The mod loader is the GDScript, not this launcher: without -s the hook still
        // installs and then has nothing to drive. Since we build the command line, add
        // it rather than making the player put it in their launch options too. An
        // explicit -s in the arguments wins, so a different script can still be run.
        if (script && !passthrough.Any(a => a is "-s" or "--script"))
        {
            passthrough.Add("-s");
            passthrough.Add(LoaderScript);
        }

        try
        {
            string directory = Path.GetDirectoryName(Environment.ProcessPath) ?? ".";
            game ??= Path.Combine(directory, GameExecutable);
            hook ??= FindHook(directory);

            if (!File.Exists(game)) throw new FileNotFoundException($"no game executable at {game}", game);
            if (!File.Exists(hook)) throw new FileNotFoundException($"no hook DLL at {hook}", hook);

            return Launch(game, hook, passthrough, wait);
        }
        catch (Exception e)
        {
            Log.Error(e.Message);
            Log.Error("the game was not started");
            return 1;
        }
    }

    /// <summary>
    /// Locates the hook, preferring a copy beside the game executable.
    /// </summary>
    /// <remarks>
    /// The same file serves two ways of starting the loader: injected from here, or left
    /// beside the executable where the game's own dinput8 import picks it up. Preferring
    /// that location means when a player has set both up, this injects the very file the
    /// loader will map, so there is one module and one initialization rather than two.
    /// </remarks>
    private static string FindHook(string directory)
    {
        string beside = Path.Combine(directory, HookDll);
        return File.Exists(beside) ? beside : Path.Combine(directory, "GodotMonoModLoader", HookDll);
    }

    private static int Launch(string game, string hook, List<string> args, bool wait)
    {
        string commandLine = BuildCommandLine(game, args);
        Log.Verbose($"command line: {commandLine}");

        Win32.StartupInfo startup = default;
        startup.cb = (uint)Marshal.SizeOf<Win32.StartupInfo>();

        // CreateProcessW may write to the command line buffer, so it cannot be a
        // marshalled literal.
        nint buffer = Marshal.StringToHGlobalUni(commandLine);
        bool created;
        Win32.ProcessInformation process;
        try
        {
            created = Win32.CreateProcessW(
                game, buffer, 0, 0, false, Win32.CREATE_SUSPENDED, 0,
                Path.GetDirectoryName(game), ref startup, out process);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        if (!created) throw new InvalidOperationException($"could not start {game} ({Marshal.GetLastWin32Error()})");

        Log.Info($"started {Path.GetFileName(game)} suspended (pid {process.ProcessId})");

        try
        {
            Injector.Inject(process.Process, process.ProcessId, hook, HookExport);
            Log.Info("mod loader injected");
        }
        catch (Exception e)
        {
            // Resuming here would start the game with no mods loaded, which for a save
            // that expects them is worse than not starting at all.
            Log.Error($"injection failed: {e.Message}");
            Win32.TerminateProcess(process.Process, 1);
            Win32.CloseHandle(process.Thread);
            Win32.CloseHandle(process.Process);
            throw new InvalidOperationException("the game was stopped rather than run unmodded");
        }

        if (Win32.ResumeThread(process.Thread) == unchecked((uint)-1))
            throw new InvalidOperationException($"ResumeThread failed ({Marshal.GetLastWin32Error()})");

        Log.Info("resumed");

        int exitCode = 0;
        if (wait)
        {
            // Staying alive keeps Steam's "running" state tied to the game rather than to
            // a launcher that exited immediately.
            Win32.WaitForSingleObject(process.Process, Win32.INFINITE);
            Win32.GetExitCodeProcess(process.Process, out uint code);
            exitCode = (int)code;
            Log.Verbose($"game exited with {exitCode}");
        }

        Win32.CloseHandle(process.Thread);
        Win32.CloseHandle(process.Process);
        return exitCode;
    }

    private static string BuildCommandLine(string game, List<string> args)
    {
        StringBuilder line = new();
        line.Append('"').Append(game).Append('"');
        foreach (string arg in args)
        {
            line.Append(' ');
            if (arg.Length > 0 && arg.IndexOfAny([' ', '\t', '"']) < 0) line.Append(arg);
            else line.Append('"').Append(arg.Replace("\"", "\\\"")).Append('"');
        }
        return line.ToString();
    }

    private static void Usage()
    {
        Console.WriteLine("""
            Usage: ModLoaderLauncher.exe [options] [-- game arguments]

            Starts Atomcraft with the mod loader injected, adding
            -s GodotMonoModLoader.gd unless the arguments already name a script. Any
            unrecognized argument is passed through to the game, so it can stand in for
            the game executable in a launch command.

            Options:
              --game <path>   game executable (default: AtomCraft.exe beside the launcher)
              --hook <path>   hook DLL (default: dinput8.dll beside the game, else
                              GodotMonoModLoader\\dinput8.dll)
              --no-wait       exit as soon as the game is running
              --no-script     do not add -s GodotMonoModLoader.gd; start the game unmodded
              -v, --verbose   report each injection step
              -h, --help      this message
            """);
    }
}
