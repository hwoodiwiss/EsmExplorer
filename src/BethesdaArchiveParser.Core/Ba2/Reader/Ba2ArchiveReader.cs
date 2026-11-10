using System.IO.Compression;
using BethesdaArchiveParser.Core.IO;
using K4os.Compression.LZ4;

namespace BethesdaArchiveParser.Core.Ba2.Reader;

/// <summary>Reads a caller-owned, readable, seekable stream. Operations on one stream must not overlap.</summary>
public sealed class Ba2ArchiveReader
{
    private readonly Stream _stream;

    public Ba2ArchiveReader(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead || !stream.CanSeek)
        {
            throw new ArgumentException("BA2 streams must be readable and seekable.", nameof(stream));
        }
        _stream = stream;
    }

    public Ba2Archive ReadBa2Archive() => Ba2IndexReader.Read(_stream, false, default).AsSync();
    public Task<Ba2Archive> ReadBa2ArchiveAsync(CancellationToken cancellationToken = default) =>
        Ba2IndexReader.Read(_stream, true, cancellationToken).AsTask();

    /// <summary>Reads a whole entry. Textures are returned as complete DDS files.</summary>
    public byte[] ReadBa2File(Ba2FileRecord file) => ReadFile(file, false, default).AsSync();
    public Task<byte[]> ReadBa2FileAsync(Ba2FileRecord file, CancellationToken cancellationToken = default) =>
        ReadFile(file, true, cancellationToken).AsTask();

    /// <summary>
    /// Streams an entry to the destination, leaving both streams open. zlib and stored chunks use bounded memory;
    /// LZ4 block chunks are decoded whole, so memory is bounded by the largest chunk.
    /// </summary>
    public long CopyBa2FileTo(Ba2FileRecord file, Stream destination) => CopyFile(file, destination, false, default).AsSync();
    public ValueTask<long> CopyBa2FileToAsync(Ba2FileRecord file, Stream destination, CancellationToken cancellationToken = default) =>
        CopyFile(file, destination, true, cancellationToken);

    private async ValueTask<byte[]> ReadFile(Ba2FileRecord file, bool isAsync, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (file.ExpandedLength > Array.MaxLength)
        {
            throw new InvalidDataException("Entry is too large for a byte array; use streaming extraction.");
        }
        byte[] bytes = new byte[(int)file.ExpandedLength];
        using var output = new MemoryStream(bytes, writable: true);
        await CopyFile(file, output, isAsync, cancellationToken).ConfigureAwait(false);
        return bytes;
    }

    private async ValueTask<long> CopyFile(Ba2FileRecord file, Stream destination, bool isAsync, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        BoundedPayload.ValidateDestination(_stream, destination);
        cancellationToken.ThrowIfCancellationRequested();
        long written = 0;
        if (file.Texture is { } texture)
        {
            await BoundedPayload.Write(destination, DdsHeader.Create(texture), isAsync, cancellationToken).ConfigureAwait(false);
            written += DdsHeader.Length;
        }
        foreach (var chunk in file.Chunks)
        {
            written += await CopyChunk(file.Header, chunk, destination, isAsync, cancellationToken).ConfigureAwait(false);
        }
        return written;
    }

    private async ValueTask<long> CopyChunk(Ba2Header header, Ba2Chunk chunk, Stream destination, bool isAsync, CancellationToken cancellationToken)
    {
        if (chunk.Offset > long.MaxValue)
        {
            throw new InvalidDataException("File payload is outside the archive.");
        }
        long offset = (long)chunk.Offset;
        if (!chunk.IsCompressed)
        {
            return await BoundedPayload.Copy(_stream, destination, offset, chunk.UnpackedSize, chunk.UnpackedSize,
                null, isAsync, cancellationToken).ConfigureAwait(false);
        }
        if (header.CompressionMethod == Ba2CompressionMethod.Zlib)
        {
            return await BoundedPayload.Copy(_stream, destination, offset, chunk.PackedSize, chunk.UnpackedSize,
                static input => new ZLibStream(input, CompressionMode.Decompress, true), isAsync, cancellationToken).ConfigureAwait(false);
        }
        return await CopyLz4Chunk(chunk, offset, destination, isAsync, cancellationToken).ConfigureAwait(false);
    }

    // LZ4 blocks carry no framing, so the whole block must be decoded at once.
    private async ValueTask<long> CopyLz4Chunk(Ba2Chunk chunk, long offset, Stream destination, bool isAsync, CancellationToken cancellationToken)
    {
        if (offset > _stream.Length || chunk.PackedSize > _stream.Length - offset)
        {
            throw new InvalidDataException("File payload is outside the archive.");
        }
        if (chunk.PackedSize > Array.MaxLength || chunk.UnpackedSize > Array.MaxLength)
        {
            throw new InvalidDataException("LZ4 chunk is too large.");
        }
        byte[] packed = new byte[chunk.PackedSize];
        using (new ScopedOffset(_stream, offset))
        {
            if (isAsync)
            {
                await _stream.ReadExactlyAsync(packed, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                _stream.ReadExactly(packed);
            }
        }
        byte[] unpacked = new byte[chunk.UnpackedSize];
        int decoded = LZ4Codec.Decode(packed, unpacked);
        if (decoded != unpacked.Length)
        {
            throw new InvalidDataException("LZ4 chunk does not match its declared size.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        await BoundedPayload.Write(destination, unpacked, isAsync, cancellationToken).ConfigureAwait(false);
        return decoded;
    }
}
