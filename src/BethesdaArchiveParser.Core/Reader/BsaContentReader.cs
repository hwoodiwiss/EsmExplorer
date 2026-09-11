using System.Text;

namespace BethesdaArchiveParser.Core.Reader;

internal sealed class BsaContentReader(BinaryBsaHeader Header, BsaReader bsaReader)
{
    private readonly BsaReader _reader = bsaReader;

    public async Task<(List<BsaFolderRecord> Folders, List<BsaFileBlockRecord> FileBlocks, List<string>? FileNames)> ReadBsaArchive()
    {
        List<BsaFolderRecord> folders = [with((int)Header.FolderCount)];
        Dictionary<long, BsaFolderRecord> foldersByBlockOffset = [with((int)Header.FolderCount)];
        _reader.BaseStream.Seek(Header.RecordOffset, SeekOrigin.Begin);
        for (int i = 0; i < Header.FolderCount; i++)
        {
            var folder = await ReadBsaFolderRecord();
            folders.Add(folder);
            foldersByBlockOffset.Add(folder.FileBlockOffset, folder);
        }

        List<BsaFileBlockRecord> fileBlocks = [with((int)Header.FolderCount)];
        for (int i = 0; i < Header.FolderCount; i++)
        {
            var folder = foldersByBlockOffset[_reader.BaseStream.Position] ?? throw new InvalidOperationException("Folder not found for file block offset.");
            fileBlocks.Add(await ReadBsaFileBlockRecord(folder));
        }

        List<string>? fileNames = null;
        if (Header.ArchiveFlags.HasFlag(BsaArchiveFlags.IncludesFileNames))
        {
            fileNames = [with((int)Header.FileCount)];
            for (int i = 0; i < Header.FileCount; i++)
            {
                var fileName = _reader.ReadZString();
                fileNames.Add(fileName);
            }
        }

        return (folders, fileBlocks, fileNames);
    }

    private async Task<BsaFolderRecord> ReadBsaFolderRecord()
    {
        var nameHash = _reader.ReadUInt64();
        var fileCount = _reader.ReadUInt32();

        if (Header.Version >= 105)
        {
            SkipPadding(4);
        }

        var fileOffset = _reader.ReadUInt32();

        if (Header.Version >= 105)
        {
            SkipPadding(4);
        }

        var folderOffset = fileOffset - Header.TotalFileNameLength;

        using var folderScope = new ScopedOffset(_reader.BaseStream, folderOffset);
        string? folderName = null;
        if (Header.ArchiveFlags.HasFlag(BsaArchiveFlags.IncludesDirectoryNames))
        {
            folderName = _reader.ReadBzString();
        }

        List<BsaFileRecord> files = [with((int)fileCount)];
        List<long> fileOffsets = [with((int)fileCount)];
        for (int i = 0; i < fileCount; i++)
        {
            fileOffsets.Add(_reader.BaseStream.Position);
        }

        return new BsaFolderRecord(Header, nameHash, fileCount, fileOffset, folderName, fileOffsets);
    }

    private async Task<BsaFileBlockRecord> ReadBsaFileBlockRecord(BsaFolderRecord folder)
    {
        string? folderName = null;
        if (Header.ArchiveFlags.HasFlag(BsaArchiveFlags.IncludesDirectoryNames))
        {
            folderName = _reader.ReadBzString();
        }

        List<BsaFileRecord> files = [with((int)folder.FileCount)];
        for (int i = 0; i < folder.FileCount; i++)
        {
            files.Add(await ReadBsaFile());
        }

        return new BsaFileBlockRecord(folderName, files);
    }

    private async Task<BsaFileRecord> ReadBsaFile()
    {
        ulong nameHash = _reader.ReadUInt64();
        uint fileSize = _reader.ReadUInt32();
        uint fileOffset = _reader.ReadUInt32();

        return new BsaFileRecord(Header, nameHash, fileSize, fileOffset);
    }

    public BsaFileData ReadBsaFileData(BsaFileRecord fileRecord)
    {
        using var fileScope = new ScopedOffset(_reader.BaseStream, fileRecord.Offset);

        string? fileName = null;
        if (Header.ArchiveFlags.HasFlag(BsaArchiveFlags.EmbedFileNames))
        {
            fileName = _reader.ReadBString();
        }

        return fileRecord.IsCompressed
            ? new BsaCompressedFile(Header, _reader.BaseStream, fileName, fileRecord.Size, _reader.ReadUInt32(), (uint)_reader.BaseStream.Position)
            : new BsaUncompressedFile(Header, _reader.BaseStream, fileName, fileRecord.Size, (uint)_reader.BaseStream.Position);
    }

    private void SkipPadding(long size) => _reader.BaseStream.Seek(size, SeekOrigin.Current);
}
