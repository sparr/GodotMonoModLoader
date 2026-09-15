using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace ModLoaderBootstrap;

/// <summary>
/// The first managed code the mod loader runs inside the game's own runtime.
/// </summary>
/// <remarks>
/// <para>
/// Whatever gets it running calls it at the same point: immediately after Godot's
/// InitializeFromGameProject has succeeded, so the managed bridge is live but the
/// main scene does not exist yet and Game._Ready has not run.
/// </para>
/// <para>
/// This assembly may be loaded through hostfxr's load_assembly_and_get_function_pointer
/// or with Assembly.LoadFrom, neither of which places it in the game's own load
/// context. It therefore takes
/// nothing on faith about which context it is in: it finds the game assembly, asks
/// which context that is in, and loads the real mod loader into that one. Anything
/// that has to touch game types lives in GodotMonoModLoader.dll, on the other side
/// of that boundary.
/// </para>
/// </remarks>
public static class Bootstrap
{
    private const string GameAssembly = "Atomcraft";
    private const string LoaderAssembly = "GodotMonoModLoader";
    private const string LoaderEntryType = "GodotMonoModLoader.GodotMonoModLoader";
    private const string LoaderEntryMethod = "Initialize";

    private static string _directory = "";
    private static AssemblyLoadContext? _gameContext;

    /// <summary>Entry point for native callers, which reach it through a function pointer.</summary>
    [UnmanagedCallersOnly]
    public static int InitializeNative() => Initialize();

    /// <summary>Entry point for managed callers, such as IL injected into the game assembly.</summary>
    /// <remarks>
    /// These have to be two methods. Calling an [UnmanagedCallersOnly] method from managed
    /// code is a fatal runtime error rather than a catchable exception: the process aborts
    /// with "attempted to call a UnmanagedCallersOnly method from managed code" before the
    /// game prints a line, and no amount of try/catch around the call site helps.
    /// </remarks>
    public static int Initialize()
    {
        try
        {
            _directory = Path.GetDirectoryName(typeof(Bootstrap).Assembly.Location) ?? "";
            Log.Open(_directory);
            Log.Write($"bootstrap running in {_directory}");

            _gameContext = FindGameContext();
            if (_gameContext is null)
            {
                Log.Write($"could not find the {GameAssembly} assembly; is this the right game?");
                return 2;
            }

            Log.Write($"game load context: {Describe(_gameContext)}");

            // Mods reference Harmony, and the loader references both. Neither is on
            // the game's probing path, so the context has to be taught where to look
            // before anything that needs them is loaded.
            _gameContext.Resolving += Resolve;

            Assembly loader = LoadIntoGame(LoaderAssembly);
            InvokeEntryPoint(loader);

            Log.Write("mod loader initialized");
            return 0;
        }
        catch (Exception e)
        {
            Log.Write($"bootstrap failed: {e}");
            return 1;
        }
    }

    /// <summary>
    /// Finds whichever load context the game assembly ended up in.
    /// </summary>
    /// <remarks>
    /// GetAssemblies spans every context in the process, not just this one, which
    /// is what makes this work from an isolated context.
    /// </remarks>
    private static AssemblyLoadContext? FindGameContext()
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (string.Equals(assembly.GetName().Name, GameAssembly, StringComparison.OrdinalIgnoreCase))
                return AssemblyLoadContext.GetLoadContext(assembly);
        }
        return null;
    }

    private static string Describe(AssemblyLoadContext context) =>
        $"{context.Name ?? "(unnamed)"}{(context == AssemblyLoadContext.Default ? " [default]" : " [isolated]")}";

    private static Assembly LoadIntoGame(string simpleName)
    {
        string path = Path.Combine(_directory, simpleName + ".dll");
        if (!File.Exists(path)) throw new FileNotFoundException($"{simpleName}.dll is missing", path);

        Assembly assembly = _gameContext!.LoadFromAssemblyPath(path);
        Log.Write($"loaded {assembly.GetName().Name} {assembly.GetName().Version}");
        return assembly;
    }

    private static Assembly? Resolve(AssemblyLoadContext context, AssemblyName name)
    {
        string path = Path.Combine(_directory, name.Name + ".dll");
        if (!File.Exists(path)) return null;

        Log.Write($"resolving {name.Name} from {path}");
        return context.LoadFromAssemblyPath(path);
    }

    private static void InvokeEntryPoint(Assembly loader)
    {
        Type type = loader.GetType(LoaderEntryType)
            ?? throw new InvalidOperationException($"{LoaderEntryType} not found in {loader.GetName().Name}");

        MethodInfo entry = type.GetMethod(LoaderEntryMethod, BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null)
            ?? throw new InvalidOperationException($"public static {LoaderEntryType}.{LoaderEntryMethod}() not found");

        entry.Invoke(null, null);
    }
}
