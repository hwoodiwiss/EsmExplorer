using BethesdaArchiveParser.Core.Ba2.Reader;

namespace BethesdaArchiveParser.Core.Ba2;

public sealed record Ba2Header(Ba2Version Version, Ba2ArchiveType Type, uint FileCount, ulong NameTableOffset,
    Ba2CompressionMethod CompressionMethod);

/// <param name="Offset">Absolute offset of the stored chunk data.</param>
/// <param name="PackedSize">Compressed size, or 0 when the chunk is stored uncompressed.</param>
/// <param name="UnpackedSize">Expanded size of the chunk.</param>
/// <param name="StartMip">First mip level in the chunk; 0 for general files.</param>
/// <param name="EndMip">Last mip level in the chunk; 0 for general files.</param>
public sealed record Ba2Chunk(ulong Offset, uint PackedSize, uint UnpackedSize, ushort StartMip, ushort EndMip)
{
    public bool IsCompressed => PackedSize != 0;
}

public sealed record Ba2TextureInfo(ushort Width, ushort Height, byte MipCount, byte DxgiFormat, bool IsCubemap, byte TileMode);

/// <param name="Header">The owning archive header.</param>
/// <param name="NameHash">CRC of the file stem; see <see cref="Ba2Hash"/>.</param>
/// <param name="Extension">Up to four extension bytes, packed little-endian.</param>
/// <param name="DirectoryHash">CRC of the parent directory; see <see cref="Ba2Hash"/>.</param>
/// <param name="Chunks">Stored data chunks, in output order; general files have exactly one.</param>
/// <param name="Texture">Texture metadata for <c>DX10</c> archives; <see langword="null"/> for general files.</param>
public sealed record Ba2FileRecord(Ba2Header Header, uint NameHash, uint Extension, uint DirectoryHash,
    IReadOnlyList<Ba2Chunk> Chunks, Ba2TextureInfo? Texture)
{
    public bool IsCompressed => Chunks.Any(static chunk => chunk.IsCompressed);

    /// <summary>Extracted length; for textures this includes the synthesised DDS header.</summary>
    public long ExpandedLength => (Texture is null ? 0 : DdsHeader.Length) + Chunks.Sum(static chunk => (long)chunk.UnpackedSize);
}

public sealed record Ba2Entry(string Path, Ba2FileRecord File);

public sealed class Ba2Archive
{
    private readonly Dictionary<string, Ba2FileRecord> _files = [with(StringComparer.OrdinalIgnoreCase)];

    public Ba2Archive(Ba2Header header, IReadOnlyList<Ba2Entry> entries)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(entries);
        Header = header;
        var normalized = new List<Ba2Entry>(entries.Count);
        foreach (var entry in entries)
        {
            var item = entry with { Path = entry.Path.Replace('/', '\\') };
            if (!_files.TryAdd(item.Path, item.File))
            {
                throw new InvalidDataException($"Duplicate BA2 path: {item.Path}");
            }
            normalized.Add(item);
        }
        Entries = normalized.AsReadOnly();
    }

    public Ba2Header Header { get; }
    public IReadOnlyList<Ba2Entry> Entries { get; }

    public Ba2FileRecord? GetFileByPath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return _files.GetValueOrDefault(path.Replace('/', '\\'));
    }
}
