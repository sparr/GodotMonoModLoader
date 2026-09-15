using System.Runtime.InteropServices;

namespace ModLoaderLauncher;

/// <summary>The Win32 surface needed to start a process suspended and inject into it.</summary>
internal static class Win32
{
    internal const uint CREATE_SUSPENDED = 0x00000004;
    internal const uint INFINITE = 0xFFFFFFFF;
    internal const uint WAIT_OBJECT_0 = 0;

    internal const uint MEM_COMMIT = 0x1000;
    internal const uint MEM_RESERVE = 0x2000;
    internal const uint MEM_RELEASE = 0x8000;
    internal const uint PAGE_READWRITE = 0x04;

    internal const uint TH32CS_SNAPMODULE = 0x00000008;
    internal const uint TH32CS_SNAPMODULE32 = 0x00000010;

    internal const int MAX_MODULE_NAME32 = 255;
    internal const int MAX_PATH = 260;

    [StructLayout(LayoutKind.Sequential)]
    internal struct ProcessInformation
    {
        internal nint Process;
        internal nint Thread;
        internal uint ProcessId;
        internal uint ThreadId;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct StartupInfo
    {
        internal uint cb;
        internal nint Reserved;
        internal nint Desktop;
        internal nint Title;
        internal uint X, Y, XSize, YSize, XCountChars, YCountChars, FillAttribute, Flags;
        internal ushort ShowWindow, cbReserved2;
        internal nint Reserved2;
        internal nint StdInput, StdOutput, StdError;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct ModuleEntry32
    {
        internal uint Size;
        internal uint ModuleId;
        internal uint ProcessId;
        internal uint GlblcntUsage;
        internal uint ProccntUsage;
        internal nint BaseAddress;
        internal uint BaseSize;
        internal nint Module;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MAX_MODULE_NAME32 + 1)]
        internal string ModuleName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MAX_PATH)]
        internal string ExePath;
    }

    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern bool CreateProcessW(
        string? applicationName, nint commandLine, nint processAttributes, nint threadAttributes,
        bool inheritHandles, uint creationFlags, nint environment, string? currentDirectory,
        ref StartupInfo startupInfo, out ProcessInformation processInformation);

    [DllImport("kernel32", SetLastError = true)]
    internal static extern nint VirtualAllocEx(nint process, nint address, nuint size, uint allocationType, uint protect);

    [DllImport("kernel32", SetLastError = true)]
    internal static extern bool VirtualFreeEx(nint process, nint address, nuint size, uint freeType);

    [DllImport("kernel32", SetLastError = true)]
    internal static extern bool WriteProcessMemory(nint process, nint address, byte[] buffer, nuint size, out nuint written);

    [DllImport("kernel32", SetLastError = true)]
    internal static extern nint CreateRemoteThread(
        nint process, nint threadAttributes, nuint stackSize, nint startAddress,
        nint parameter, uint creationFlags, out uint threadId);

    [DllImport("kernel32", SetLastError = true)]
    internal static extern uint WaitForSingleObject(nint handle, uint milliseconds);

    [DllImport("kernel32", SetLastError = true)]
    internal static extern bool GetExitCodeThread(nint thread, out uint exitCode);

    [DllImport("kernel32", SetLastError = true)]
    internal static extern uint ResumeThread(nint thread);

    [DllImport("kernel32", SetLastError = true)]
    internal static extern bool TerminateProcess(nint process, uint exitCode);

    [DllImport("kernel32", SetLastError = true)]
    internal static extern bool GetExitCodeProcess(nint process, out uint exitCode);

    [DllImport("kernel32", SetLastError = true)]
    internal static extern bool CloseHandle(nint handle);

    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint GetModuleHandleW(string name);

    [DllImport("kernel32", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
    internal static extern nint GetProcAddress(nint module, string name);

    [DllImport("kernel32", SetLastError = true)]
    internal static extern nint CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern bool Module32FirstW(nint snapshot, ref ModuleEntry32 entry);

    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern bool Module32NextW(nint snapshot, ref ModuleEntry32 entry);
}
