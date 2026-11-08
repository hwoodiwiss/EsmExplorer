#if BA2_READER
using System.Buffers.Binary;
using BethesdaArchiveParser.Core.Ba2;
using BethesdaArchiveParser.Core.Ba2.Reader;
using BethesdaArchiveParser.Core.Bsa.Reader;

namespace BethesdaArchiveParser.Core.Tests;

// Specification suite for BA2 support. See docs/ba2-support-plan.md for the API these tests assume
// and the phase in which each group is expected to pass. Remove the BA2_READER guard in phase 1.
public sealed class Ba2ArchiveTests
{
    // ---- Phase 1: header and GNRL index -------------------------------------------------------

    [Test]
    [Arguments(1u, Ba2Version.Fallout4)]
    [Arguments(2u, Ba2Version.StarfieldV2)]
    [Arguments(3u, Ba2Version.StarfieldV3)]
    [Arguments(7u, Ba2Version.Fallout4NextGenV7)]
    [Arguments(8u, Ba2Version.Fallout4NextGenV8)]
    public async Task Maps_wire_versions_to_named_formats(uint wireVersion, Ba2Version expected)
    {
        using var stream = new MemoryStream(Ba2Fixture.CreateGeneral(wireVersion));
        var reader = new Ba2ArchiveReader(stream);
        var archive = reader.ReadBa2Archive();
        await Assert.That(archive.Header.Version).IsEqualTo(expected);
        await Assert.That(archive.Header.Type).IsEqualTo(Ba2ArchiveType.General);
        await Assert.That(archive.Header.FileCount).IsEqualTo(2u);
        await Assert.That((await reader.ReadBa2ArchiveAsync()).Header.Version).IsEqualTo(expected);
    }

