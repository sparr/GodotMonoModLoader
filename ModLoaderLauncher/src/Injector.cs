using System.Runtime.InteropServices;
using System.Text;

namespace ModLoaderLauncher;

/// <summary>
/// Loads a DLL into another process and calls one of its exports.
/// </summary>
/// <remarks>
/// Two remote threads rather than one. Native AOT images run no managed code at DLL
/// attach, so LoadLibraryW alone maps the hook without starting it; a second thread
/// then calls the export directly. The export's address is the module's base in the
/// target plus its RVA, read from the file on disk.
/// </remarks>
internal static class Injector
{
    /// <summary>How long to wait for either remote thread. Generous; these are quick.</summary>
    private const uint Timeout = 30_000;

    internal static void Inject(nint process, uint processId, string dllPath, string exportName)
    {
        uint exportRva = PeFile.FindExportRva(dllPath, exportName);
        if (exportRva == 0) throw new InvalidOperationException($"{Path.GetFileName(dllPath)} does not export {exportName}");

        LoadRemotely(process, dllPath);

        nint moduleBase = FindRemoteModule(processId, dllPath);
        if (moduleBase == 0)
            throw new InvalidOperationException($"{Path.GetFileName(dllPath)} is not listed in the target's modules after loading it");

        Log.Verbose($"hook loaded at 0x{moduleBase:X}, {exportName} at +0x{exportRva:X}");
        RunRemotely(process, moduleBase + (nint)exportRva, 0, exportName);
    }

    /// <summary>
    /// Runs LoadLibraryW in the target with the hook's path.
    /// </summary>
    /// <remarks>
    /// kernel32 is mapped at the same address in every process of a session, on Windows
    /// and under Wine alike, so the local address of LoadLibraryW is valid in the target.
    /// </remarks>
    private static void LoadRemotely(nint process, string dllPath)
    {
        nint kernel32 = Win32.GetModuleHandleW("kernel32.dll");
        nint loadLibrary = Win32.GetProcAddress(kernel32, "LoadLibraryW");
        if (loadLibrary == 0) throw new InvalidOperationException("could not resolve LoadLibraryW");

        byte[] path = Encoding.Unicode.GetBytes(dllPath + "\0");
        nint remote = Win32.VirtualAllocEx(process, 0, (nuint)path.Length, Win32.MEM_COMMIT | Win32.MEM_RESERVE, Win32.PAGE_READWRITE);
        if (remote == 0) throw new InvalidOperationException($"VirtualAllocEx failed ({Marshal.GetLastWin32Error()})");

        try
        {
            if (!Win32.WriteProcessMemory(process, remote, path, (nuint)path.Length, out _))
                throw new InvalidOperationException($"WriteProcessMemory failed ({Marshal.GetLastWin32Error()})");

            uint result = RunRemotely(process, loadLibrary, remote, "LoadLibraryW");
            if (result == 0)
                throw new InvalidOperationException($"LoadLibraryW returned NULL in the target; is {dllPath} readable and win-x64?");
        }
        finally
        {
            Win32.VirtualFreeEx(process, remote, 0, Win32.MEM_RELEASE);
        }
    }

    private static uint RunRemotely(nint process, nint address, nint parameter, string what)
    {
        nint thread = Win32.CreateRemoteThread(process, 0, 0, address, parameter, 0, out _);
        if (thread == 0)
            throw new InvalidOperationException($"CreateRemoteThread for {what} failed ({Marshal.GetLastWin32Error()})");

        try
        {
            if (Win32.WaitForSingleObject(thread, Timeout) != Win32.WAIT_OBJECT_0)
                throw new InvalidOperationException($"the remote {what} thread did not finish within {Timeout / 1000}s");

            Win32.GetExitCodeThread(thread, out uint exitCode);
            return exitCode;
        }
        finally
        {
            Win32.CloseHandle(thread);
        }
    }

    private static nint FindRemoteModule(uint processId, string dllPath)
    {
        string wanted = Path.GetFileName(dllPath);

        nint snapshot = Win32.CreateToolhelp32Snapshot(Win32.TH32CS_SNAPMODULE | Win32.TH32CS_SNAPMODULE32, processId);
        if (snapshot == -1 || snapshot == 0)
            throw new InvalidOperationException($"CreateToolhelp32Snapshot failed ({Marshal.GetLastWin32Error()})");

        try
        {
            Win32.ModuleEntry32 entry = default;
            entry.Size = (uint)Marshal.SizeOf<Win32.ModuleEntry32>();

            for (bool more = Win32.Module32FirstW(snapshot, ref entry); more; more = Win32.Module32NextW(snapshot, ref entry))
            {
                if (string.Equals(entry.ModuleName, wanted, StringComparison.OrdinalIgnoreCase))
                    return entry.BaseAddress;
            }
        }
        finally
        {
            Win32.CloseHandle(snapshot);
        }

        return 0;
    }
}
