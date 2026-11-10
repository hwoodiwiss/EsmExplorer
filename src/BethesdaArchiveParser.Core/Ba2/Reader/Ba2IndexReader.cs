using System.Buffers.Binary;
using BethesdaArchiveParser.Core.IO;

namespace BethesdaArchiveParser.Core.Ba2.Reader;

internal static class Ba2IndexReader
{
    private const int GeneralRecordSize = 36;
    private const int TextureRecordSize = 24;
    private const int ChunkRecordSize = 24;

    public static async ValueTask<Ba2Archive> Read(Stream stream, bool isAsync, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        long length = stream.Length;
        if (length < 24)
        {
            throw new InvalidDataException("BA2 header is truncated.");
        }
        stream.Position = 0;
        var source = new BufferedSource(stream, isAsync, cancellationToken);
        var header = await ReadHeader(source, length).ConfigureAwait(false);
        int headerSize = HeaderSize(header.Version);
        int minimumRecordSize = header.Type == Ba2ArchiveType.General ? GeneralRecordSize : TextureRecordSize;
        if ((long)header.FileCount * minimumRecordSize > length - headerSize)
        {
            throw new InvalidDataException("BA2 file count exceeds the archive size.");
        }

        var records = new List<Ba2FileRecord>((int)header.FileCount);
        for (uint i = 0; i < header.FileCount; i++)
        {
            records.Add(header.Type == Ba2ArchiveType.General
                ? await ReadGeneralRecord(source, header).ConfigureAwait(false)
                : await ReadTextureRecord(source, header).ConfigureAwait(false));
        }

        stream.Position = (long)header.NameTableOffset;
        var names = new BufferedSource(stream, isAsync, cancellationToken);
        var entries = new List<Ba2Entry>(records.Count);
        foreach (var record in records)
        {
            var lengthBytes = await names.Read(2).ConfigureAwait(false);
            int nameLength = BinaryPrimitives.ReadUInt16LittleEndian(lengthBytes.Span);
            if (nameLength == 0)
            {
                throw new InvalidDataException("Empty BA2 filename.");
            }
            var nameBytes = await names.Read(nameLength).ConfigureAwait(false);
            entries.Add(new Ba2Entry(ArchiveEncoding.Windows1252.GetString(nameBytes.Span), record));
        }
        return new Ba2Archive(header, entries);
    }

    internal static int HeaderSize(Ba2Version version) => version switch
    {
        Ba2Version.StarfieldV2 => 32,
        Ba2Version.StarfieldV3 => 36,
        _ => 24,
    };

