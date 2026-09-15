using System.Runtime.InteropServices;

namespace ModLoaderHook;

/// <summary>
/// The Win32 surface the proxy needs, plus just enough PE parsing to repoint one
/// entry in a loaded module's export address table.
/// </summary>
internal static unsafe class Native
{
    private const uint PAGE_READWRITE = 0x04;

    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint LoadLibraryW(string path);

    [DllImport("kernel32", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
    internal static extern nint GetProcAddress(nint module, string name);

    [DllImport("kernel32", SetLastError = true)]
    private static extern bool VirtualProtect(nint address, nuint size, uint newProtect, out uint oldProtect);

    /// <summary>
    /// Repoints <paramref name="exportName"/> in an already-loaded module to
    /// <paramref name="replacement"/>, and returns the original address.
    /// </summary>
    /// <remarks>
    /// Godot resolves hostfxr's entry points with GetProcAddress, which reads the
    /// export address table directly. Rewriting the table entry therefore
    /// intercepts the lookup without any inline patching or trampolines: the one
    /// write is a 32-bit RVA.
    ///
    /// Returns 0 if the export is missing, or if the replacement lies further than
    /// 4 GB from the module base, which a 32-bit RVA cannot encode. Both are
    /// reported rather than papered over, because a silent failure here would look
    /// like "mods just did not load".
    /// </remarks>
    internal static nint PatchExport(nint module, string exportName, nint replacement)
    {
        byte* b = (byte*)module;

        // DOS header -> NT headers. The optional header's DataDirectory sits 112
        // bytes in, after the 4-byte signature and the 20-byte file header.
        if (*(ushort*)b != 0x5A4D) return 0;                   // "MZ"
        int lfanew = *(int*)(b + 0x3C);
        if (*(uint*)(b + lfanew) != 0x00004550) return 0;      // "PE\0\0"

        uint exportDirRva = *(uint*)(b + lfanew + 4 + 20 + 112);
        uint exportDirSize = *(uint*)(b + lfanew + 4 + 20 + 112 + 4);
        if (exportDirRva == 0) return 0;

        byte* ed = b + exportDirRva;
        uint numberOfNames = *(uint*)(ed + 0x18);
        uint* nameRvas = (uint*)(b + *(uint*)(ed + 0x20));
        ushort* nameOrdinals = (ushort*)(b + *(uint*)(ed + 0x24));
        uint* functions = (uint*)(b + *(uint*)(ed + 0x1C));

        for (uint i = 0; i < numberOfNames; i++)
        {
            if (!MatchesAscii(b + nameRvas[i], exportName)) continue;

            uint* slot = functions + nameOrdinals[i];
            nint original = module + (nint)(*slot);

            long delta = replacement - module;
            if (delta <= 0 || delta > uint.MaxValue)
            {
                Log.Write($"cannot hook {exportName}: replacement is {delta} bytes from the module base, which does not fit a 32-bit RVA");
                return 0;
            }

            // An RVA landing inside the export directory would be read back as a
            // forwarder string rather than a code address.
            uint newRva = (uint)delta;
            if (newRva >= exportDirRva && newRva < exportDirRva + exportDirSize)
            {
                Log.Write($"cannot hook {exportName}: replacement RVA falls inside the export directory");
                return 0;
            }

            if (!VirtualProtect((nint)slot, sizeof(uint), PAGE_READWRITE, out uint old))
            {
                Log.Write($"cannot hook {exportName}: VirtualProtect failed ({Marshal.GetLastWin32Error()})");
                return 0;
            }

            *slot = newRva;
            VirtualProtect((nint)slot, sizeof(uint), old, out _);
            return original;
        }

        Log.Write($"cannot hook {exportName}: not found in the export table");
        return 0;
    }

    private static bool MatchesAscii(byte* p, string expected)
    {
        for (int i = 0; i < expected.Length; i++)
        {
            if (p[i] != (byte)expected[i]) return false;
        }
        return p[expected.Length] == 0;
    }
}
