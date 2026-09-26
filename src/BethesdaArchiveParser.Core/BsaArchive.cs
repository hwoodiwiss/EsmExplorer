namespace BethesdaArchiveParser.Core;

public sealed record BsaEntry(string Path, BsaFileRecord File);

public sealed class BsaArchive
{
    private readonly Dictionary<string, BsaFileRecord> _files = [with(StringComparer.OrdinalIgnoreCase)];

    public BsaArchive(BinaryBsaHeader header, List<BsaFolderRecord> folders, List<BsaFileBlockRecord> fileBlocks, List<string> fileNames)
    {
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(fileBlocks);
        ArgumentNullException.ThrowIfNull(fileNames);
        Header = header;
        Folders = folders;
        FileBlocks = fileBlocks;
        FileNames = fileNames;
        var entries = new List<BsaEntry>(fileNames.Count);
        int index = 0;
        foreach (var block in fileBlocks)
        {
            foreach (var file in block.Files)
            {
                if (index >= fileNames.Count)
                {
                    throw new InvalidDataException("Missing BSA filenames.");
                }
                string name = fileNames[index++];
                string path = string.IsNullOrEmpty(block.FolderName) ? name : $"{block.FolderName}\\{name}";
                path = path.Replace('/', '\\');
                entries.Add(new BsaEntry(path, file));
                if (!_files.TryAdd(path, file))
                {
                    throw new InvalidDataException($"Duplicate BSA path: {path}");
                }
            }
        }
        if (index != fileNames.Count)
        {
            throw new InvalidDataException("Extra BSA filenames.");
        }
        Entries = entries.AsReadOnly();
    }

    public BinaryBsaHeader Header { get; }
    public List<BsaFolderRecord> Folders { get; }
    public List<BsaFileBlockRecord> FileBlocks { get; }
    public List<string> FileNames { get; }
    public IReadOnlyList<BsaEntry> Entries { get; }

    public BsaFileRecord? GetFileByPath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return _files.GetValueOrDefault(path.Replace('/', '\\'));
    }
}
