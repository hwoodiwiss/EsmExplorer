using System.IO.Compression;
using System.Text;
using K4os.Compression.LZ4.Streams;

namespace BethesdaArchiveParser.Core.Tests;

// Write-only test fixture. Layout and flags are deliberately independent of production parsing/hashing.
internal static class BsaFixture
{
    public static byte[] Create(uint version = 104, bool compressed = false, bool defaultCompressed = false,
        bool embedded = false, string folder = "meshes", string firstName = "first.bin", byte[]? content = null)
    {
        content ??= "first payload"u8.ToArray();
        byte[][] contents = [content, "second payload must not leak"u8.ToArray()];
        string[] names = [firstName, "second.bin"];
        var payloads = new List<byte[]>();
        for (int i = 0; i < 2; i++)
        {
            using var payload = new MemoryStream();
            using var writer = new BinaryWriter(payload, Encoding.Latin1, true);
            if (embedded)
            {
                byte[] name = Encoding.Latin1.GetBytes($"{folder}\\{names[i]}");
                writer.Write((byte)name.Length);
                writer.Write(name);
            }
            if (compressed)
            {
                writer.Write((uint)contents[i].Length);
                using Stream encoder = version == 105 ? LZ4Stream.Encode(payload, leaveOpen: true)
                    : new ZLibStream(payload, CompressionLevel.SmallestSize, true);
                encoder.Write(contents[i]);
            }
            else
            {
                writer.Write(contents[i]);
            }
            payloads.Add(payload.ToArray());
        }
        int folderRecordSize = version == 105 ? 24 : 16;
        byte[] folderBytes = Encoding.Latin1.GetBytes(folder);
        byte[] nameBytes = Encoding.Latin1.GetBytes(string.Join('\0', names) + '\0');
        int blockOffset = 36 + folderRecordSize;
        int dataOffset = blockOffset + folderBytes.Length + 2 + 32 + nameBytes.Length;
        using var stream = new MemoryStream();
        using var binary = new BinaryWriter(stream, Encoding.Latin1, true);
        binary.Write("BSA\0"u8);
        binary.Write(version);
        binary.Write(36u);
        binary.Write(3u | (defaultCompressed ? 4u : 0u) | (embedded ? 256u : 0u));
        binary.Write(1u);
        binary.Write(2u);
        binary.Write((uint)folderBytes.Length + 1);
        binary.Write((uint)nameBytes.Length);
        binary.Write(0u);
        binary.Write(0x1234UL);
        binary.Write(2u);
        if (version == 105)
        {
            binary.Write(0u);
        }
        if (version == 105)
        {
            binary.Write((ulong)(blockOffset + nameBytes.Length));
        }
        else
        {
            binary.Write((uint)(blockOffset + nameBytes.Length));
        }
        binary.Write((byte)(folderBytes.Length + 1));
        binary.Write(folderBytes);
        binary.Write((byte)0);
        for (int i = 0; i < 2; i++)
        {
            binary.Write((ulong)(i + 1));
            binary.Write((uint)payloads[i].Length | (compressed != defaultCompressed ? 0x40000000u : 0u));
            binary.Write((uint)dataOffset);
            dataOffset += payloads[i].Length;
        }
        binary.Write(nameBytes);
        foreach (var payload in payloads)
        {
            binary.Write(payload);
        }
        // Catches accidental copy-to-EOF and oversized compressed reads.
        binary.Write("trailing archive bytes"u8);
        return stream.ToArray();
    }
}
