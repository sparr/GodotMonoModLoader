namespace ModLoaderLauncher;

/// <summary>
/// Reads an export's RVA out of a PE file on disk.
/// </summary>
/// <remarks>
/// The launcher needs the address of the hook DLL's entry point inside the *target*
/// process, which is the module's base there plus the export's RVA. The RVA is a
/// property of the file, so it can be read here without loading the DLL.
///
/// Loading it locally and subtracting would be shorter, but the hook DLL is itself a
/// native AOT image: mapping a second one into this process invites two runtimes
/// initializing over each other for no benefit.
/// </remarks>
internal static class PeFile
{
    /// <summary>Returns the RVA of a named export, or 0 if it is not present.</summary>
    internal static uint FindExportRva(string path, string exportName)
    {
        byte[] image = File.ReadAllBytes(path);

        if (Read16(image, 0) != 0x5A4D) throw new BadImageFormatException($"{path} is not a PE image");
        int ntHeaders = (int)Read32(image, 0x3C);
        if (Read32(image, ntHeaders) != 0x00004550) throw new BadImageFormatException($"{path} has no PE signature");

        ushort sectionCount = Read16(image, ntHeaders + 4 + 2);
        ushort optionalHeaderSize = Read16(image, ntHeaders + 4 + 16);
        int sections = ntHeaders + 4 + 20 + optionalHeaderSize;

        // DataDirectory[0] is the export directory, 112 bytes into the optional header.
        uint exportDirRva = Read32(image, ntHeaders + 4 + 20 + 112);
        if (exportDirRva == 0) return 0;

        int exportDir = ToOffset(image, sections, sectionCount, exportDirRva);
        uint nameCount = Read32(image, exportDir + 0x18);
        int nameRvas = ToOffset(image, sections, sectionCount, Read32(image, exportDir + 0x20));
        int ordinals = ToOffset(image, sections, sectionCount, Read32(image, exportDir + 0x24));
        int functions = ToOffset(image, sections, sectionCount, Read32(image, exportDir + 0x1C));

        for (uint i = 0; i < nameCount; i++)
        {
            int nameOffset = ToOffset(image, sections, sectionCount, Read32(image, nameRvas + (int)i * 4));
            if (ReadAscii(image, nameOffset) != exportName) continue;

            ushort ordinal = Read16(image, ordinals + (int)i * 2);
            return Read32(image, functions + ordinal * 4);
        }

        return 0;
    }

    private static int ToOffset(byte[] image, int sections, ushort sectionCount, uint rva)
    {
        for (int i = 0; i < sectionCount; i++)
        {
            int header = sections + i * 40;
            uint virtualAddress = Read32(image, header + 12);
            uint rawSize = Read32(image, header + 16);
            uint rawPointer = Read32(image, header + 20);
            if (rva >= virtualAddress && rva < virtualAddress + rawSize)
                return (int)(rawPointer + (rva - virtualAddress));
        }
        throw new BadImageFormatException($"RVA 0x{rva:X} is not inside any section");
    }

    private static ushort Read16(byte[] b, int offset) => (ushort)(b[offset] | (b[offset + 1] << 8));

    private static uint Read32(byte[] b, int offset) =>
        (uint)(b[offset] | (b[offset + 1] << 8) | (b[offset + 2] << 16) | (b[offset + 3] << 24));

    private static string ReadAscii(byte[] b, int offset)
    {
        int end = offset;
        while (end < b.Length && b[end] != 0) end++;
        return System.Text.Encoding.ASCII.GetString(b, offset, end - offset);
    }
}
