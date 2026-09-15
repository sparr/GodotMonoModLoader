using System.Runtime.InteropServices;

namespace ModLoaderHook;

/// <summary>
/// The mod loader's foothold inside the game process.
/// </summary>
/// <remarks>
/// <para>
/// This is the half that is the same however the DLL gets into the process. Something
/// has to call <see cref="EnsureInitialized"/> before Godot loads hostfxr; how that is
/// arranged is the delivery mechanism's problem, and is what the approaches built on
/// top of this differ in.
/// </para>
/// <para>
/// From there the chain is three ordinary function-pointer swaps, no trampolines:
/// hostfxr_get_runtime_delegate hands back a wrapped
/// load_assembly_and_get_function_pointer, which hands back a wrapped
/// godotsharp_game_main_init, which loads the managed bootstrap once Godot's own
/// initialization has succeeded. See docs/hostfxr-chain.md.
/// </para>
/// </remarks>
public static unsafe class Hook
{
    /// <summary>hostfxr_delegate_type.hdt_load_assembly_and_get_function_pointer.</summary>
    private const int HdtLoadAssemblyAndGetFunctionPointer = 5;

    /// <summary>UNMANAGEDCALLERSONLY_METHOD: use the method's own signature, no delegate type.</summary>
    private static readonly nint UnmanagedCallersOnlyMethod = -1;

    private const string BootstrapAssembly = "ModLoaderBootstrap.dll";
    private const string BootstrapType = "ModLoaderBootstrap.Bootstrap, ModLoaderBootstrap";
    private const string BootstrapMethod = "InitializeNative";

    private static readonly object Gate = new();
    private static bool _initialized;
    private static string _gameDirectory = "";

    private static delegate* unmanaged<nint, int, nint*, int> _realGetRuntimeDelegate;
    private static delegate* unmanaged<char*, char*, char*, char*, nint, nint*, int> _realLoadAssembly;
    private static delegate* unmanaged<nint, nint, nint, int, byte> _realGameMainInit;

    // ---------------------------------------------------------------------- entry

    /// <summary>
    /// Exported so a remote thread can call it directly.
    /// </summary>
    /// <remarks>
    /// The signature matches LPTHREAD_START_ROUTINE, which is what CreateRemoteThread
    /// requires. A non-zero return says the hook is not in place, so a caller that can
    /// still abort should.
    /// </remarks>
    [UnmanagedCallersOnly(EntryPoint = "InitializeHook")]
    public static uint InitializeHook(nint parameter) => EnsureInitialized();

    /// <summary>
    /// Installs the hook, once, whichever caller gets here first.
    /// </summary>
    /// <remarks>
    /// Idempotent and locked because a delivery mechanism may have several entry points
    /// and no say in which the game calls first. Returns zero once the hook is in place.
    /// </remarks>
    public static uint EnsureInitialized()
    {
        lock (Gate)
        {
            if (!_initialized)
            {
                _initialized = true;
                try
                {
                    Initialize();
                }
                catch (Exception e)
                {
                    Log.Write($"initialization failed: {e}");
                    return 1;
                }
            }

            return _realGetRuntimeDelegate != null ? 0u : 1u;
        }
    }

    private static void Initialize()
    {
        string? exe = Environment.ProcessPath;
        _gameDirectory = Path.GetDirectoryName(exe) ?? "";
        Log.Open(_gameDirectory);
        Log.Write($"hook active in {exe}");
        HookHostfxr();
    }

    /// <summary>
    /// Loads hostfxr ahead of Godot and repoints its get_runtime_delegate export.
    /// </summary>
    /// <remarks>
    /// Loading it here rather than waiting means no import-table or LoadLibrary hook is
    /// needed: when Godot loads hostfxr by the same path it receives the module already
    /// mapped here, and its GetProcAddress then reads the patched export table.
    /// </remarks>
    private static void HookHostfxr()
    {
        string? hostfxr = FindHostfxr();
        if (hostfxr is null)
        {
            Log.Write("could not find hostfxr.dll under the game directory; giving up");
            return;
        }

        nint module = Native.LoadLibraryW(hostfxr);
        if (module == 0)
        {
            Log.Write($"could not load {hostfxr} ({Marshal.GetLastWin32Error()})");
            return;
        }

        nint original = Native.PatchExport(module, "hostfxr_get_runtime_delegate", (nint)(delegate* unmanaged<nint, int, nint*, int>)&GetRuntimeDelegateHook);
        if (original == 0) return;

        _realGetRuntimeDelegate = (delegate* unmanaged<nint, int, nint*, int>)original;
        Log.Write($"hooked hostfxr_get_runtime_delegate in {hostfxr}");
    }

