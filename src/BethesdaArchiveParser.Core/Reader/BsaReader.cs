using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace BethesdaArchiveParser.Core.Reader;

internal sealed class BsaReader(BinaryBsaHeader Header, Stream Stream, bool asyncReader)
{
    private readonly bool _bigEndian = Header.ArchiveFlags.HasFlag(BsaArchiveFlags.Xbox360Archive);

    public Stream BaseStream => Stream;

    public static async ValueTask<BinaryBsaHeader> ReadHeader(Stream stream)
    {
        using BinaryReader _reader = new(stream, Encoding.UTF8, leaveOpen: true);

        int bufferSize = Marshal.SizeOf<BinaryBsaHeader>();
        byte[] buffer = asyncReader ? _reader.ReadBytesAsync(bufferSize) : _reader.ReadBytes(bufferSize);
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

    public uint ReadUInt32()
    {
        var bytes = ReadBytes(4);
        return _bigEndian ? BinaryPrimitives.ReadUInt32BigEndian(bytes) : BinaryPrimitives.ReadUInt32LittleEndian(bytes);
    }

    public ulong ReadUInt64()
    {
        var bytes = ReadBytes(8);
        return _bigEndian ? BinaryPrimitives.ReadUInt64BigEndian(bytes) : BinaryPrimitives.ReadUInt64LittleEndian(bytes);
    }

    public string ReadBzString()
    {
        var length = Stream.ReadByte();
        if (length == -1)
        {
            throw new EndOfStreamException("Unexpected end of stream while reading BzString.");
        }
        var bytes = ReadBytes(length);
        return Encoding.GetEncoding(1252).GetString(bytes[..^1]);
    }

    public string ReadBString()
    {
        var length = Stream.ReadByte();
        if (length == -1)
        {
            throw new EndOfStreamException("Unexpected end of stream while reading BString.");
        }
        var bytes = ReadBytes(length);
        return Encoding.GetEncoding(1252).GetString(bytes);
    }

    public string ReadZString()
    {
        List<byte> bytes = [];
        while (true)
        {
            var b = Stream.ReadByte();
            if (b == -1)
            {
                throw new EndOfStreamException("Unexpected end of stream while reading ZString.");
            }
            if (b == '\0')
            {
                break;
            }
            bytes.Add((byte)b);
        }

        return Encoding.GetEncoding(1252).GetString([.. bytes]);
    }

    private byte[] ReadBytes(int count)
    {
        Span<byte> buffer = stackalloc byte[count];
        if (Stream.ReadAtLeast(buffer, count, false) != count)
        {
            throw new EndOfStreamException("Unexpected end of stream while reading bytes.");
        }
        return buffer.ToArray();
    }

    private async Task<byte[]> ReadBytesAsync(int count)
    {
        Memory<byte> buffer = new Memory<byte>(new byte[count]);
        if (await Stream.ReadAtLeastAsync(buffer, count, false) != count)
        {
            throw new EndOfStreamException("Unexpected end of stream while reading bytes.");
        }
        return buffer.ToArray();
    }
}
