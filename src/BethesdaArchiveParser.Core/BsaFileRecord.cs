namespace BethesdaArchiveParser.Core;

public sealed record BsaFileRecord(BinaryBsaHeader Header, ulong NameHash, uint Size, uint Offset)
{
    public bool IsCompressed
    {
        get
        {
            var compressionFlagSet = (Size & 0x40000000) != 0;
            var isCompressed = Header.ArchiveFlags.HasFlag(BsaArchiveFlags.CompressedByDefault) ^ compressionFlagSet;
            return isCompressed;
        }
    }
}
