using System.Runtime.InteropServices;

/// <summary>
/// Minimal native export, used to prove the cross-compiled DLL both links and
/// actually executes managed code under the Windows loader.
/// </summary>
public static class Exports
{
    [UnmanagedCallersOnly(EntryPoint = "ProbeExport")]
    public static int ProbeExport(int value) => value + 1;
}
