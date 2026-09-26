namespace BethesdaArchiveParser.Core;

public sealed record BsaFolderRecord(BinaryBsaHeader Header, ulong FolderHash, uint FileCount, ulong Offset, string? Name, List<long> FileOffsets)
{
    public ulong FileBlockOffset => Offset - Header.TotalFileNameLength;
}
