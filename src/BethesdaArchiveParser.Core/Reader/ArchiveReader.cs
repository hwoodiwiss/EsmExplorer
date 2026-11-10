using BethesdaArchiveParser.Core.Ba2;
using BethesdaArchiveParser.Core.Ba2.Reader;
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
    private ArchiveFormat _format;
    private BsaArchiveReader? _bsa;
    private Ba2ArchiveReader? _ba2;

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

    /// <summary>Reads a whole entry. BA2 textures are returned as complete DDS files.</summary>
    public byte[] ReadFile(ArchiveEntry entry) => Select(entry) switch
    {
        BsaFileRecord bsa => Bsa.ReadBsaFile(bsa),
        Ba2FileRecord ba2 => Ba2.ReadBa2File(ba2),
        _ => throw new ArgumentException("Unsupported archive entry.", nameof(entry)),
    };

    public Task<byte[]> ReadFileAsync(ArchiveEntry entry, CancellationToken cancellationToken = default) => Select(entry) switch
    {
        BsaFileRecord bsa => Bsa.ReadBsaFileAsync(bsa, cancellationToken),
        Ba2FileRecord ba2 => Ba2.ReadBa2FileAsync(ba2, cancellationToken),
        _ => throw new ArgumentException("Unsupported archive entry.", nameof(entry)),
    };

    /// <summary>Streams an entry to the destination, leaving both streams open.</summary>
    public long CopyFileTo(ArchiveEntry entry, Stream destination) => Select(entry) switch
    {
        BsaFileRecord bsa => Bsa.CopyBsaFileTo(bsa, destination),
        Ba2FileRecord ba2 => Ba2.CopyBa2FileTo(ba2, destination),
        _ => throw new ArgumentException("Unsupported archive entry.", nameof(entry)),
    };

    public ValueTask<long> CopyFileToAsync(ArchiveEntry entry, Stream destination, CancellationToken cancellationToken = default) => Select(entry) switch
    {
        BsaFileRecord bsa => Bsa.CopyBsaFileToAsync(bsa, destination, cancellationToken),
        Ba2FileRecord ba2 => Ba2.CopyBa2FileToAsync(ba2, destination, cancellationToken),
        _ => throw new ArgumentException("Unsupported archive entry.", nameof(entry)),
    };

    private BsaArchiveReader Bsa => _bsa ??= new BsaArchiveReader(_stream);
    private Ba2ArchiveReader Ba2 => _ba2 ??= new Ba2ArchiveReader(_stream);

    private async ValueTask<Archive> Read(bool isAsync, CancellationToken cancellationToken)
    {
        var format = await Detect(_stream, isAsync, cancellationToken).ConfigureAwait(false);
        Archive archive = format switch
        {
            ArchiveFormat.Bsa => new Archive(isAsync
                ? await Bsa.ReadBsaArchiveAsync(cancellationToken).ConfigureAwait(false)
                : Bsa.ReadBsaArchive()),
            ArchiveFormat.Ba2 => new Archive(isAsync
                ? await Ba2.ReadBa2ArchiveAsync(cancellationToken).ConfigureAwait(false)
                : Ba2.ReadBa2Archive()),
            _ => throw new InvalidDataException("Unrecognised archive signature."),
        };
        _format = format;
        return archive;
    }

    // Once an archive has been read, entries from another format are rejected rather than misread.
    private object Select(ArchiveEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var (record, format) = entry.File switch
        {
            BsaFileRecord bsa => ((object)bsa, ArchiveFormat.Bsa),
            Ba2FileRecord ba2 => (ba2, ArchiveFormat.Ba2),
            _ => throw new ArgumentException("Unsupported archive entry.", nameof(entry)),
        };
        if (_format != ArchiveFormat.Unknown && _format != format)
        {
            throw new ArgumentException($"The entry belongs to a {format} archive, but this reader holds a {_format} archive.", nameof(entry));
        }
        return record;
    }

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
