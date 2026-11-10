using BethesdaArchiveParser.Core.Bsa;

namespace BethesdaArchiveParser.Core;

/// <summary>Archive container formats recognised by <see cref="Reader.ArchiveReader.DetectFormat(Stream)"/>.</summary>
public enum ArchiveFormat
{
    Unknown,

    /// <summary>Bethesda BSA (<c>BSA\0</c>); see <see cref="BsaVersion"/> for supported versions.</summary>
    Bsa,

    /// <summary>Bethesda BA2 (<c>BTDX</c>); see <see cref="Ba2.Ba2Version"/> for supported versions.</summary>
    Ba2,
}
