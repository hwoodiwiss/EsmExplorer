namespace BethesdaArchiveParser.Core.IO;

internal sealed class ScopedOffset : IDisposable
{
    private readonly Stream _stream;
    private readonly long _originalPosition;

    public ScopedOffset(Stream stream, long offset)
    {
        if (!stream.CanSeek)
        {
            throw new NotSupportedException("Stream must support seeking.");
        }
        _stream = stream;
        _originalPosition = stream.Position;
        stream.Seek(offset, SeekOrigin.Begin);
    }

    public void Dispose() => _stream.Seek(_originalPosition, SeekOrigin.Begin);
}