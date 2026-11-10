namespace BethesdaArchiveParser.Core.Tests;

// Shared I/O-mode streams for archive tests.
internal sealed class ModeStream(byte[] bytes, bool asyncOnly) : MemoryStream(bytes, 0, bytes.Length, false, true)
{
    public int AsyncReads { get; private set; }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (asyncOnly)
        {
            throw new InvalidOperationException("Synchronous I/O in async mode.");
        }
        return base.Read(buffer, offset, Math.Min(count, 7));
    }

    public override int Read(Span<byte> buffer)
    {
        if (asyncOnly)
        {
            throw new InvalidOperationException("Synchronous I/O in async mode.");
        }
        return base.Read(buffer[..Math.Min(buffer.Length, 7)]);
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (!asyncOnly)
        {
            throw new InvalidOperationException("Async I/O in sync mode.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        AsyncReads++;
        await Task.Yield();
        // Copy directly to avoid MemoryStream dispatching back to the sync overrides.
        int count = (int)Math.Min(Math.Min(buffer.Length, 7), Length - Position);
        GetBuffer().AsMemory((int)Position, count).CopyTo(buffer);
        Position += count;
        return count;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
}

internal sealed class AsyncOutput : MemoryStream
{
    public override void Write(byte[] buffer, int offset, int count) => throw new InvalidOperationException("Synchronous output in async mode.");
    public override void Write(ReadOnlySpan<byte> buffer) => throw new InvalidOperationException("Synchronous output in async mode.");

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Yield();
        byte[] bytes = buffer.ToArray();
        base.Write(bytes, 0, bytes.Length);
    }
}
