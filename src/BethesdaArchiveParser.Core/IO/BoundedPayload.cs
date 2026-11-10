using System.Buffers;

namespace BethesdaArchiveParser.Core.IO;

// Shared by every archive format: copies one stored payload, optionally through a decoder, with exact-size checks.
internal static class BoundedPayload
{
    public static void ValidateDestination(Stream source, Stream destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite)
        {
            throw new ArgumentException("Destination must be writable.", nameof(destination));
        }
        if (ReferenceEquals(source, destination))
        {
            throw new ArgumentException("Destination cannot be the archive stream.", nameof(destination));
        }
    }

    public static async ValueTask Write(Stream destination, ReadOnlyMemory<byte> bytes, bool isAsync, CancellationToken cancellationToken)
    {
        if (isAsync)
        {
            await destination.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            destination.Write(bytes.Span);
        }
    }

    public static async ValueTask<long> Copy(Stream source, Stream destination, long offset, long storedSize, long expandedSize,
        Func<Stream, Stream>? createDecoder, bool isAsync, CancellationToken cancellationToken)
    {
        ValidateDestination(source, destination);
        if (offset < 0 || storedSize < 0 || offset > source.Length || storedSize > source.Length - offset)
        {
            throw new InvalidDataException("File payload is outside the archive.");
        }
        using var scope = new ScopedOffset(source, offset);
        using var bounded = new BoundedReadStream(source, storedSize);
        using Stream? decoder = createDecoder?.Invoke(bounded);
        Stream input = decoder ?? bounded;
        byte[] buffer = ArrayPool<byte>.Shared.Rent(81920);
        try
        {
            long remaining = expandedSize;
            while (remaining > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int count = (int)Math.Min(remaining, buffer.Length);
                int read = isAsync
                    ? await input.ReadAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false)
                    : input.Read(buffer, 0, count);
                if (read == 0)
                {
                    throw new InvalidDataException("File payload is shorter than its declared size.");
                }
                if (isAsync)
                {
                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    destination.Write(buffer, 0, read);
                }
                remaining -= read;
            }
            // Also validates decoder trailers and detects an understated expanded size.
            int extra = isAsync
                ? await input.ReadAsync(buffer.AsMemory(0, 1), cancellationToken).ConfigureAwait(false)
                : input.Read(buffer, 0, 1);
            if (extra != 0)
            {
                throw new InvalidDataException("File payload exceeds its declared size.");
            }
            return expandedSize;
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }
}
