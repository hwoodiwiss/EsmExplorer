namespace EsmParser.Core.IO;

/// <summary>
/// Random-access read abstraction over plugin bytes. Implementations exist for
/// in-memory buffers, files, and (in the web host) browser blobs.
/// </summary>
/// <remarks>
/// Reads report failures (out-of-range requests, I/O faults) as <see cref="ParseError"/>
/// results rather than exceptions; argument contract violations (negative offsets,
/// null buffers) throw. Implementations must support concurrent readers.
/// </remarks>
public interface IDataSource : IAsyncDisposable
{
    /// <summary>A human-meaningful name for the source, typically the file name.</summary>
    string Name { get; }

    /// <summary>Total length of the underlying data, in bytes.</summary>
    long Length { get; }

    /// <summary>
    /// Fills <paramref name="destination"/> with the bytes at <paramref name="offset"/>,
    /// failing with <see cref="ParseErrorKind.UnexpectedEndOfData"/> when the requested
    /// range extends past <see cref="Length"/>.
    /// </summary>
    ValueTask<ParseResult<Unit>> ReadExactlyAsync(long offset, Memory<byte> destination, CancellationToken cancellationToken = default);
}
