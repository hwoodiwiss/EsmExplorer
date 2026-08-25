namespace BethesdaArchiveParser.Core;

public sealed class BsaArchive(BinaryBsaHeader header)
{

    private readonly BinaryBsaHeader _header = header;
    private readonly List<BsaFolderRecord> _folders = [with((int)header.FolderCount)];
    private readonly List<BsaFileRecord> _filesBlocks = [with((int)header.FileCount)];
    private readonly List<string> _fileNames = [with((int)header.FileCount)];

    public void AddFolder(BsaFolderRecord folder) => _folders[folder.FolderHash] = folder;

    public void AddFile(BsaFileRecord file) => _files[file.NameHash] = file;

    public void AddFileName(string folderName) => _fileNames.Add(folderName);
}
