using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

namespace AtomcraftPatcher;

/// <summary>
/// Patches Atomcraft.dll to call the mod loader's bootstrap during startup.
/// </summary>
/// <remarks>
/// <para>
/// It appends a call to ModLoaderBootstrap.Initialize at the end of Godot's own managed
/// entry point, which is late enough that the managed bridge is live and early enough
/// that Game._Ready has not run, so Harmony patches land before the game initializes.
/// </para>
/// <para>
/// Earlier versions cloned a whole Godot Node subclass into the game assembly, because
/// GDScript could only reach C# through a type registered with Godot's script manager.
/// That call now lives in the bootstrap, so nothing needs to be injected: a dozen
/// instructions calling it are enough. Reaching it by reflection also means the patch
/// references only System.Runtime primitives, which the game assembly already imports,
/// rather than GodotSharp types that would tie it to one Godot version.
/// </para>
/// </remarks>
internal static class Program
{
    private const int ExitSuccess = 0;
    private const int ExitFailure = 1;
    private const int ExitUsage = 2;
    private const int ExitTargetNotFound = 3;
    private const int ExitTargetUnusable = 4;
    private const int ExitOldPatch = 5;
    private const int ExitAlreadyPatched = 6;

    /// <summary>The method to append the call to: Godot's managed entry point.</summary>
    private const string EntryType = "GodotPlugins.Game.Main";
    private const string EntryMethod = "InitializeFromGameProject";

    /// <summary>
    /// The last thing Godot's entry point does before succeeding. The call goes directly
    /// after it, which is inside the method's existing try block and late enough that the
    /// managed bridge is live.
    /// </summary>
    private const string AnchorMethod = "LookupScriptsInAssembly";

    private const string BootstrapType = "ModLoaderBootstrap.Bootstrap";
    private const string BootstrapMethod = "Initialize";

    /// <summary>
    /// Relative to the game assembly's own directory, which is the data_* folder, so this
    /// climbs out to the game root. Doubles as the marker that says the file is patched.
    /// </summary>
    private const string BootstrapRelativePath = "../GodotMonoModLoader/ModLoaderBootstrap.dll";

    /// <summary>Types the pre-launcher patcher injected. Their presence means a stale patch.</summary>
    private static readonly string[] LegacyTypes =
        ["Atomcraft.GodotMonoModLoaderPatch", "Atomcraft.GodotMonoModLoader"];

    private static string _targetPath = Path.Combine(AppContext.BaseDirectory, "data_Atomcraft_windows_x86_64", "Atomcraft.dll");

    private static bool _nonInteractive;
    private static bool _quiet;
    private static bool _failIfPatched;
    private static bool _restoreOnly;

    private static bool IsInteractive =>
        !_nonInteractive && !Console.IsInputRedirected && Environment.UserInteractive;

    static int Main(string[] args)
    {
        if (!TryParseArguments(args, out string? targetArgument, out bool showHelp))
        {
            PrintUsage(Console.Error);
            return Exit(ExitUsage);
        }

        if (showHelp)
        {
            PrintUsage(Console.Out);
            return ExitSuccess;
        }

        if (!TryLocateTarget(targetArgument)) return Exit(ExitTargetNotFound);

        Info($"File to patch located: {_targetPath}.");

        if (_restoreOnly) return Exit(RestoreBackup());

        string backupPath = _targetPath + ".backup";
        string tempPath = _targetPath + ".patched";

        try
        {
            Info("Reading Atomcraft.dll...");

            using DefaultAssemblyResolver resolver = new();
            resolver.AddSearchDirectory(Path.GetDirectoryName(_targetPath)!);
            ReaderParameters readerParameters = new() { AssemblyResolver = resolver };

            using (AssemblyDefinition target = AssemblyDefinition.ReadAssembly(_targetPath, readerParameters))
            {
                ModuleDefinition module = target.MainModule;

                foreach (string legacy in LegacyTypes)
                {
                    if (module.GetType(legacy) == null) continue;
                    Error($"Old patch detected ({legacy}). Restore the backup, or verify the game files through Steam.");
                    target.Dispose(); // Required to be able to restore the target with the backup.
                    return Exit(ExitOldPatch, true);
                }

                MethodDefinition? entry = module.GetType(EntryType)?.Methods
                    .FirstOrDefault(m => m.Name == EntryMethod && m.HasBody);

                if (entry == null)
                {
                    Error($"{EntryType}::{EntryMethod} not found in {_targetPath}.");
                    Error("This does not look like an Atomcraft build this patcher understands.");
                    return Exit(ExitTargetUnusable);
                }

                if (IsAlreadyPatched(entry))
                {
                    Info("Already patched.");
                    target.Dispose(); // Required to be able to restore the target with the backup.
                    return Exit(_failIfPatched ? ExitAlreadyPatched : ExitSuccess, true);
                }

                Info($"Injecting the bootstrap call into {EntryType}::{EntryMethod}...");

                if (!TryInjectBootstrapCall(module, entry))
                {
                    Error($"Could not find the {AnchorMethod} call to inject after.");
                    return Exit(ExitTargetUnusable);
                }

                Info("Creating backup...");
                File.Copy(_targetPath, backupPath, overwrite: true);
                Info($"Backup created: {backupPath}");

                Info("Applying patch...");
                target.Write(tempPath);
            }

            File.Move(tempPath, _targetPath, overwrite: true);

            Info();
            Info("Patch applied.");
            return Exit(ExitSuccess, true);
        }
        catch (Exception ex)
        {
            Error();
            Error("ERROR:");
            Error(ex.ToString());
            return Exit(ExitFailure);
        }
    }

