using System.Buffers.Binary;
using BethesdaArchiveParser.Core.Extensions;

namespace BethesdaArchiveParser.Core.Reader;

/// <summary>Reads a caller-owned, readable, seekable stream. Operations on one stream must not overlap.</summary>
public sealed class BsaArchiveReader
{
    private readonly Stream _stream;

    public BsaArchiveReader(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead || !stream.CanSeek)
        {
            throw new ArgumentException("BSA streams must be readable and seekable.", nameof(stream));
        }
        _stream = stream;
    }

    public BsaArchive ReadBsaArchive() => ReadArchive(false, default).AsSync();
    public Task<BsaArchive> ReadBsaArchiveAsync(CancellationToken cancellationToken = default) => ReadArchive(true, cancellationToken).AsTask();

    private async ValueTask<BsaArchive> ReadArchive(bool isAsync, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _stream.Position = 0;
        var header = await BsaReader.ReadHeader(_stream, isAsync, cancellationToken).ConfigureAwait(false);
        return await new BsaContentReader(header, new BsaReader(_stream, isAsync, cancellationToken)).ReadBsaArchive().ConfigureAwait(false);
    }

    public byte[] ReadBsaFile(BsaFileRecord file) => ReadFile(file, false, default).AsSync();
    public Task<byte[]> ReadBsaFileAsync(BsaFileRecord file, CancellationToken cancellationToken = default) => ReadFile(file, true, cancellationToken).AsTask();

    /// <summary>Streams an entry to the destination with bounded memory, leaving both streams open.</summary>
    public long CopyBsaFileTo(BsaFileRecord file, Stream destination) => CopyFile(file, destination, false, default).AsSync();
    public ValueTask<long> CopyBsaFileToAsync(BsaFileRecord file, Stream destination, CancellationToken cancellationToken = default) =>
        CopyFile(file, destination, true, cancellationToken);

    private async ValueTask<byte[]> ReadFile(BsaFileRecord file, bool isAsync, CancellationToken cancellationToken)
    {
        var (offset, storedSize, expandedSize) = await GetPayload(file, isAsync, cancellationToken).ConfigureAwait(false);
        if (expandedSize > Array.MaxLength)
        {
            throw new InvalidDataException("Entry is too large for a byte array; use streaming extraction.");
        }
        byte[] bytes = new byte[(int)expandedSize];
        using var output = new MemoryStream(bytes, writable: true);
        await BsaPayload.Copy(_stream, output, file.Header.Version, offset, storedSize,
            expandedSize, file.IsCompressed, isAsync, cancellationToken).ConfigureAwait(false);
        return bytes;
    }

    private async ValueTask<long> CopyFile(BsaFileRecord file, Stream destination, bool isAsync, CancellationToken cancellationToken)
    {
        var (offset, storedSize, expandedSize) = await GetPayload(file, isAsync, cancellationToken).ConfigureAwait(false);
        return await BsaPayload.Copy(_stream, destination, file.Header.Version, offset, storedSize,
            expandedSize, file.IsCompressed, isAsync, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<(long Offset, uint StoredSize, uint ExpandedSize)> GetPayload(BsaFileRecord file, bool isAsync, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        cancellationToken.ThrowIfCancellationRequested();
        uint size = file.StoredSize;
        if (file.Offset > _stream.Length || size > _stream.Length - file.Offset)
        {
            throw new InvalidDataException("File record is outside the archive.");
        }
        using var scope = new ScopedOffset(_stream, file.Offset);
        byte[] scratch = new byte[4];
        if (file.Header.Version >= 104 && file.Header.ArchiveFlags.HasFlag(BsaArchiveFlags.EmbedFileNames))
        {
            if (size == 0)
            {
                throw new InvalidDataException("Missing embedded filename.");
            }
            if (isAsync)
            {
                await _stream.ReadExactlyAsync(scratch.AsMemory(0, 1), cancellationToken).ConfigureAwait(false);
            }
            else
            {
                _stream.ReadExactly(scratch.AsSpan(0, 1));
            }
            uint prefix = (uint)scratch[0] + 1;
            if (prefix > size)
            {
                throw new InvalidDataException("Embedded filename exceeds the file size.");
            }
            size -= prefix;
            _stream.Seek(scratch[0], SeekOrigin.Current);
        }
        uint expanded = size;
        if (file.IsCompressed)
        {
            if (size < 4)
            {
                throw new InvalidDataException("Missing uncompressed size.");
            }
            if (isAsync)
            {
                await _stream.ReadExactlyAsync(scratch, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                _stream.ReadExactly(scratch);
            }
            expanded = BinaryPrimitives.ReadUInt32LittleEndian(scratch);
            size -= 4;
        }
        return (_stream.Position, size, expanded);
    }
}