    [Test]
    [Arguments(0u)]
    [Arguments(4u)]
    [Arguments(6u)]
    [Arguments(9u)]
    [Arguments(uint.MaxValue)]
    public async Task Rejects_unknown_wire_versions_in_both_modes(uint version)
    {
        byte[] bytes = Ba2Fixture.CreateGeneral();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), version);
        using var stream = new MemoryStream(bytes);
        var reader = new Ba2ArchiveReader(stream);
        await Assert.That(() => reader.ReadBa2Archive()).Throws<InvalidDataException>();
        await Assert.That(async () => await reader.ReadBa2ArchiveAsync()).Throws<InvalidDataException>();
    }

    [Test]
    [Arguments(1u, false, 0u)]
    [Arguments(1u, true, 0u)]
    [Arguments(2u, true, 0u)]
    [Arguments(3u, false, 0u)]
    [Arguments(3u, true, 0u)]
    [Arguments(3u, true, 3u)]
    [Arguments(7u, true, 0u)]
    [Arguments(8u, false, 0u)]
    [Arguments(8u, true, 0u)]
    public async Task Reads_exact_general_entries_in_sync_and_genuinely_async_modes(uint version, bool compressed, uint compressionMethod)
    {
        byte[] bytes = Ba2Fixture.CreateGeneral(version, compressed, compressionMethod);
        using var sync = new ModeStream(bytes, false);
        using var asyncStream = new ModeStream(bytes, true);
        var syncReader = new Ba2ArchiveReader(sync);
        var asyncReader = new Ba2ArchiveReader(asyncStream);
        var archive = syncReader.ReadBa2Archive();
        var asyncArchive = await asyncReader.ReadBa2ArchiveAsync();
        await Assert.That(archive.Entries.Count).IsEqualTo(2);
        await Assert.That(asyncArchive.Entries.Count).IsEqualTo(2);
        await Assert.That(archive.Entries[0].Path).IsEqualTo("meshes\\first.bin");
        await Assert.That(archive.Entries[1].Path).IsEqualTo("meshes\\second.bin");
        await Assert.That(archive.GetFileByPath("MESHES/first.BIN")).IsEqualTo(archive.Entries[0].File);
        await Assert.That(archive.GetFileByPath("meshes\\missing.bin")).IsNull();
        byte[][] expected = [Ba2Fixture.FirstContent, Ba2Fixture.SecondContent];
        for (int i = 0; i < 2; i++)
        {
            await Assert.That(archive.Entries[i].File.IsCompressed).IsEqualTo(compressed);
            await Assert.That(archive.Entries[i].File.ExpandedLength).IsEqualTo((long)expected[i].Length);
            long position = sync.Position;
            await Assert.That(syncReader.ReadBa2File(archive.Entries[i].File)).IsEquivalentTo(expected[i]);
            await Assert.That(sync.Position).IsEqualTo(position);
            await Assert.That(await asyncReader.ReadBa2FileAsync(asyncArchive.Entries[i].File)).IsEquivalentTo(expected[i]);
            using var destination = new AsyncOutput();
            await Assert.That(await asyncReader.CopyBa2FileToAsync(asyncArchive.Entries[i].File, destination)).IsEqualTo((long)expected[i].Length);
            await Assert.That(destination.ToArray()).IsEquivalentTo(expected[i]);
            using var syncDestination = new MemoryStream();
            await Assert.That(syncReader.CopyBa2FileTo(archive.Entries[i].File, syncDestination)).IsEqualTo((long)expected[i].Length);
            await Assert.That(syncDestination.ToArray()).IsEquivalentTo(expected[i]);
        }
        await Assert.That(sync.CanRead).IsTrue();
        await Assert.That(asyncStream.AsyncReads > 0).IsTrue();
    }

    [Test]
    public async Task Reports_v3_compression_method()
    {
        using var zlib = new MemoryStream(Ba2Fixture.CreateGeneral(3, true, 0));
        using var lz4 = new MemoryStream(Ba2Fixture.CreateGeneral(3, true, 3));
        await Assert.That(new Ba2ArchiveReader(zlib).ReadBa2Archive().Header.CompressionMethod).IsEqualTo(Ba2CompressionMethod.Zlib);
        await Assert.That(new Ba2ArchiveReader(lz4).ReadBa2Archive().Header.CompressionMethod).IsEqualTo(Ba2CompressionMethod.Lz4Block);
    }

    [Test]
    [Arguments(1u)]
    [Arguments(2u)]
    [Arguments(99u)]
    public async Task Rejects_unknown_v3_compression_methods(uint method)
    {
        byte[] bytes = Ba2Fixture.CreateGeneral(3, true, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(32), method);
        using var stream = new MemoryStream(bytes);
        await Assert.That(() => new Ba2ArchiveReader(stream).ReadBa2Archive()).Throws<InvalidDataException>();
    }

    [Test]
    public async Task Supports_root_files_empty_payloads_and_windows_1252_names()
    {
        using var stream = new MemoryStream(Ba2Fixture.CreateGeneral(firstPath: "caf\u00e9.bin", firstContent: []));
        var reader = new Ba2ArchiveReader(stream);
        var archive = reader.ReadBa2Archive();
        await Assert.That(archive.GetFileByPath("CAF\u00c9.BIN")).IsNotNull();
        await Assert.That(reader.ReadBa2File(archive.Entries[0].File).Length).IsEqualTo(0);
    }

    [Test]
    public async Task Rejects_archives_without_a_name_table_explicitly()
    {
        using var stream = new MemoryStream(Ba2Fixture.CreateGeneral(includeNameTable: false));
        await Assert.That(() => new Ba2ArchiveReader(stream).ReadBa2Archive()).Throws<InvalidDataException>();
    }

    [Test]
    [Arguments(0, 0u)]
    [Arguments(8, 0x58585858u)]
    [Arguments(12, uint.MaxValue)]
    [Arguments(16, uint.MaxValue)]
    public async Task Rejects_invalid_headers(int offset, uint value)
    {
        byte[] bytes = Ba2Fixture.CreateGeneral();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
        using var stream = new MemoryStream(bytes);
        await Assert.That(() => new Ba2ArchiveReader(stream).ReadBa2Archive()).Throws<InvalidDataException>();
    }

    [Test]
    public async Task Rejects_name_table_offset_beyond_the_archive()
    {
        byte[] bytes = Ba2Fixture.CreateGeneral();
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(16), (ulong)bytes.Length + 1);
        using var stream = new MemoryStream(bytes);
        await Assert.That(() => new Ba2ArchiveReader(stream).ReadBa2Archive()).Throws<InvalidDataException>();
    }

    [Test]
    public async Task Rejects_duplicate_paths()
    {
        using var stream = new MemoryStream(Ba2Fixture.CreateGeneral(firstPath: "MESHES/SECOND.BIN"));
        await Assert.That(() => new Ba2ArchiveReader(stream).ReadBa2Archive()).Throws<InvalidDataException>();
    }

    [Test]
    public async Task Rejects_truncated_index_and_name_table()
    {
        byte[] bytes = Ba2Fixture.CreateGeneral();
        foreach (int length in new[] { 0, 3, 23, 24 + 35, bytes.Length - 1 })
        {
            using var stream = new MemoryStream(bytes[..length]);
            await Assert.That(() => new Ba2ArchiveReader(stream).ReadBa2Archive()).Throws<InvalidDataException>();
        }
    }

    [Test]
    public async Task Tolerates_nonstandard_record_sentinel()
    {
        // Shipped tooling does not validate the 0xBAADF00D padding; treat it as informational.
        byte[] bytes = Ba2Fixture.CreateGeneral();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(Ba2Fixture.FirstGeneralSentinelOffset(1)), 0);
        using var stream = new MemoryStream(bytes);
        var reader = new Ba2ArchiveReader(stream);
        await Assert.That(reader.ReadBa2File(reader.ReadBa2Archive().Entries[0].File)).IsEquivalentTo(Ba2Fixture.FirstContent);
    }

    [Test]
    public async Task Rejects_payload_ranges_outside_the_archive()
    {
        byte[] bytes = Ba2Fixture.CreateGeneral(compressed: true);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(Ba2Fixture.FirstGeneralDataOffsetOffset(1)), (ulong)bytes.Length - 2);
        using var stream = new MemoryStream(bytes);
        var reader = new Ba2ArchiveReader(stream);
        var archive = reader.ReadBa2Archive();
        await Assert.That(() => reader.ReadBa2File(archive.Entries[0].File)).Throws<InvalidDataException>();
        await Assert.That(async () => await reader.ReadBa2FileAsync(archive.Entries[0].File)).Throws<InvalidDataException>();
    }

    [Test]
    [Arguments(1u, 0u)]
    [Arguments(3u, 3u)]
    public async Task Compressed_reads_do_not_consume_the_next_entry(uint version, uint method)
    {
        // Overstating the packed size must not let a decoder read into the following payload unnoticed.
        byte[] bytes = Ba2Fixture.CreateGeneral(version, true, method);
        int field = Ba2Fixture.FirstGeneralPackedSizeOffset(version);
        uint packed = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(field));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(field), packed + 4);
        using var stream = new MemoryStream(bytes);
        var reader = new Ba2ArchiveReader(stream);
        var archive = reader.ReadBa2Archive();
        await Assert.That(() => reader.ReadBa2File(archive.Entries[0].File)).Throws<InvalidDataException>();
        await Assert.That(reader.ReadBa2File(archive.Entries[1].File)).IsEquivalentTo(Ba2Fixture.SecondContent);
    }

    [Test]
    [Arguments(1u, 0u, 1u)]
    [Arguments(1u, 0u, 1000u)]
    [Arguments(3u, 3u, 1u)]
    [Arguments(3u, 3u, 1000u)]
    public async Task Rejects_incorrect_decompressed_sizes(uint version, uint method, uint declaredSize)
    {
        byte[] bytes = Ba2Fixture.CreateGeneral(version, true, method);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(Ba2Fixture.FirstGeneralPackedSizeOffset(version) + 4), declaredSize);
        using var stream = new MemoryStream(bytes);
        var reader = new Ba2ArchiveReader(stream);
        var archive = reader.ReadBa2Archive();
        await Assert.That(() => reader.ReadBa2File(archive.Entries[0].File)).Throws<InvalidDataException>();
        await Assert.That(async () => await reader.CopyBa2FileToAsync(archive.Entries[0].File, Stream.Null)).Throws<InvalidDataException>();
    }

    [Test]
    public async Task Cancellation_propagates_to_archive_and_payload_reads()
    {
        byte[] bytes = Ba2Fixture.CreateGeneral(compressed: true);
        using var stream = new ModeStream(bytes, true);
        var reader = new Ba2ArchiveReader(stream);
        var archive = await reader.ReadBa2ArchiveAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.That(async () => await reader.ReadBa2ArchiveAsync(cancellation.Token)).Throws<OperationCanceledException>();
        await Assert.That(async () => await reader.ReadBa2FileAsync(archive.Entries[0].File, cancellation.Token)).Throws<OperationCanceledException>();
        await Assert.That(async () => await reader.CopyBa2FileToAsync(archive.Entries[0].File, Stream.Null, cancellation.Token)).Throws<OperationCanceledException>();
    }

    [Test]
    // Vectors from real Fallout 4 archives, via bsa-rs src/fo4/hashing.rs. CRC-32, init 0, no final XOR.
    [Arguments("Interface\\Pipboy_StatsPage.swf", 0x2F26E4D0u, 0x00667773u, 0xD2FDF873u)]
    [Arguments("interface/pipboy_statspage.swf", 0x2F26E4D0u, 0x00667773u, 0xD2FDF873u)]
    public async Task Computes_ba2_path_hashes(string path, uint name, uint extension, uint directory)
    {
        var hash = Ba2Hash.Compute(path);
        await Assert.That(hash.Name).IsEqualTo(name);
        await Assert.That(hash.Extension).IsEqualTo(extension);
        await Assert.That(hash.Directory).IsEqualTo(directory);
    }

    [Test]
    public async Task Requires_readable_seekable_streams_and_leaves_them_open()
    {
        using var stream = new MemoryStream(Ba2Fixture.CreateGeneral());
        _ = new Ba2ArchiveReader(stream).ReadBa2Archive();
        await Assert.That(stream.CanRead).IsTrue();
        await Assert.That(() => new Ba2ArchiveReader(new NonSeekable())).Throws<ArgumentException>();
    }

    // ---- Phase 2: BSA interplay (unified API tests live in ArchiveReaderTests) ---------------

    [Test]
    public async Task Bsa_reader_rejects_ba2_input_and_vice_versa()
    {
        using var ba2 = new MemoryStream(Ba2Fixture.CreateGeneral());
        using var bsa = new MemoryStream(BsaFixture.Create());
        await Assert.That(() => new BsaArchiveReader(ba2).ReadBsaArchive()).Throws<InvalidDataException>();
        await Assert.That(() => new Ba2ArchiveReader(bsa).ReadBa2Archive()).Throws<InvalidDataException>();
    }

    // ---- Phase 3: DX10 textures --------------------------------------------------------------

    [Test]
    [Arguments(1u, false, 0u, false)]
    [Arguments(1u, true, 0u, false)]
    [Arguments(1u, true, 0u, true)]
    [Arguments(3u, true, 3u, false)]
    [Arguments(8u, true, 0u, false)]
    public async Task Reconstructs_dds_files_from_texture_chunks(uint version, bool compressed, uint method, bool cubemap)
    {
        byte[] bytes = Ba2Fixture.CreateTexture(version, compressed, method, cubemap);
        using var sync = new ModeStream(bytes, false);
        using var asyncStream = new ModeStream(bytes, true);
        var syncReader = new Ba2ArchiveReader(sync);
        var asyncReader = new Ba2ArchiveReader(asyncStream);
        var archive = syncReader.ReadBa2Archive();
        var asyncArchive = await asyncReader.ReadBa2ArchiveAsync();
        await Assert.That(archive.Header.Type).IsEqualTo(Ba2ArchiveType.Texture);
        var file = archive.GetFileByPath(Ba2Fixture.TexturePath)!;
        await Assert.That(file.Chunks.Count).IsEqualTo(2);
        await Assert.That(file.Texture!.Width).IsEqualTo((ushort)4);
        await Assert.That(file.Texture.Height).IsEqualTo((ushort)4);
        await Assert.That(file.Texture.MipCount).IsEqualTo((byte)3);
        await Assert.That(file.Texture.DxgiFormat).IsEqualTo((byte)71);
        await Assert.That(file.Texture.IsCubemap).IsEqualTo(cubemap);

        byte[] dds = syncReader.ReadBa2File(file);
        await Assert.That(file.ExpandedLength).IsEqualTo((long)dds.Length);
        await AssertDds(dds, cubemap);
        using var destination = new AsyncOutput();
        long written = await asyncReader.CopyBa2FileToAsync(asyncArchive.Entries[0].File, destination);
        await Assert.That(written).IsEqualTo((long)dds.Length);
        await Assert.That(destination.ToArray()).IsEquivalentTo(dds);
    }

    [Test]
    public async Task Rejects_texture_chunk_ranges_outside_the_archive()
    {
        byte[] bytes = Ba2Fixture.CreateTexture();
        int secondChunkOffset = Ba2Fixture.HeaderSize(1) + Ba2Fixture.TextureRecordSize + Ba2Fixture.ChunkRecordSize;
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(secondChunkOffset), (ulong)bytes.Length);
        using var stream = new MemoryStream(bytes);
        var reader = new Ba2ArchiveReader(stream);
        var archive = reader.ReadBa2Archive();
        await Assert.That(() => reader.ReadBa2File(archive.Entries[0].File)).Throws<InvalidDataException>();
    }

    [Test]
    public async Task Rejects_unexpected_texture_chunk_header_size()
    {
        byte[] bytes = Ba2Fixture.CreateTexture();
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(Ba2Fixture.HeaderSize(1) + 14), 16);
        using var stream = new MemoryStream(bytes);
        await Assert.That(() => new Ba2ArchiveReader(stream).ReadBa2Archive()).Throws<InvalidDataException>();
    }

    // Expected values are written from the DDS specification, not from production code:
    // https://learn.microsoft.com/windows/win32/direct3ddds/dds-header
    private static async Task AssertDds(byte[] dds, bool cubemap)
    {
        byte[] pixels = Ba2Fixture.TexturePixelData;
        await Assert.That(dds.Length).IsEqualTo(4 + 124 + 20 + pixels.Length);
        await Assert.That(dds.AsSpan(0, 4).ToArray()).IsEquivalentTo("DDS "u8.ToArray());
        await Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(dds.AsSpan(4))).IsEqualTo(124u);
        await Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(dds.AsSpan(12))).IsEqualTo(4u);
        await Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(dds.AsSpan(16))).IsEqualTo(4u);
        await Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(dds.AsSpan(28))).IsEqualTo(3u);
        await Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(dds.AsSpan(76))).IsEqualTo(32u);
        await Assert.That(dds.AsSpan(84, 4).ToArray()).IsEquivalentTo("DX10"u8.ToArray());
        await Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(dds.AsSpan(128))).IsEqualTo(71u);
        await Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(dds.AsSpan(132))).IsEqualTo(3u);
        await Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(dds.AsSpan(136)) & 4u).IsEqualTo(cubemap ? 4u : 0u);
        await Assert.That(dds.AsSpan(148).ToArray()).IsEquivalentTo(pixels);
    }

    private sealed class NonSeekable : MemoryStream
    {
        public override bool CanSeek => false;
    }
}
#endif