    private static bool IsAlreadyPatched(MethodDefinition entry) =>
        entry.Body.Instructions.Any(i => i.OpCode == OpCodes.Ldstr && (i.Operand as string) == BootstrapRelativePath);

    /// <summary>
    /// Appends, immediately after the anchor call:
    /// <code>
    /// try { Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, REL))
    ///           .GetType(TYPE).GetMethod(METHOD).Invoke(null, null); }
    /// catch { }
    /// </code>
    /// </summary>
    /// <remarks>
    /// The handler is not optional. The entry point is [UnmanagedCallersOnly] and its own
    /// catch turns any escaping exception into a false return, which Godot reports as
    /// "GodotPlugins initialization failed" and the game dies. Without a handler here, a
    /// missing or broken ModLoaderBootstrap.dll would stop the game from starting at all
    /// rather than merely leaving it unmodded.
    /// </remarks>
    private static bool TryInjectBootstrapCall(ModuleDefinition module, MethodDefinition entry)
    {
        Instruction? anchor = entry.Body.Instructions.FirstOrDefault(i =>
            (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt) &&
            i.Operand is MethodReference { Name: AnchorMethod });

        if (anchor?.Next == null) return false;

        // References are built by hand against the System.Runtime the target already
        // imports. ImportReference(typeof(...)) would resolve against *this* process's
        // runtime, where these types physically live in System.Private.CoreLib, and emit
        // a reference to it. That is an implementation assembly no application may
        // reference: the game then fails to load at all, before printing a single line.
        AssemblyNameReference corlib =
            module.AssemblyReferences.FirstOrDefault(a => a.Name == "System.Runtime")
            ?? throw new InvalidOperationException("the target does not reference System.Runtime");

        TypeReference str = module.TypeSystem.String;
        TypeReference obj = module.TypeSystem.Object;
        TypeReference typeRef = new("System", "Type", module, corlib);
        TypeReference appContext = new("System", "AppContext", module, corlib);
        TypeReference path = new("System.IO", "Path", module, corlib);
        TypeReference assembly = new("System.Reflection", "Assembly", module, corlib);
        TypeReference methodBase = new("System.Reflection", "MethodBase", module, corlib);
        TypeReference methodInfo = new("System.Reflection", "MethodInfo", module, corlib);
        TypeReference exception = new("System", "Exception", module, corlib);

        static MethodReference Method(bool instance, TypeReference declaring, string name, TypeReference returns, params TypeReference[] parameters)
        {
            MethodReference method = new(name, returns, declaring) { HasThis = instance };
            foreach (TypeReference parameter in parameters) method.Parameters.Add(new ParameterDefinition(parameter));
            return method;
        }

        MethodReference baseDirectory = Method(false, appContext, "get_BaseDirectory", str);
        MethodReference combine = Method(false, path, "Combine", str, str, str);
        MethodReference loadFrom = Method(false, assembly, "LoadFrom", assembly, str);
        MethodReference getType = Method(true, assembly, "GetType", typeRef, str);
        MethodReference getMethod = Method(true, typeRef, "GetMethod", methodInfo, str);
        MethodReference invoke = Method(true, methodBase, "Invoke", obj, obj, new ArrayType(obj));

        Instruction resume = anchor.Next;

        List<Instruction> body =
        [
            Instruction.Create(OpCodes.Call, baseDirectory),
            Instruction.Create(OpCodes.Ldstr, BootstrapRelativePath),
            Instruction.Create(OpCodes.Call, combine),
            Instruction.Create(OpCodes.Call, loadFrom),
            Instruction.Create(OpCodes.Ldstr, BootstrapType),
            Instruction.Create(OpCodes.Callvirt, getType),
            Instruction.Create(OpCodes.Ldstr, BootstrapMethod),
            Instruction.Create(OpCodes.Callvirt, getMethod),
            Instruction.Create(OpCodes.Ldnull),
            Instruction.Create(OpCodes.Ldnull),
            Instruction.Create(OpCodes.Callvirt, invoke),
            Instruction.Create(OpCodes.Pop),
            Instruction.Create(OpCodes.Leave, resume),
        ];

        // The handler discards the exception and rejoins the original code.
        Instruction handlerStart = Instruction.Create(OpCodes.Pop);
        List<Instruction> handler = [handlerStart, Instruction.Create(OpCodes.Leave, resume)];

        ILProcessor il = entry.Body.GetILProcessor();
        Instruction at = anchor;
        foreach (Instruction instruction in body.Concat(handler))
        {
            il.InsertAfter(at, instruction);
            at = instruction;
        }

        // Inner handlers must precede enclosing ones, and this nests inside the try the
        // entry point already has, so it goes first in the table.
        entry.Body.ExceptionHandlers.Insert(0, new ExceptionHandler(ExceptionHandlerType.Catch)
        {
            CatchType = exception,
            TryStart = body[0],
            TryEnd = handlerStart,
            HandlerStart = handlerStart,
            HandlerEnd = resume,
        });

        entry.Body.OptimizeMacros();
        return true;
    }

