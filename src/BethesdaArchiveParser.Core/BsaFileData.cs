using K4os.Compression.LZ4.Streams;
using System.IO.Compression;

namespace BethesdaArchiveParser.Core;

public sealed record BsaCompressedFile(BinaryBsaHeader Header, Stream FileStream, string? Name, uint CompressedSize, uint UncompressedSize, uint DataOffset)
{
    public int CopyTo(Stream destination)
    {
        byte[] compressedBuffer = new byte[CompressedSize];
        FileStream.Seek(DataOffset, SeekOrigin.Begin);
        FileStream.ReadExactly(compressedBuffer);
        using var compressedStream = new MemoryStream(compressedBuffer);
        using Stream decompressionStream = Header.Version <= 104
            ? new ZLibStream(compressedStream, CompressionMode.Decompress, true)
            : LZ4Stream.Decode(compressedStream, leaveOpen: true);
        decompressionStream.CopyTo(destination);
        return (int)UncompressedSize;
    }

    public byte[] GetContent()
    {
        using var memoryStream = new MemoryStream((int)UncompressedSize);
        CopyTo(memoryStream);
        return memoryStream.ToArray();
    }
}

public sealed record BsaUncompressedFile(BinaryBsaHeader Header, Stream FileStream, string? Name, uint UncompressedSize, uint DataOffset)
{
    public int CopyTo(Stream destination)
    {
        FileStream.Seek(DataOffset, SeekOrigin.Begin);
        FileStream.CopyTo(destination, (int)UncompressedSize);
        return (int)UncompressedSize;
    }

    public byte[] GetContent()
    {
        using var memoryStream = new MemoryStream((int)UncompressedSize);
        CopyTo(memoryStream);
        return memoryStream.ToArray();
    }
}


public readonly union BsaFileData(BsaCompressedFile, BsaUncompressedFile);
