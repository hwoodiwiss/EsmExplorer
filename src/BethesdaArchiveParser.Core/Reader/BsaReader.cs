using System.Buffers.Binary;
using System.Text;

namespace BethesdaArchiveParser.Core.Reader;

// Read-ahead avoids an allocation and a stream/JS interop call per scalar or character.
internal sealed class BsaReader(Stream stream, bool isAsync, CancellationToken cancellationToken)
{
    private static readonly Encoding NameEncoding = CreateEncoding();
    private readonly byte[] _buffer = new byte[16 * 1024];
    private int _position;
    private int _length;

    public long Position => stream.Position - _length + _position;

    public void Seek(long offset)
    {
        if (offset < 0 || offset > stream.Length)
        {
            throw new InvalidDataException("BSA offset is outside the stream.");
        }
        if (offset == Position)
        {
            return;
        }
        stream.Position = offset;
        _position = _length = 0;
    }

    private static Encoding CreateEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1252);
    }

    public async ValueTask<byte> ReadByte()
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_position == _length)
        {
            _length = isAsync
                ? await stream.ReadAsync(_buffer, cancellationToken).ConfigureAwait(false)
                : stream.Read(_buffer);
            _position = 0;
            if (_length == 0)
            {
                throw new EndOfStreamException();
            }
        }
        return _buffer[_position++];
    }

    public async ValueTask<uint> ReadUInt32()
    {
        uint value = 0;
        for (int i = 0; i < 4; i++)
        {
            value |= (uint)await ReadByte().ConfigureAwait(false) << (i * 8);
        }
        return value;
    }

    public async ValueTask<ulong> ReadUInt64()
    {
        var low = await ReadUInt32().ConfigureAwait(false);
        return low | ((ulong)await ReadUInt32().ConfigureAwait(false) << 32);
    }

    public async ValueTask<string> ReadBzString()
    {
        var length = await ReadByte().ConfigureAwait(false);
        if (length == 0)
        {
            throw new InvalidDataException("A folder name must include its terminator.");
        }
        var bytes = new byte[length];
        for (int i = 0; i < length; i++)
        {
            bytes[i] = await ReadByte().ConfigureAwait(false);
        }
        if (bytes[^1] != 0)
        {
            throw new InvalidDataException("Folder name is not null terminated.");
        }
        return NameEncoding.GetString(bytes.AsSpan(0, length - 1));
    }

    public async ValueTask<List<string>> ReadFileNames(uint byteCount, uint count)
    {
        if (byteCount > int.MaxValue || byteCount > stream.Length - Position || count > byteCount)
        {
            throw new InvalidDataException("Invalid filename table length.");
        }
        var bytes = new byte[(int)byteCount];
        for (int i = 0; i < bytes.Length;)
        {
            bytes[i++] = await ReadByte().ConfigureAwait(false);
            int available = Math.Min(_length - _position, bytes.Length - i);
            _buffer.AsSpan(_position, available).CopyTo(bytes.AsSpan(i));
            _position += available;
            i += available;
        }
        var names = new List<string>((int)count);
        int start = 0;
        // Some shipped archives (e.g. New Vegas Voices1) overstate TotalFileNameLength.
        // FileCount is authoritative; the length still bounds all reads and folder offset adjustment.
        for (int i = 0; i < bytes.Length && names.Count < count; i++)
        {
            if (bytes[i] != 0)
            {
                continue;
            }
            if (i == start)
            {
                throw new InvalidDataException("Empty BSA filename.");
            }
            names.Add(NameEncoding.GetString(bytes.AsSpan(start, i - start)));
            start = i + 1;
        }
        if (names.Count != count)
        {
            throw new InvalidDataException("Filename table does not match the header.");
        }
        return names;
    }

    public static async ValueTask<BinaryBsaHeader> ReadHeader(Stream stream, bool isAsync, CancellationToken cancellationToken)
    {
        var bytes = new byte[36];
        if (isAsync)
        {
            await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            stream.ReadExactly(bytes);
        }
        if (!bytes.AsSpan(0, 4).SequenceEqual("BSA\0"u8))
        {
            throw new InvalidDataException("Invalid BSA signature.");
        }
        var header = new BinaryBsaHeader
        {
            MagicBytes = "BSA\0",
            Version = (BsaVersion)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4)),
            RecordOffset = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8)),
            ArchiveFlags = (BsaArchiveFlags)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(12)),
            FolderCount = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(16)),
            FileCount = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(20)),
            TotalFolderNameLength = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(24)),
            TotalFileNameLength = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(28)),
            FileFlags = (BsaFileFlags)BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(32)),
            Padding = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(34)),
        };
        // Only dispatch layouts we implement; an unknown version is not necessarily an invalid BSA.
        if (header.Version is not (BsaVersion.Oblivion or BsaVersion.Fallout3AndSkyrim or BsaVersion.SkyrimSpecialEdition))
        {
            throw new InvalidDataException($"Unsupported BSA version {(uint)header.Version}.");
        }
        if ((header.ArchiveFlags & (BsaArchiveFlags.Xbox360Archive | BsaArchiveFlags.XMemCodec)) != 0)
        {
            throw new NotSupportedException("Xbox/XMem BSA archives are not supported.");
        }
        const BsaArchiveFlags names = BsaArchiveFlags.IncludesDirectoryNames | BsaArchiveFlags.IncludesFileNames;
        if ((header.ArchiveFlags & names) != names)
        {
            throw new InvalidDataException("BSA directory and file names are required.");
        }
        long recordBytes = (long)header.FolderCount * (header.Version == BsaVersion.SkyrimSpecialEdition ? 24 : 16);
        if (header.RecordOffset < 36 || header.RecordOffset > stream.Length || recordBytes > stream.Length - header.RecordOffset
            || header.FileCount > int.MaxValue || (long)header.FileCount * 16 > stream.Length)
        {
            throw new InvalidDataException("Invalid BSA record counts or offsets.");
        }
        return header;
    }
}
