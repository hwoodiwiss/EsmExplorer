namespace EsmParser.Core.IO;

/// <summary>
/// A page-caching decorator for <see cref="IDataSource"/>, intended for sources with
/// high per-read cost (e.g. browser blob slices crossing the JS interop boundary).
/// Pages are evicted least-recently-used.
/// </summary>
public sealed class CachingDataSource : IDataSource
{
    private readonly IDataSource _inner;
    private readonly bool _disposeInner;
    private readonly int _pageSize;
    private readonly int _maxPages;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<long, LinkedListNode<Page>> _pagesByIndex = [];
    private readonly LinkedList<Page> _recency = new();
    private long _pageLoads;

    public CachingDataSource(IDataSource inner, int pageSize = 64 * 1024, int maxPages = 256, bool disposeInner = true)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxPages, 1);
        _inner = inner;
        _pageSize = pageSize;
        _maxPages = maxPages;
        _disposeInner = disposeInner;
    }

    public string Name => _inner.Name;

    public long Length => _inner.Length;

    /// <summary>Number of page loads issued to the inner source; a cache-effectiveness diagnostic.</summary>
    public long PageLoadCount => Interlocked.Read(ref _pageLoads);

    public async ValueTask<ParseResult<Unit>> ReadExactlyAsync(long offset, Memory<byte> destination, CancellationToken cancellationToken = default)
    {
        if (DataSourceGuard.CheckRange(offset, destination.Length, Length) is { } rangeError)
        {
            return rangeError;
        }

        int copied = 0;
        while (copied < destination.Length)
        {
            long position = offset + copied;
            long pageIndex = position / _pageSize;
            int offsetInPage = (int)(position - (pageIndex * _pageSize));

            ParseResult<byte[]> pageResult = await GetPageAsync(pageIndex, cancellationToken);
            if (!pageResult.TryGet(out byte[]? page, out ParseError? error))
            {
                return error;
            }

            int available = page.Length - offsetInPage;
            if (available <= 0)
            {
                // Only possible if the inner source shrank after construction.
                return new ParseError(ParseErrorKind.Io, "The underlying source returned a short page.", position);
            }

            int toCopy = Math.Min(available, destination.Length - copied);
            page.AsMemory(offsetInPage, toCopy).CopyTo(destination[copied..]);
            copied += toCopy;
        }

        return new Success<Unit>(Unit.Value);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposeInner)
        {
            await _inner.DisposeAsync();
        }

        _gate.Dispose();
    }

    private async ValueTask<ParseResult<byte[]>> GetPageAsync(long pageIndex, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_pagesByIndex.TryGetValue(pageIndex, out LinkedListNode<Page>? node))
            {
                _recency.Remove(node);
                _recency.AddFirst(node);
                return new Success<byte[]>(node.Value.Data);
            }

            long start = pageIndex * _pageSize;
            int length = (int)Math.Min(_pageSize, Length - start);
            byte[] data = new byte[length];
            ParseResult<Unit> read = await _inner.ReadExactlyAsync(start, data, cancellationToken);
            if (read is ParseError error)
            {
                return error;
            }

            Interlocked.Increment(ref _pageLoads);
            var newNode = _recency.AddFirst(new Page(pageIndex, data));
            _pagesByIndex[pageIndex] = newNode;

            if (_pagesByIndex.Count > _maxPages)
            {
                LinkedListNode<Page> oldest = _recency.Last!;
                _recency.RemoveLast();
                _pagesByIndex.Remove(oldest.Value.Index);
            }

            return new Success<byte[]>(data);
        }
        finally
        {
            _gate.Release();
        }
    }

    private sealed record Page(long Index, byte[] Data);
}
