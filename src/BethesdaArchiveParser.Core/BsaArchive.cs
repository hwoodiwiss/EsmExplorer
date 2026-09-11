namespace BethesdaArchiveParser.Core;

public sealed class BsaArchive(BinaryBsaHeader Header, List<BsaFolderRecord> Folders, List<BsaFileBlockRecord> FileBlocks, List<string> FileNames)
{
    public BinaryBsaHeader Header { get; } = Header;
    public List<BsaFolderRecord> Folders { get; } = Folders;
    public List<BsaFileBlockRecord> FileBlocks { get; } = FileBlocks;
    public List<string> FileNames { get; } = FileNames;

    // Read optimized views
    private readonly Dictionary<ulong, BsaFolderRecord> _folderHashLookup = Folders.ToDictionary(static f => f.FolderHash, static f => f);
    private readonly Dictionary<ulong, BsaFileBlockRecord> _folderFilesLookup = FileBlocks.ToDictionary(static f => BsaHash.Compute(f.FolderName!), static f => f);
    private readonly Dictionary<ulong, string> _fileNameLookup = FileNames.Distinct().ToDictionary(static f => BsaHash.Compute(f), static f => f);

    public BsaFileRecord? GetFileByPath(string path)
    {
        if (path.Contains('/'))
        {
            path = path.Replace('/', '\\');
        }
        var lastSlash = path.LastIndexOf('\\');
        var folderPath = lastSlash == -1 ? string.Empty : path[..lastSlash];
        var fileName = lastSlash == -1 ? path : path[(lastSlash + 1)..];

        var folderHash = BsaHash.Compute(folderPath);
        if (!_folderHashLookup.TryGetValue(folderHash, out var _))
        {
            return null;
        }

        if (!_folderFilesLookup.TryGetValue(folderHash, out var fileBlock))
        {
            return null;
        }
        var fileHash = BsaHash.Compute(fileName);
        return fileBlock.GetFileByNameHash(fileHash);
    }

    private string? GetFileNameByHash(ulong hash) => _fileNameLookup.TryGetValue(hash, out var fileName) ? fileName : null;
}