    private static async ValueTask<Ba2Header> ReadHeader(BufferedSource source, long length)
    {
        var bytes = await source.Read(24).ConfigureAwait(false);
        if (!bytes.Span[..4].SequenceEqual("BTDX"u8))
        {
            throw new InvalidDataException(bytes.Span[..4].SequenceEqual("BSA\0"u8)
                ? "Invalid BA2 signature: this is a BSA archive."
                : "Invalid BA2 signature.");
        }
        var version = (Ba2Version)BinaryPrimitives.ReadUInt32LittleEndian(bytes.Span[4..]);
        if (version is not (Ba2Version.Fallout4 or Ba2Version.StarfieldV2 or Ba2Version.StarfieldV3
            or Ba2Version.Fallout4NextGenV7 or Ba2Version.Fallout4NextGenV8))
        {
            throw new InvalidDataException($"Unsupported BA2 version {(uint)version}.");
        }
        var typeBytes = bytes.Span[8..12];
        Ba2ArchiveType type;
        if (typeBytes.SequenceEqual("GNRL"u8))
        {
            type = Ba2ArchiveType.General;
        }
        else if (typeBytes.SequenceEqual("DX10"u8))
        {
            type = Ba2ArchiveType.Texture;
        }
        else if (typeBytes.SequenceEqual("GNMF"u8))
        {
            throw new NotSupportedException("GNMF (console texture) BA2 archives are not supported.");
        }
        else
        {
            throw new InvalidDataException("Unknown BA2 archive type.");
        }
        uint fileCount = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Span[12..]);
        ulong nameTableOffset = BinaryPrimitives.ReadUInt64LittleEndian(bytes.Span[16..]);
        int headerSize = HeaderSize(version);
        if (length < headerSize)
        {
            throw new InvalidDataException("BA2 header is truncated.");
        }
        var compression = Ba2CompressionMethod.Zlib;
        if (version is Ba2Version.StarfieldV2 or Ba2Version.StarfieldV3)
        {
            // Unexplained 64-bit field (tools write 1); intentionally neither interpreted nor validated.
            _ = await source.Read(8).ConfigureAwait(false);
        }
        if (version == Ba2Version.StarfieldV3)
        {
            var method = (Ba2CompressionMethod)BinaryPrimitives.ReadUInt32LittleEndian((await source.Read(4).ConfigureAwait(false)).Span);
            if (method is not (Ba2CompressionMethod.Zlib or Ba2CompressionMethod.Lz4Block))
            {
                throw new InvalidDataException($"Unsupported BA2 compression method {(uint)method}.");
            }
            compression = method;
        }
        if (nameTableOffset == 0)
        {
            throw new InvalidDataException("BA2 archives without a name table are not supported.");
        }
        if (nameTableOffset < (ulong)headerSize || nameTableOffset > (ulong)length)
        {
            throw new InvalidDataException("BA2 name table offset is outside the archive.");
        }
        return new Ba2Header(version, type, fileCount, nameTableOffset, compression);
    }

    private static async ValueTask<Ba2FileRecord> ReadGeneralRecord(BufferedSource source, Ba2Header header)
    {
        var bytes = (await source.Read(GeneralRecordSize).ConfigureAwait(false)).Span;
        // Bytes 12-15 are per-file flags and 32-35 the 0xBAADF00D sentinel; neither is validated by shipped tooling.
        var chunk = new Ba2Chunk(BinaryPrimitives.ReadUInt64LittleEndian(bytes[16..]),
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[24..]), BinaryPrimitives.ReadUInt32LittleEndian(bytes[28..]), 0, 0);
        return new Ba2FileRecord(header, BinaryPrimitives.ReadUInt32LittleEndian(bytes),
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]), BinaryPrimitives.ReadUInt32LittleEndian(bytes[8..]), [chunk], null);
    }

    private static async ValueTask<Ba2FileRecord> ReadTextureRecord(BufferedSource source, Ba2Header header)
    {
        var record = (await source.Read(TextureRecordSize).ConfigureAwait(false)).ToArray();
        int chunkCount = record[13];
        ushort chunkHeaderSize = BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(14));
        var texture = new Ba2TextureInfo(
            Width: BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(18)),
            Height: BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(16)),
            MipCount: record[20],
            DxgiFormat: record[21],
            IsCubemap: (record[22] & 1) != 0,
            TileMode: record[23]);
        if (chunkHeaderSize != ChunkRecordSize)
        {
            throw new InvalidDataException($"Unexpected BA2 texture chunk header size {chunkHeaderSize}.");
        }
        if (chunkCount == 0)
        {
            throw new InvalidDataException("BA2 texture has no chunks.");
        }
        var chunkBytes = (await source.Read(chunkCount * ChunkRecordSize).ConfigureAwait(false)).Span;
        var chunks = new Ba2Chunk[chunkCount];
        int nextMip = 0;
        for (int i = 0; i < chunkCount; i++)
        {
            var bytes = chunkBytes.Slice(i * ChunkRecordSize, ChunkRecordSize);
            var chunk = new Ba2Chunk(BinaryPrimitives.ReadUInt64LittleEndian(bytes),
                BinaryPrimitives.ReadUInt32LittleEndian(bytes[8..]), BinaryPrimitives.ReadUInt32LittleEndian(bytes[12..]),
                BinaryPrimitives.ReadUInt16LittleEndian(bytes[16..]), BinaryPrimitives.ReadUInt16LittleEndian(bytes[18..]));
            if (chunk.StartMip < nextMip || chunk.EndMip < chunk.StartMip || chunk.EndMip >= Math.Max(texture.MipCount, (byte)1))
            {
                throw new InvalidDataException("BA2 texture chunk mip range is invalid.");
            }
            nextMip = chunk.EndMip + 1;
            chunks[i] = chunk;
        }
        return new Ba2FileRecord(header, BinaryPrimitives.ReadUInt32LittleEndian(record),
            BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(4)), BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(8)),
            chunks, texture);
    }
}
