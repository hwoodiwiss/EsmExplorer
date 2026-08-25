using System.Runtime.InteropServices;
using System.Text;

namespace BethesdaArchiveParser.Core;

public sealed class BsaReader(Stream stream) : IDisposable
{
    private readonly BinaryReader _reader = new(stream, Encoding.UTF8, leaveOpen: true);

    public BinaryBsaHeader ReadHeader()
    {
        int bufferSize = Marshal.SizeOf<BinaryBsaHeader>();
        byte[] buffer = _reader.ReadBytes(bufferSize);
        nint ptr = Marshal.AllocHGlobal(bufferSize);
        Marshal.Copy(buffer, 0, ptr, buffer.Length);
        BinaryBsaHeader header = Marshal.PtrToStructure<BinaryBsaHeader>(ptr)!;
        Marshal.FreeHGlobal(ptr);
        return header;
    }

    public BsaArchive ReadBsaArchive(BinaryBsaHeader header)
    {
        BsaArchive archive = new(header);
        _reader.BaseStream.Seek(header.RecordOffset, SeekOrigin.Begin);
        for (int i = 0; i < header.FolderCount; i++)
        {
            archive.AddFolder(ReadBsaFolder(header, archive));
        }

        if (header.ArchiveFlags.HasFlag(BsaArchiveFlags.IncludesFileNames))
        {
            for (int i = 0; i < header.FileCount; i++)
            {
                var fileName = ReadZString();
                archive.AddFileName(fileName);
            }
        }

        return archive;
    }

    private BsaFolderRecord ReadBsaFolder(BinaryBsaHeader header, BsaArchive archive)
    {
        var nameHash = _reader.ReadUInt64();
        var fileCount = _reader.ReadUInt32();

        if (header.Version >= 105)
        {
            SkipPadding(4);
        }

        var fileOffset = _reader.ReadUInt32();

        if (header.Version >= 105)
        {
            SkipPadding(4);
        }

        var folderOffset = fileOffset - header.TotalFileNameLength;

        using var folderScope = new ScopedOffset(_reader.BaseStream, folderOffset);
        string? folderName = null;
        if (header.ArchiveFlags.HasFlag(BsaArchiveFlags.IncludesDirectoryNames))
        {
            folderName = ReadBzString();
        }

        List<BsaFileRecord> files = [with((int)fileCount)];
        List<long> fileOffsets = [with((int)fileCount)];
        for (int i = 0; i < fileCount; i++)
        {
            fileOffsets.Add(_reader.BaseStream.Position);
            archive.AddFile(ReadBsaFile(header));
        }

        return new BsaFolderRecord(header, nameHash, fileCount, fileOffset, folderName, fileOffsets);
    }

    private BsaFileRecord ReadBsaFile(BinaryBsaHeader header)
    {
        ulong nameHash = _reader.ReadUInt64();
        uint fileSize = _reader.ReadUInt32();
        uint fileOffset = _reader.ReadUInt32();

        return new BsaFileRecord(header, nameHash, fileSize, fileOffset);
    }

    private BsaFileData ReadBsaFileData(BinaryBsaHeader header, BsaFileRecord fileRecord)
    {
        using var fileScope = new ScopedOffset(_reader.BaseStream, fileRecord.Offset);

        string? fileName = null;
        if (header.ArchiveFlags.HasFlag(BsaArchiveFlags.EmbedFileNames))
        {
            fileName = ReadBString();
        }

        return fileRecord.IsCompressed
            ? new BsaCompressedFile(header, _reader.BaseStream, fileName, fileRecord.Size, _reader.ReadUInt32(), _reader.ReadUInt32())
            : new BsaUncompressedFile(header, _reader.BaseStream, fileName, fileRecord.Size, (uint)_reader.BaseStream.Position);
    }

    private void SkipPadding(long size) => _reader.BaseStream.Seek(size, SeekOrigin.Current);

    private string ReadBzString()
    {
        var length = _reader.ReadByte();
        var bytes = _reader.ReadBytes(length);
        return Encoding.UTF8.GetString(bytes);
    }

    private string ReadBString()
    {
        var length = _reader.ReadByte();
        var bytes = _reader.ReadBytes((int)length);
        return Encoding.UTF8.GetString(bytes);
    }

    private string ReadZString()
    {
        List<byte> bytes = [];
        while (true)
        {
            var b = _reader.ReadByte();
            if (b == '\0')
            {
                break;
            }
            bytes.Add(b);
        }

        return Encoding.UTF8.GetString([.. bytes]);
    }

    public void Dispose() => _reader.Dispose();
}
