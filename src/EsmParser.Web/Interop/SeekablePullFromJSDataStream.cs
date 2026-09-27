using Microsoft.JSInterop;

namespace EsmParser.Web.Interop;

/// <summary>A seekable, async-only stream over a module-owned browser File snapshot.</summary>
internal sealed class SeekablePullFromJSDataStream(IJSObjectReference module, int id, long length) : Stream
{
    private long _position;
    private bool _disposed;

    public static async ValueTask<SeekablePullFromJSDataStream?> OpenAsync(IJSObjectReference module, string path)
    {
        try
        {
            var file = await module.InvokeAsync<OpenedFile?>("openFile", path);
            return file is null ? null : new SeekablePullFromJSDataStream(module, file.Id, file.Length);
        }
        catch (JSException ex)
        {
            throw new IOException($"Could not open browser file '{path}'.", ex);
        }
    }

    public override bool CanRead => !_disposed;
    public override bool CanSeek => !_disposed;
    public override bool CanWrite => false;
    public override long Length => length;
    public override long Position
    {
        get => _position;
        set => Seek(value, SeekOrigin.Begin);
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        long position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => checked(_position + offset),
            SeekOrigin.End => checked(length + offset),
            _ => throw new ArgumentOutOfRangeException(nameof(origin)),
        };
        if (position < 0 || position > length)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }
        return _position = position;
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException("Use asynchronous reads for browser files.");
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        int count = (int)Math.Min(Math.Min(buffer.Length, 64 * 1024), length - _position);
        if (count == 0)
        {
            return 0;
        }
        byte[] bytes;
        try
        {
            bytes = await module.InvokeAsync<byte[]>("readFileChunk", cancellationToken, id, _position, count);
        }
        catch (JSException ex)
        {
            throw new IOException("Could not read browser file chunk.", ex);
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (bytes.Length != count)
        {
            throw new EndOfStreamException("Browser file returned an incomplete chunk.");
        }
        bytes.CopyTo(buffer);
        _position += count;
        return count;
    }

    public override async ValueTask DisposeAsync()
    {
        try
        {
            if (!_disposed)
            {
                _disposed = true;
                try
                {
                    await module.InvokeVoidAsync("closeFile", id);
                }
                catch (JSDisconnectedException)
                {
                    // Browser/runtime has already released its resources.
                }
            }
        }
        finally
        {
            await base.DisposeAsync();
            GC.SuppressFinalize(this);
        }
    }

    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    private sealed record OpenedFile(int Id, long Length);
}