    private static bool TryLocateTarget(string? targetArgument)
    {
        if (targetArgument != null)
        {
            _targetPath = Path.GetFullPath(targetArgument);
            if (File.Exists(_targetPath)) return true;
            Error($"File not found: {_targetPath}");
            return false;
        }

        if (File.Exists(_targetPath)) return true;

        Info($"File not found: {_targetPath}");
        Info("Trying one folder up.");
        _targetPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "data_Atomcraft_windows_x86_64", "Atomcraft.dll"));
        if (File.Exists(_targetPath)) return true;

        Error($"File not found: {_targetPath}");
        return false;
    }

    static bool TryParseArguments(string[] args, out string? targetArgument, out bool showHelp)
    {
        targetArgument = null;
        showHelp = false;

        bool optionsEnded = false;

        foreach (string arg in args)
        {
            if (!optionsEnded)
            {
                switch (arg)
                {
                    case "--":
                        optionsEnded = true;
                        continue;
                    case "-y":
                    case "--non-interactive":
                        _nonInteractive = true;
                        continue;
                    case "--quiet":
                        _quiet = true;
                        continue;
                    case "--fail-if-patched":
                        _failIfPatched = true;
                        continue;
                    case "--restore":
                        _restoreOnly = true;
                        continue;
                    case "-h":
                    case "--help":
                        showHelp = true;
                        continue;
                }

                if (arg.StartsWith('-') && arg.Length > 1)
                {
                    Error($"Unknown option: {arg}");
                    return false;
                }
            }

            if (targetArgument != null)
            {
                Error($"Unexpected argument: {arg}");
                return false;
            }

            targetArgument = arg;
        }

        return true;
    }

    static void PrintUsage(TextWriter writer)
    {
        writer.WriteLine("Usage: AtomcraftPatcher [options] [path to Atomcraft.dll]");
        writer.WriteLine();
        writer.WriteLine("Patches Atomcraft.dll to load GodotMonoModLoader. With no path given, the");
        writer.WriteLine("game data folder next to the patcher, then one folder up, is searched.");
        writer.WriteLine();
        writer.WriteLine("Options:");
        writer.WriteLine("  -y, --non-interactive  Never wait for a keypress before exiting.");
        writer.WriteLine("      --restore          Restore the backup instead of patching.");
        writer.WriteLine("      --fail-if-patched  Exit 6 instead of 0 when already patched.");
        writer.WriteLine("      --quiet            Suppress progress output. Errors still go to stderr.");
        writer.WriteLine("  -h, --help             Show this help.");
        writer.WriteLine();
        writer.WriteLine("Exit codes:");
        writer.WriteLine("  0  Patch applied, or already patched");
        writer.WriteLine("  1  Unclassified failure");
        writer.WriteLine("  2  Usage error");
        writer.WriteLine("  3  Atomcraft.dll or backup not found");
        writer.WriteLine("  4  Atomcraft.dll is not a build this patcher understands");
        writer.WriteLine("  5  Old patch detected, restore required");
        writer.WriteLine("  6  Already patched, with --fail-if-patched");
    }

    static void Info(string message = "")
    {
        if (!_quiet)
        {
            Console.WriteLine(message);
        }
    }

    static void Error(string message = "")
    {
        Console.Error.WriteLine(message);
    }

    static int Exit(int exitCode, bool restore = false)
    {
        bool canRestore = restore && File.Exists(_targetPath + ".backup");

        if (!IsInteractive)
        {
            return exitCode;
        }

        Console.WriteLine();
        Console.WriteLine(canRestore ? "Press ANY key to EXIT or R to restore the backup" : "Press ANY key to EXIT");
        Console.WriteLine();

        ConsoleKeyInfo key;
        try
        {
            key = Console.ReadKey(true);
        }
        catch (InvalidOperationException)
        {
            // No console input handle after all. Nothing to wait for.
            return exitCode;
        }

        if (canRestore && key.Key == ConsoleKey.R)
        {
            return Exit(RestoreBackup());
        }
        return exitCode;
    }

    static int RestoreBackup()
    {
        string backupPath = _targetPath + ".backup";

        if (!File.Exists(backupPath))
        {
            Error($"Backup not found: {backupPath}");
            return ExitTargetNotFound;
        }

        try
        {
            File.Copy(backupPath, _targetPath, overwrite: true);
        }
        catch (Exception ex)
        {
            Error();
            Error("ERROR:");
            Error(ex.ToString());
            return ExitFailure;
        }

        Info("Backup restored.");
        return ExitSuccess;
    }
}
