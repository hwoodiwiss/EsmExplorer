namespace BethesdaArchiveParser.Core;

public sealed record BsaFolderRecord(BinaryBsaHeader Header, ulong FolderHash, uint FileCount, uint Offset, string? Name, List<long> FileOffsets)
{
    public uint FileBlockOffset => Offset - Header.TotalFileNameLength;
}
