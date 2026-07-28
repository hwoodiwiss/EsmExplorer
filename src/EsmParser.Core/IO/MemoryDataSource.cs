namespace EsmParser.Core.IO;

/// <summary>An <see cref="IDataSource"/> over an in-memory buffer.</summary>
public sealed class MemoryDataSource : IDataSource
{
    private readonly ReadOnlyMemory<byte> _data;

    public MemoryDataSource(ReadOnlyMemory<byte> data, string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        _data = data;
        Name = name;
    }

    public string Name { get; }

    public long Length => _data.Length;

    public ValueTask<ParseResult<Unit>> ReadExactlyAsync(long offset, Memory<byte> destination, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (DataSourceGuard.CheckRange(offset, destination.Length, Length) is { } error)
        {
            return ValueTask.FromResult<ParseResult<Unit>>(error);
        }

        _data.Span.Slice((int)offset, destination.Length).CopyTo(destination.Span);
        return ValueTask.FromResult<ParseResult<Unit>>(new Success<Unit>(Unit.Value));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
