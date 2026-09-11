namespace BethesdaArchiveParser.Core;

public sealed record BsaFileBlockRecord(string? FolderName, ICollection<BsaFileRecord> Files)
{
    public int Size { get; } = 16 * Files.Count;

    private readonly Dictionary<ulong, BsaFileRecord> _fileLookup = Files.ToDictionary(static f => f.NameHash, static f => f);

    public BsaFileRecord? GetFileByNameHash(ulong nameHash) => _fileLookup.TryGetValue(nameHash, out var file) ? file : null;
}
