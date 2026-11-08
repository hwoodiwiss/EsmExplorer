using BethesdaArchiveParser.Core.Bsa;
using BethesdaArchiveParser.Core.Bsa.Reader;
using BethesdaArchiveParser.Core.IO;

namespace BethesdaArchiveParser.Core.Reader;

/// <summary>
/// Reads any supported archive format from a caller-owned, readable, seekable stream, which is left open.
/// Operations on one stream must not overlap. Delegates to the format-specific readers.
/// </summary>
public sealed class ArchiveReader
{
    private readonly Stream _stream;
    private BsaArchiveReader? _bsa;

    public ArchiveReader(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead || !stream.CanSeek)
        {
            throw new ArgumentException("Archive streams must be readable and seekable.", nameof(stream));
        }
        _stream = stream;
    }

    /// <summary>Identifies the archive format from its signature, restoring the stream position.</summary>
    public static ArchiveFormat DetectFormat(Stream stream) => Detect(stream, false, default).AsSync();

    /// <inheritdoc cref="DetectFormat(Stream)"/>
    public static ValueTask<ArchiveFormat> DetectFormatAsync(Stream stream, CancellationToken cancellationToken = default) =>
        Detect(stream, true, cancellationToken);

    public Archive ReadArchive() => Read(false, default).AsSync();
    public Task<Archive> ReadArchiveAsync(CancellationToken cancellationToken = default) => Read(true, cancellationToken).AsTask();

    public byte[] ReadFile(ArchiveEntry entry) => GetBsaReader(entry).ReadBsaFile(GetBsaRecord(entry));
    public Task<byte[]> ReadFileAsync(ArchiveEntry entry, CancellationToken cancellationToken = default) =>
        GetBsaReader(entry).ReadBsaFileAsync(GetBsaRecord(entry), cancellationToken);

    /// <summary>Streams an entry to the destination with bounded memory, leaving both streams open.</summary>
    public long CopyFileTo(ArchiveEntry entry, Stream destination) => GetBsaReader(entry).CopyBsaFileTo(GetBsaRecord(entry), destination);
    public ValueTask<long> CopyFileToAsync(ArchiveEntry entry, Stream destination, CancellationToken cancellationToken = default) =>
        GetBsaReader(entry).CopyBsaFileToAsync(GetBsaRecord(entry), destination, cancellationToken);

    private async ValueTask<Archive> Read(bool isAsync, CancellationToken cancellationToken)
    {
        var format = await Detect(_stream, isAsync, cancellationToken).ConfigureAwait(false);
        switch (format)
        {
            case ArchiveFormat.Bsa:
                _bsa ??= new BsaArchiveReader(_stream);
                var bsa = isAsync
                    ? await _bsa.ReadBsaArchiveAsync(cancellationToken).ConfigureAwait(false)
                    : _bsa.ReadBsaArchive();
                return new Archive(bsa);
            case ArchiveFormat.Ba2:
                throw new NotSupportedException("BA2 archives are not supported yet.");
            default:
                throw new InvalidDataException("Unrecognised archive signature.");
        }
    }

    private BsaArchiveReader GetBsaReader(ArchiveEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return _bsa ??= new BsaArchiveReader(_stream);
    }

    private static BsaFileRecord GetBsaRecord(ArchiveEntry entry) => entry.File switch
    {
        BsaFileRecord record => record,
        _ => throw new ArgumentException("The entry does not belong to a BSA archive.", nameof(entry)),
    };

    private static async ValueTask<ArchiveFormat> Detect(Stream stream, bool isAsync, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        cancellationToken.ThrowIfCancellationRequested();
        if (!stream.CanRead || !stream.CanSeek)
        {
            throw new ArgumentException("Archive streams must be readable and seekable.", nameof(stream));
        }
        byte[] magic = new byte[4];
        int read;
        using (new ScopedOffset(stream, 0))
        {
            read = isAsync
                ? await stream.ReadAtLeastAsync(magic, magic.Length, false, cancellationToken).ConfigureAwait(false)
                : stream.ReadAtLeast(magic, magic.Length, false);
        }
        if (read < magic.Length)
        {
            return ArchiveFormat.Unknown;
        }
        if (magic.AsSpan().SequenceEqual("BSA\0"u8))
        {
            return ArchiveFormat.Bsa;
        }
        return magic.AsSpan().SequenceEqual("BTDX"u8) ? ArchiveFormat.Ba2 : ArchiveFormat.Unknown;
    }
}
