namespace BethesdaArchiveParser.Core;

public sealed record BsaFileRecord(BinaryBsaHeader Header, ulong NameHash, uint Size, uint Offset)
{
    /// <summary>Stored size, excluding the compression-toggle flag.</summary>
    public uint StoredSize => Size & 0x3fffffff;

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
