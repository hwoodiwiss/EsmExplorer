namespace BethesdaArchiveParser.Core;

/// <summary>The BSA format versions implemented by this reader, using their on-disk values.</summary>
/// <remarks>
/// Version mappings and layout differences are documented at
/// https://en.uesp.net/wiki/Skyrim_Mod:Archive_File_Format.
/// Unknown numeric values are representable by this enum but are not supported by the reader.
/// </remarks>
public enum BsaVersion : uint
{
    /// <summary>Oblivion: 16-byte folder records and zlib compression.</summary>
    Oblivion = 103,

    /// <summary>Fallout 3, Fallout: New Vegas, and original Skyrim: zlib, with embedded-name support.</summary>
    Fallout3AndSkyrim = 104,

    /// <summary>Skyrim Special Edition: 24-byte folder records and LZ4 compression.</summary>
    SkyrimSpecialEdition = 105,
}
