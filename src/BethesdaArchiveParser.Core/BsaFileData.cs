using BethesdaArchiveParser.Core.Extensions;
using BethesdaArchiveParser.Core.Reader;

namespace BethesdaArchiveParser.Core;

// Sizes here describe the payload only: embedded names and the expanded-size prefix are excluded.
public sealed record BsaCompressedFile(BinaryBsaHeader Header, Stream FileStream, string? Name, uint CompressedSize, uint UncompressedSize, uint DataOffset)
{
    public int CopyTo(Stream destination) => checked((int)BsaPayload.Copy(FileStream, destination, Header.Version,
        DataOffset, CompressedSize, UncompressedSize, true, false, default).AsSync());

    public async ValueTask<long> CopyToAsync(Stream destination, CancellationToken cancellationToken = default) =>
        await BsaPayload.Copy(FileStream, destination, Header.Version, DataOffset, CompressedSize,
            UncompressedSize, true, true, cancellationToken).ConfigureAwait(false);

    public byte[] GetContent()
    {
        byte[] bytes = new byte[checked((int)UncompressedSize)];
        using var output = new MemoryStream(bytes, writable: true);
        CopyTo(output);
        return bytes;
    }
}

public sealed record BsaUncompressedFile(BinaryBsaHeader Header, Stream FileStream, string? Name, uint UncompressedSize, uint DataOffset)
{
    public int CopyTo(Stream destination) => checked((int)BsaPayload.Copy(FileStream, destination, Header.Version,
        DataOffset, UncompressedSize, UncompressedSize, false, false, default).AsSync());

    public async ValueTask<long> CopyToAsync(Stream destination, CancellationToken cancellationToken = default) =>
        await BsaPayload.Copy(FileStream, destination, Header.Version, DataOffset, UncompressedSize,
            UncompressedSize, false, true, cancellationToken).ConfigureAwait(false);

    public byte[] GetContent()
    {
        byte[] bytes = new byte[checked((int)UncompressedSize)];
        using var output = new MemoryStream(bytes, writable: true);
        CopyTo(output);
        return bytes;
    }
}

public readonly union BsaFileData(BsaCompressedFile, BsaUncompressedFile);
