namespace BethesdaArchiveParser.Core;

internal sealed class ScopedOffset : IDisposable
{
    private readonly Stream _stream;
    private readonly long _originalPosition;

    public ScopedOffset(Stream stream, long offset, SeekOrigin origin = SeekOrigin.Begin)
    {
        if (!stream.CanSeek)
        {
            throw new NotSupportedException("Stream must support seeking.");
        }
        _stream = stream;
        _originalPosition = stream.Position;
        stream.Seek(offset, origin);
    }

    public void Dispose() => _stream.Seek(_originalPosition, SeekOrigin.Begin);
}