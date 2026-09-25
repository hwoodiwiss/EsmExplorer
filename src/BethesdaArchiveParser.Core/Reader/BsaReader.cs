using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace BethesdaArchiveParser.Core.Reader;

internal sealed class BsaReader(BinaryBsaHeader Header, Stream Stream, bool asyncReader)
{
    private readonly bool _bigEndian = Header.ArchiveFlags.HasFlag(BsaArchiveFlags.Xbox360Archive);

    public Stream BaseStream => Stream;

    public bool IsAsync { get; } = asyncReader;

    public static async ValueTask<BinaryBsaHeader> ReadHeader(Stream stream, bool readAsync)
    {
        int bufferSize = Marshal.SizeOf<BinaryBsaHeader>();
        byte[] buffer = new byte[bufferSize];

        if (readAsync)
        {
            await stream.ReadExactlyAsync(new Memory<byte>(buffer));
        }
        else
        {
            stream.ReadExactly(buffer);
        }

        nint ptr = Marshal.AllocHGlobal(bufferSize);
        Marshal.Copy(buffer, 0, ptr, buffer.Length);
        BinaryBsaHeader header = Marshal.PtrToStructure<BinaryBsaHeader>(ptr)!;
        Marshal.FreeHGlobal(ptr);

        if (!header.ArchiveFlags.HasFlag(BsaArchiveFlags.IncludesDirectoryNames))
        {
            throw new InvalidDataException("Expected IncludesDirectoryNames flag to be set in the BSA header.");
        }

        if (!header.ArchiveFlags.HasFlag(BsaArchiveFlags.IncludesFileNames))
        {
            throw new InvalidDataException("Expected IncludesFileNames flag to be set in the BSA header.");
        }

        return header;
    }

    public async ValueTask<uint> ReadUInt32()
    {
        var bytes = await ReadBytesAsync(4);
        return _bigEndian ? BinaryPrimitives.ReadUInt32BigEndian(bytes) : BinaryPrimitives.ReadUInt32LittleEndian(bytes);
    }

    public async ValueTask<ulong> ReadUInt64()
    {
        var bytes = await ReadBytesAsync(8);
        return _bigEndian ? BinaryPrimitives.ReadUInt64BigEndian(bytes) : BinaryPrimitives.ReadUInt64LittleEndian(bytes);
    }

    public async ValueTask<string> ReadBzString()
    {
        var length = Stream.ReadByte();
        if (length == -1)
        {
            throw new EndOfStreamException("Unexpected end of stream while reading BzString.");
        }
        var bytes = await ReadBytesAsync(length);
        return Encoding.GetEncoding(1252).GetString(bytes[..^1]);
    }

    public async ValueTask<string> ReadBString()
    {
        var length = Stream.ReadByte();
        if (length == -1)
        {
            throw new EndOfStreamException("Unexpected end of stream while reading BString.");
        }
        var bytes = await ReadBytesAsync(length);
        return Encoding.GetEncoding(1252).GetString(bytes);
    }

    public async ValueTask<string> ReadZString()
    {
        List<byte> bytes = [];
        while (true)
        {
            var b = await ReadByteAsync();
            if (b == '\0')
            {
                break;
            }
            bytes.Add(b);
        }

        return Encoding.GetEncoding(1252).GetString([.. bytes]);
    }

    private async ValueTask<byte> ReadByteAsync()
    {
        var singleByteArray = await ReadBytesAsync(1);
        if (singleByteArray.Length != 1)
        {
            throw new EndOfStreamException("Unexpected end of stream while reading a single byte.");
        }
        return singleByteArray[0];
    }

    private async ValueTask<byte[]> ReadBytesAsync(int count)
    {
        byte[] buffer = new byte[count];
        int bytesRead = IsAsync
            ? await Stream.ReadAtLeastAsync(new Memory<byte>(buffer), count, false)
            : Stream.ReadAtLeast(buffer, count, false);
        if (bytesRead != count)
        {
            throw new EndOfStreamException("Unexpected end of stream while reading bytes.");
        }
        return buffer;
    }
}
