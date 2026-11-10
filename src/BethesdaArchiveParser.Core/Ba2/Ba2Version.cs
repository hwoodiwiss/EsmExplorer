namespace BethesdaArchiveParser.Core.Ba2;

/// <summary>The BA2 format versions implemented by this reader, using their on-disk values.</summary>
/// <remarks>Layouts follow https://en.uesp.net/wiki/Fallout4Mod:Archive_File_Format and the bsa-rs <c>fo4</c> module.</remarks>
public enum Ba2Version : uint
{
    /// <summary>Original Fallout 4: 24-byte header, zlib.</summary>
    Fallout4 = 1,

    /// <summary>Starfield: 32-byte header with an unexplained 64-bit field, zlib.</summary>
    StarfieldV2 = 2,

    /// <summary>Starfield: 36-byte header adding a compression method (zlib or LZ4 block).</summary>
    StarfieldV3 = 3,

    /// <summary>Fallout 4 next-gen update: original layout, zlib.</summary>
    Fallout4NextGenV7 = 7,

    /// <summary>Fallout 4 next-gen update: original layout, zlib.</summary>
    Fallout4NextGenV8 = 8,
}

public enum Ba2ArchiveType
{
    /// <summary><c>GNRL</c>: one chunk per file.</summary>
    General,

    /// <summary><c>DX10</c>: textures split into mip chunks; extracted as DDS files.</summary>
    Texture,
}

public enum Ba2CompressionMethod : uint
{
    Zlib = 0,

    /// <summary>Raw LZ4 block (not frame) per chunk; version 3 only.</summary>
    Lz4Block = 3,
}
