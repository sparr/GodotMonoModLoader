using System.Runtime.InteropServices;

namespace ModLoaderHook;

/// <summary>
/// Makes this DLL a stand-in for the system dinput8.dll, as a way of being resident in
/// the game process early.
/// </summary>
/// <remarks>
/// <para>
/// AtomCraft.exe imports dinput8.dll statically, and Steam does not ship that file, so a
/// dinput8.dll beside the executable is a *new* file rather than a modified one:
/// integrity checks and game updates both leave it alone. The loader maps it during
/// process startup, roughly 80 loader events before hostfxr appears.
/// </para>
/// <para>
/// Being mapped is not enough on its own. Native AOT runs no managed code at DLL attach
/// (see docs/hostfxr-chain.md), so the hook can only start when the game calls one of
/// these exports. Godot calls DirectInput8Create while setting up input, which in a
/// windowed run happens before it loads hostfxr. A headless run creates no DisplayServer
/// and never calls it, which this approach cannot work around; see
/// docs/dinput8-delivery.md.
/// </para>
/// <para>
/// Every export drives <see cref="Hook.EnsureInitialized"/> rather than just the one we
/// expect, because which export the game reaches first is not ours to decide.
/// </para>
/// </remarks>
internal static unsafe class Dinput8Exports
{
    private const int E_FAIL = unchecked((int)0x80004005);

    private static readonly object Gate = new();
    private static nint _real;
    private static bool _tried;

    /// <summary>
    /// Resolves an export of the real dinput8, loading it on first use.
    /// </summary>
    /// <remarks>
    /// By absolute path out of the system directory, so that a process-wide override
    /// preferring a native dinput8 cannot resolve back to this file.
    /// </remarks>
    private static nint Forward(string name)
    {
        lock (Gate)
        {
            if (!_tried)
            {
                _tried = true;
                string real = Path.Combine(Environment.SystemDirectory, "dinput8.dll");
                _real = Native.LoadLibraryW(real);
                if (_real == 0)
                    Log.Write($"could not load the real {real} ({Marshal.GetLastWin32Error()}); DirectInput will be unavailable");
            }
        }

        return _real == 0 ? 0 : Native.GetProcAddress(_real, name);
    }

    [UnmanagedCallersOnly(EntryPoint = "DirectInput8Create")]
    public static int DirectInput8Create(nint instance, uint version, nint riid, nint result, nint outer)
    {
        Hook.EnsureInitialized();
        nint fn = Forward(nameof(DirectInput8Create));
        return fn == 0 ? E_FAIL : ((delegate* unmanaged<nint, uint, nint, nint, nint, int>)fn)(instance, version, riid, result, outer);
    }

    [UnmanagedCallersOnly(EntryPoint = "DllGetClassObject")]
    public static int DllGetClassObject(nint classId, nint interfaceId, nint result)
    {
        Hook.EnsureInitialized();
        nint fn = Forward(nameof(DllGetClassObject));
        return fn == 0 ? E_FAIL : ((delegate* unmanaged<nint, nint, nint, int>)fn)(classId, interfaceId, result);
    }

    [UnmanagedCallersOnly(EntryPoint = "DllCanUnloadNow")]
    public static int DllCanUnloadNow()
    {
        Hook.EnsureInitialized();
        nint fn = Forward(nameof(DllCanUnloadNow));
        return fn == 0 ? E_FAIL : ((delegate* unmanaged<int>)fn)();
    }

    [UnmanagedCallersOnly(EntryPoint = "DllRegisterServer")]
    public static int DllRegisterServer()
    {
        Hook.EnsureInitialized();
        nint fn = Forward(nameof(DllRegisterServer));
        return fn == 0 ? E_FAIL : ((delegate* unmanaged<int>)fn)();
    }

    [UnmanagedCallersOnly(EntryPoint = "DllUnregisterServer")]
    public static int DllUnregisterServer()
    {
        Hook.EnsureInitialized();
        nint fn = Forward(nameof(DllUnregisterServer));
        return fn == 0 ? E_FAIL : ((delegate* unmanaged<int>)fn)();
    }
}
