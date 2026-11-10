using System.IO.Compression;
using System.Text;
using K4os.Compression.LZ4;

namespace BethesdaArchiveParser.Core.Tests;

// Write-only BA2 test fixture. Layout, hashing and compression are deliberately independent of production code.
// Reference: https://en.uesp.net/wiki/Fallout4Mod:Archive_File_Format
internal static class Ba2Fixture
{
    public const uint Sentinel = 0xBAADF00D;
    public const int GeneralRecordSize = 36;
    public const int TextureRecordSize = 24;
    public const int ChunkRecordSize = 24;

    public static readonly byte[] FirstContent = "first payload"u8.ToArray();
    public static readonly byte[] SecondContent = "second payload must not leak"u8.ToArray();

    public static int HeaderSize(uint version) => version switch
    {
        2 => 32,
        3 => 36,
        _ => 24,
    };

    // Creates a two-entry GNRL archive: meshes\first.bin and meshes\second.bin.
    // compressionMethod applies to v3 only: 0 = zlib, 3 = raw LZ4 block.
    public static byte[] CreateGeneral(uint version = 1, bool compressed = false, uint compressionMethod = 0,
        bool includeNameTable = true, string firstPath = "meshes\\first.bin", byte[]? firstContent = null)
    {
        byte[][] contents = [firstContent ?? FirstContent, SecondContent];
        string[] paths = [firstPath, "meshes\\second.bin"];
        int headerSize = HeaderSize(version);
        long dataOffset = headerSize + (GeneralRecordSize * paths.Length);
        var payloads = contents.Select(content => compressed ? Compress(content, version, compressionMethod) : content).ToArray();

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.Latin1, true);
        long nameTableOffset = dataOffset + payloads.Sum(static p => (long)p.Length);
        WriteHeader(writer, version, "GNRL"u8, (uint)paths.Length, includeNameTable ? (ulong)nameTableOffset : 0, compressionMethod);
        long offset = dataOffset;
        for (int i = 0; i < paths.Length; i++)
        {
            WriteHashes(writer, paths[i]);
            writer.Write(0x00100100u);
            writer.Write((ulong)offset);
            writer.Write(compressed ? (uint)payloads[i].Length : 0u);
            writer.Write((uint)contents[i].Length);
            writer.Write(Sentinel);
            offset += payloads[i].Length;
        }
        foreach (byte[] payload in payloads)
        {
            writer.Write(payload);
        }
        if (includeNameTable)
        {
            WriteNameTable(writer, paths);
        }
        return stream.ToArray();
    }

    /// <summary>
    /// Creates a one-entry DX10 archive (<c>textures\test.dds</c>, 4x4 BC1/DXGI 71, 3 mips) split over two chunks:
    /// chunk 0 holds mip 0 (8 bytes), chunk 1 holds mips 1-2 (16 bytes).
    /// </summary>
    public static byte[] CreateTexture(uint version = 1, bool compressed = false, uint compressionMethod = 0, bool cubemap = false)
    {
        byte[][] chunks = [[.. Enumerable.Range(0, 8).Select(static i => (byte)i)], [.. Enumerable.Range(100, 16).Select(static i => (byte)i)]];
        var payloads = chunks.Select(chunk => compressed ? Compress(chunk, version, compressionMethod) : chunk).ToArray();
        int headerSize = HeaderSize(version);
        long dataOffset = headerSize + TextureRecordSize + (ChunkRecordSize * chunks.Length);
        long nameTableOffset = dataOffset + payloads.Sum(static p => (long)p.Length);

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.Latin1, true);
        WriteHeader(writer, version, "DX10"u8, 1, (ulong)nameTableOffset, compressionMethod);
        WriteHashes(writer, TexturePath);
        writer.Write((byte)0);
        writer.Write((byte)chunks.Length);
        writer.Write((ushort)ChunkRecordSize);
        writer.Write((ushort)4);
        writer.Write((ushort)4);
        writer.Write((byte)3);
        writer.Write((byte)71);
        writer.Write(cubemap ? (byte)1 : (byte)0);
        writer.Write((byte)8);
        long offset = dataOffset;
        ushort[][] mips = [[0, 0], [1, 2]];
        for (int i = 0; i < chunks.Length; i++)
        {
            writer.Write((ulong)offset);
            writer.Write(compressed ? (uint)payloads[i].Length : 0u);
            writer.Write((uint)chunks[i].Length);
            writer.Write(mips[i][0]);
            writer.Write(mips[i][1]);
            writer.Write(Sentinel);
            offset += payloads[i].Length;
        }
        foreach (byte[] payload in payloads)
        {
            writer.Write(payload);
        }
        WriteNameTable(writer, [TexturePath]);
        return stream.ToArray();
    }

    public const string TexturePath = "textures\\test.dds";

    public static byte[] TexturePixelData => [.. Enumerable.Range(0, 8).Select(static i => (byte)i), .. Enumerable.Range(100, 16).Select(static i => (byte)i)];

    /// <summary>Offset of the first file record's packed-size field, for corruption tests.</summary>
    public static int FirstGeneralPackedSizeOffset(uint version) => HeaderSize(version) + 24;

    /// <summary>Offset of the first file record's data-offset field, for corruption tests.</summary>
    public static int FirstGeneralDataOffsetOffset(uint version) => HeaderSize(version) + 16;

    /// <summary>Offset of the first file record's sentinel field, for corruption tests.</summary>
    public static int FirstGeneralSentinelOffset(uint version) => HeaderSize(version) + 32;

    private static void WriteHeader(BinaryWriter writer, uint version, ReadOnlySpan<byte> type, uint fileCount, ulong nameTableOffset, uint compressionMethod)
    {
        writer.Write("BTDX"u8);
        writer.Write(version);
        writer.Write(type);
        writer.Write(fileCount);
        writer.Write(nameTableOffset);
        if (version is 2 or 3)
        {
            writer.Write(1UL);
        }
        if (version == 3)
        {
            writer.Write(compressionMethod);
        }
    }

    private static void WriteHashes(BinaryWriter writer, string path)
    {
        string lower = path.ToLowerInvariant();
        int slash = lower.LastIndexOf('\\');
        string directory = slash < 0 ? "" : lower[..slash];
        string fileName = lower[(slash + 1)..];
        int dot = fileName.LastIndexOf('.');
        string stem = dot < 0 ? fileName : fileName[..dot];
        string extension = dot < 0 ? "" : fileName[(dot + 1)..];
        writer.Write(Crc32(stem));
        byte[] ext = new byte[4];
        Encoding.Latin1.GetBytes(extension.AsSpan(0, Math.Min(4, extension.Length)), ext);
        writer.Write(ext);
        writer.Write(Crc32(directory));
    }

    private static void WriteNameTable(BinaryWriter writer, string[] paths)
    {
        foreach (string path in paths)
        {
            byte[] name = Encoding.Latin1.GetBytes(path);
            writer.Write((ushort)name.Length);
            writer.Write(name);
        }
    }

    private static byte[] Compress(byte[] content, uint version, uint compressionMethod)
    {
        if (version == 3 && compressionMethod == 3)
        {
            byte[] target = new byte[LZ4Codec.MaximumOutputSize(content.Length)];
            int length = LZ4Codec.Encode(content, target, LZ4Level.L00_FAST);
            return target[..length];
        }
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.SmallestSize, true))
        {
            zlib.Write(content);
        }
        return output.ToArray();
    }

    // Bitwise CRC-32 (reflected, polynomial 0xEDB88320), written out to stay independent of production hashing.
    private static uint Crc32(string text)
    {
        uint crc = 0;
        foreach (byte b in Encoding.Latin1.GetBytes(text))
        {
            crc ^= b;
            for (int bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }
        return crc;
    }
}
