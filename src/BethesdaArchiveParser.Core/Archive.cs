using BethesdaArchiveParser.Core.Bsa;

namespace BethesdaArchiveParser.Core;

/// <summary>A format-specific archive. Pattern-match to access format details.</summary>
public readonly union ArchiveDetails(BsaArchive);

/// <summary>A format-agnostic view of an archive's entries.</summary>
public sealed class Archive
{
    private readonly Dictionary<string, ArchiveEntry> _entries = [with(StringComparer.OrdinalIgnoreCase)];

    public Archive(BsaArchive archive)
    {
        ArgumentNullException.ThrowIfNull(archive);
        Format = ArchiveFormat.Bsa;
        Details = archive;
        bool embeddedNames = archive.Header.Version is BsaVersion.Fallout3AndSkyrim or BsaVersion.SkyrimSpecialEdition
            && archive.Header.ArchiveFlags.HasFlag(BsaArchiveFlags.EmbedFileNames);
        var entries = new List<ArchiveEntry>(archive.Entries.Count);
        foreach (var entry in archive.Entries)
        {
            // Compressed sizes and embedded-name prefixes live inside the payload, so only plain entries are known up front.
            long? expanded = entry.File.IsCompressed || embeddedNames ? null : entry.File.StoredSize;
            var unified = new ArchiveEntry(entry.Path, entry.File, entry.File.IsCompressed, expanded);
            entries.Add(unified);
            _entries.Add(unified.Path, unified);
        }
        Entries = entries.AsReadOnly();
    }

    public ArchiveFormat Format { get; }
    public ArchiveDetails Details { get; }
    public IReadOnlyList<ArchiveEntry> Entries { get; }

    /// <summary>Case-insensitive lookup accepting either path separator.</summary>
    public ArchiveEntry? GetEntry(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return _entries.GetValueOrDefault(path.Replace('/', '\\'));
    }
}
