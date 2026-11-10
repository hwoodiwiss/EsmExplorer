namespace BethesdaArchiveParser.Core.IO;

// Read-ahead buffer returning slices, so metadata parsing avoids a stream/JS interop call per field.
// A returned slice is valid only until the next Read.
internal sealed class BufferedSource(Stream stream, bool isAsync, CancellationToken cancellationToken)
{
    private byte[] _buffer = new byte[64 * 1024];
    private int _position;
    private int _length;

    public async ValueTask<ReadOnlyMemory<byte>> Read(int count)
    {
        if (_length - _position < count)
        {
            Buffer.BlockCopy(_buffer, _position, _buffer, 0, _length - _position);
            _length -= _position;
            _position = 0;
            if (count > _buffer.Length)
            {
                Array.Resize(ref _buffer, count);
            }
            while (_length < count)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int read = isAsync
                    ? await stream.ReadAsync(_buffer.AsMemory(_length), cancellationToken).ConfigureAwait(false)
                    : stream.Read(_buffer, _length, _buffer.Length - _length);
                if (read == 0)
                {
                    throw new InvalidDataException("Unexpected end of archive.");
                }
                _length += read;
            }
        }
        var slice = _buffer.AsMemory(_position, count);
        _position += count;
        return slice;
    }
}
