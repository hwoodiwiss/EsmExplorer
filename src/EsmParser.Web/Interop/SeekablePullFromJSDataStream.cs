// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.JSInterop;

namespace EsmParser.Web.Interop;

/// <Summary>
/// A stream that pulls each chunk on demand using JavaScript interop. This implementation is used for
/// WebAssembly and WebView applications.
/// </Summary>
internal sealed class SeekablePullFromJSDataStream : Stream
{
    private readonly IJSRuntime _runtime;
    private readonly IJSStreamReference _jsStreamReference;
    private readonly long _totalLength;
    private readonly CancellationToken _streamCancellationToken;
    private long _offset;

    public static SeekablePullFromJSDataStream CreateJSDataStream(
        IJSRuntime runtime,
        IJSStreamReference jsStreamReference,
        long totalLength,
        CancellationToken cancellationToken = default)
    {
        var jsDataStream = new SeekablePullFromJSDataStream(runtime, jsStreamReference, totalLength, cancellationToken);
        return jsDataStream;
    }

    private SeekablePullFromJSDataStream(
        IJSRuntime runtime,
        IJSStreamReference jsStreamReference,
        long totalLength,
        CancellationToken cancellationToken)
    {
        _runtime = runtime;
        _jsStreamReference = jsStreamReference;
        _totalLength = totalLength;
        _streamCancellationToken = cancellationToken;
        _offset = 0;
    }

    public override bool CanRead => true;

    public override bool CanSeek => true;

    public override bool CanWrite => false;

    public override long Length => _totalLength;

    public override long Position
    {
        get => _offset;
        set => _offset = value;
    }

    public override void Flush()
    {
        // No-op
    }

    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public override int Read(byte[] buffer, int offset, int count)
        => throw new NotSupportedException("Synchronous reads are not supported.");

    public override long Seek(long offset, SeekOrigin origin)
    {
        long newOffset = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _offset + offset,
            SeekOrigin.End => _totalLength - offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin), "Invalid seek origin.")
        };

        if (newOffset < 0 || newOffset > _totalLength)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), "Seek position is out of bounds.");
        }

        _offset = newOffset;
        return _offset;
    }

    public override void SetLength(long value)
        => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count)
        => throw new NotSupportedException();

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => await ReadAsync(buffer.AsMemory(offset, count), cancellationToken);

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var bytesRead = await RequestDataFromJSAsync(buffer.Length);
        ThrowIfCancellationRequested(cancellationToken);
        bytesRead.CopyTo(buffer);

        return bytesRead.Length;
    }

    private void ThrowIfCancellationRequested(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested ||
            _streamCancellationToken.IsCancellationRequested)
        {
            throw new TaskCanceledException();
        }
    }

    private async ValueTask<byte[]> RequestDataFromJSAsync(int numBytesToRead)
    {
        numBytesToRead = (int)Math.Min(numBytesToRead, _totalLength - _offset);
#pragma warning disable BL0016 // Unguarded JS interop call
        var bytesRead = await _runtime.InvokeAsync<byte[]>("Blazor._internal.getJSDataStreamChunk", _jsStreamReference, _offset, numBytesToRead);
#pragma warning restore BL0016 // Unguarded JS interop call
        if (bytesRead.Length != numBytesToRead)
        {
            throw new EndOfStreamException("Failed to read the requested number of bytes from the stream.");
        }

        _offset += bytesRead.Length;
        if (_offset == _totalLength)
        {
            Dispose(true);
        }
        return bytesRead;
    }
}
