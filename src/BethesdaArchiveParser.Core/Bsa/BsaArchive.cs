namespace BethesdaArchiveParser.Core.Bsa;

public sealed record BsaEntry(string Path, BsaFileRecord File);

public sealed class BsaArchive
{
    private readonly Dictionary<string, BsaFileRecord> _files = [with(StringComparer.OrdinalIgnoreCase)];

    /// <param name="header">The parsed archive header.</param>
    /// <param name="entries">Entries in physical file-block order; paths may use either separator.</param>
    public BsaArchive(BinaryBsaHeader header, IReadOnlyList<BsaEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        Header = header;
        var normalized = new List<BsaEntry>(entries.Count);
        foreach (var entry in entries)
        {
            var item = entry with { Path = entry.Path.Replace('/', '\\') };
            if (!_files.TryAdd(item.Path, item.File))
            {
                throw new InvalidDataException($"Duplicate BSA path: {item.Path}");
            }
            normalized.Add(item);
        }
        Entries = normalized.AsReadOnly();
    }

    public BinaryBsaHeader Header { get; }
    public IReadOnlyList<BsaEntry> Entries { get; }

    /// <summary>Case-insensitive lookup accepting either path separator.</summary>
    public BsaFileRecord? GetFileByPath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return _files.GetValueOrDefault(path.Replace('/', '\\'));
    }
}