    /// <summary>
    /// Locates the Godot data directory by looking for the one holding hostfxr.
    /// </summary>
    /// <remarks>
    /// The directory is named for the Godot project rather than the executable
    /// (AtomCraft.exe ships beside data_Atomcraft_windows_x86_64, note the case), so it
    /// cannot be derived from the exe name. Searching for the file itself also survives
    /// a rename or an architecture change.
    /// </remarks>
    private static string? FindHostfxr()
    {
        if (_gameDirectory.Length == 0) return null;
        try
        {
            foreach (string directory in Directory.EnumerateDirectories(_gameDirectory, "data_*"))
            {
                string candidate = Path.Combine(directory, "hostfxr.dll");
                if (File.Exists(candidate)) return candidate;
            }
        }
        catch (Exception e) { Log.Write($"error searching for hostfxr: {e.Message}"); }
        return null;
    }

    // ------------------------------------------------------------- the hook chain

    [UnmanagedCallersOnly]
    private static int GetRuntimeDelegateHook(nint context, int type, nint* result)
    {
        int rc = _realGetRuntimeDelegate(context, type, result);
        try
        {
            if (rc == 0 && type == HdtLoadAssemblyAndGetFunctionPointer && _realLoadAssembly == null)
            {
                _realLoadAssembly = (delegate* unmanaged<char*, char*, char*, char*, nint, nint*, int>)(*result);
                *result = (nint)(delegate* unmanaged<char*, char*, char*, char*, nint, nint*, int>)&LoadAssemblyHook;
                Log.Write("wrapped load_assembly_and_get_function_pointer");
            }
            else if (rc != 0)
            {
                Log.Write($"hostfxr_get_runtime_delegate(type {type}) failed with 0x{rc:X8}");
            }
        }
        catch (Exception e) { Log.Write($"error wrapping the runtime delegate: {e}"); }
        return rc;
    }

    [UnmanagedCallersOnly]
    private static int LoadAssemblyHook(char* assemblyPath, char* typeName, char* methodName, char* delegateTypeName, nint reserved, nint* result)
    {
        int rc = _realLoadAssembly(assemblyPath, typeName, methodName, delegateTypeName, reserved, result);
        try
        {
            // Godot asks for exactly one entry point, GodotPlugins.Game.Main in the game
            // assembly. Wrapping it is what gets the mod loader a turn after Godot's
            // managed bridge is live but before the main scene exists.
            if (rc == 0 && _realGameMainInit == null && Marshal.PtrToStringUni((nint)methodName) == "InitializeFromGameProject")
            {
                _realGameMainInit = (delegate* unmanaged<nint, nint, nint, int, byte>)(*result);
                *result = (nint)(delegate* unmanaged<nint, nint, nint, int, byte>)&GameMainInitHook;
                Log.Write($"wrapped {Marshal.PtrToStringUni((nint)typeName)}::InitializeFromGameProject");
            }
        }
        catch (Exception e) { Log.Write($"error wrapping the game entry point: {e}"); }
        return rc;
    }

    [UnmanagedCallersOnly]
    private static byte GameMainInitHook(nint godotDllHandle, nint outManagedCallbacks, nint unmanagedCallbacks, int unmanagedCallbacksSize)
    {
        // Let Godot finish first. It installs the DllImport resolver, initializes
        // NativeFuncs and the managed callbacks, and registers the game assembly's
        // scripts. Only afterwards is it safe for managed code to touch Godot.
        byte ok = _realGameMainInit(godotDllHandle, outManagedCallbacks, unmanagedCallbacks, unmanagedCallbacksSize);
        if (ok == 0)
        {
            Log.Write("Godot's own initialization failed; not loading the mod loader");
            return ok;
        }

        try { LoadBootstrap(); }
        catch (Exception e) { Log.Write($"error loading the bootstrap, the game will run unmodded: {e}"); }
        return ok;
    }

    /// <summary>Hands control to the managed bootstrap, inside the game's own runtime.</summary>
    private static void LoadBootstrap()
    {
        string path = Path.Combine(_gameDirectory, "GodotMonoModLoader", BootstrapAssembly);
        if (!File.Exists(path))
        {
            Log.Write($"no bootstrap at {path}; the game will run unmodded");
            return;
        }

        nint entry;
        int rc;
        fixed (char* p = path)
        fixed (char* t = BootstrapType)
        fixed (char* m = BootstrapMethod)
        {
            rc = _realLoadAssembly(p, t, m, (char*)UnmanagedCallersOnlyMethod, 0, &entry);
        }

        if (rc != 0)
        {
            Log.Write($"could not load {BootstrapAssembly}: 0x{rc:X8}");
            return;
        }

        Log.Write("invoking the managed bootstrap");
        int result = ((delegate* unmanaged<int>)entry)();
        Log.Write(result == 0
            ? "mod loader bootstrap reported success"
            : $"mod loader bootstrap reported failure ({result}); see godot.log");
    }
}
