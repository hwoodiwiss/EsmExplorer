namespace BethesdaArchiveParser.Core;

[Flags]
public enum BsaArchiveFlags : uint
{
    None = 0,
    IncludesDirectoryNames = 1 << 0,
    IncludesFileNames = 1 << 1,
    CompressedByDefault = 1 << 2,
    RetainDirectoryNames = 1 << 3,
    RetainFileNames = 1 << 4,
    RetainFileNameOffsets = 1 << 5,
    Xbox360Archive = 1 << 6,
    RetainStringsDuringStartup = 1 << 7,
    EmbedFileNames = 1 << 8,
    XMemCodec = 1 << 9,
}