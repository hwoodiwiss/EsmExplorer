using BethesdaArchiveParser.Core.Bsa;
using BethesdaArchiveParser.Core.Bsa.Reader;
using BethesdaArchiveParser.Core.Reader;
#if BA2_READER
using BethesdaArchiveParser.Core.Ba2;
using BethesdaArchiveParser.Core.Ba2.Reader;
#endif

namespace BethesdaArchiveParser.Core.Tests;

// Format-agnostic archive API. BA2 cases are enabled with the BA2 reader (see docs/ba2-support-plan.md).
public sealed class ArchiveReaderTests
{
    public static IEnumerable<Func<(ArchiveFormat Format, byte[] Bytes)>> GeneralArchives()
    {
        yield return () => (ArchiveFormat.Bsa, BsaFixture.Create(103, compressed: true));
        yield return () => (ArchiveFormat.Bsa, BsaFixture.Create(104, compressed: true));
        yield return () => (ArchiveFormat.Bsa, BsaFixture.Create(105, compressed: true, embedded: true));
#if BA2_READER
        yield return () => (ArchiveFormat.Ba2, Ba2Fixture.CreateGeneral(1, compressed: true));
        yield return () => (ArchiveFormat.Ba2, Ba2Fixture.CreateGeneral(3, compressed: true, compressionMethod: 3));
#endif
    }

    [Test]
    [MethodDataSource(nameof(GeneralArchives))]
    public async Task Reads_archives_through_one_api_in_both_modes(ArchiveFormat format, byte[] bytes)
    {
        using var sync = new ModeStream(bytes, false);
        using var asyncStream = new ModeStream(bytes, true);
        var syncReader = new ArchiveReader(sync);
        var asyncReader = new ArchiveReader(asyncStream);
        var archive = syncReader.ReadArchive();
        var asyncArchive = await asyncReader.ReadArchiveAsync();
        await Assert.That(archive.Format).IsEqualTo(format);
        await Assert.That(asyncArchive.Format).IsEqualTo(format);
        await Assert.That(archive.Entries.Select(static e => e.Path)).IsEquivalentTo(["meshes\\first.bin", "meshes\\second.bin"]);
        await Assert.That(archive.GetEntry("MESHES/first.BIN")).IsEqualTo(archive.Entries[0]);
        await Assert.That(archive.GetEntry("meshes\\missing.bin")).IsNull();
        byte[][] expected = ["first payload"u8.ToArray(), "second payload must not leak"u8.ToArray()];
        for (int i = 0; i < 2; i++)
        {
            await Assert.That(archive.Entries[i].IsCompressed).IsTrue();
            long position = sync.Position;
            await Assert.That(syncReader.ReadFile(archive.Entries[i])).IsEquivalentTo(expected[i]);
            await Assert.That(sync.Position).IsEqualTo(position);
            await Assert.That(await asyncReader.ReadFileAsync(asyncArchive.Entries[i])).IsEquivalentTo(expected[i]);
            using var destination = new AsyncOutput();
            await Assert.That(await asyncReader.CopyFileToAsync(asyncArchive.Entries[i], destination)).IsEqualTo((long)expected[i].Length);
            await Assert.That(destination.ToArray()).IsEquivalentTo(expected[i]);
            using var syncDestination = new MemoryStream();
            await Assert.That(syncReader.CopyFileTo(archive.Entries[i], syncDestination)).IsEqualTo((long)expected[i].Length);
            await Assert.That(syncDestination.ToArray()).IsEquivalentTo(expected[i]);
        }
        await Assert.That(asyncStream.AsyncReads > 0).IsTrue();
    }

    [Test]
    [Arguments(103u, false, false, 13L)]
    [Arguments(104u, true, false, null)]
    [Arguments(104u, false, true, null)]
    public async Task Reports_expanded_length_only_when_known_without_reading_the_payload(uint version, bool compressed, bool embedded, long? expected)
    {
        using var stream = new MemoryStream(BsaFixture.Create(version, compressed, compressed, embedded));
        var entry = new ArchiveReader(stream).ReadArchive().Entries[0];
        await Assert.That(entry.ExpandedLength).IsEqualTo(expected);
    }

    [Test]
    public async Task Exposes_format_specific_records_and_archives()
    {
        using var bsaStream = new MemoryStream(BsaFixture.Create());
        var bsa = new ArchiveReader(bsaStream).ReadArchive();
        await Assert.That(bsa.Entries[0].File is BsaFileRecord).IsTrue();
        await Assert.That(bsa.Details is BsaArchive { Header.Version: BsaVersion.Fallout3AndSkyrim }).IsTrue();
#if BA2_READER
        using var ba2Stream = new MemoryStream(Ba2Fixture.CreateGeneral());
        var ba2 = new ArchiveReader(ba2Stream).ReadArchive();
        await Assert.That(ba2.Entries[0].File is Ba2FileRecord).IsTrue();
        await Assert.That(ba2.Details is Ba2Archive { Header.Version: Ba2Version.Fallout4 }).IsTrue();
#endif
    }

    [Test]
    public async Task Unified_reads_match_format_specific_readers()
    {
        byte[] bytes = BsaFixture.Create(105, compressed: true, embedded: true);
        using var unifiedStream = new MemoryStream(bytes);
        using var specificStream = new MemoryStream(bytes);
        var unified = new ArchiveReader(unifiedStream);
        var specific = new BsaArchiveReader(specificStream);
        var entry = unified.ReadArchive().GetEntry("meshes\\second.bin")!;
        var record = specific.ReadBsaArchive().GetFileByPath("meshes\\second.bin")!;
        await Assert.That(entry.File is BsaFileRecord unifiedRecord && unifiedRecord == record).IsTrue();
        await Assert.That(unified.ReadFile(entry)).IsEquivalentTo(specific.ReadBsaFile(record));
#if BA2_READER
        byte[] texture = Ba2Fixture.CreateTexture(1, compressed: true);
        using var unifiedBa2 = new MemoryStream(texture);
        using var specificBa2 = new MemoryStream(texture);
        var ba2Unified = new ArchiveReader(unifiedBa2);
        var ba2Specific = new Ba2ArchiveReader(specificBa2);
        var ba2Entry = ba2Unified.ReadArchive().GetEntry(Ba2Fixture.TexturePath)!;
        var ba2Record = ba2Specific.ReadBa2Archive().GetFileByPath(Ba2Fixture.TexturePath)!;
        await Assert.That(ba2Unified.ReadFile(ba2Entry)).IsEquivalentTo(ba2Specific.ReadBa2File(ba2Record));
        await Assert.That(ba2Entry.ExpandedLength).IsEqualTo(ba2Record.ExpandedLength);
#endif
    }

#if BA2_READER
    [Test]
    public async Task Rejects_entries_from_a_different_format()
    {
        using var bsaStream = new MemoryStream(BsaFixture.Create());
        using var ba2Stream = new MemoryStream(Ba2Fixture.CreateGeneral());
        var bsaReader = new ArchiveReader(bsaStream);
        var ba2Entry = new ArchiveReader(ba2Stream).ReadArchive().Entries[0];
        _ = bsaReader.ReadArchive();
        await Assert.That(() => bsaReader.ReadFile(ba2Entry)).Throws<ArgumentException>();
    }
#else
    [Test]
    public async Task Rejects_ba2_archives_explicitly_until_supported()
    {
        using var stream = new MemoryStream(Ba2Fixture.CreateGeneral());
        var reader = new ArchiveReader(stream);
        await Assert.That(() => reader.ReadArchive()).Throws<NotSupportedException>();
        await Assert.That(async () => await reader.ReadArchiveAsync()).Throws<NotSupportedException>();
    }
#endif

    [Test]
    public async Task Detects_formats_from_magic_without_moving_the_stream()
    {
        using var ba2 = new MemoryStream(Ba2Fixture.CreateGeneral());
        using var bsa = new MemoryStream(BsaFixture.Create());
        using var plugin = new MemoryStream("TES4\0\0\0\0"u8.ToArray());
        using var tiny = new MemoryStream("BT"u8.ToArray());
        ba2.Position = 5;
        await Assert.That(ArchiveReader.DetectFormat(ba2)).IsEqualTo(ArchiveFormat.Ba2);
        await Assert.That(ba2.Position).IsEqualTo(5L);
        await Assert.That(await ArchiveReader.DetectFormatAsync(bsa)).IsEqualTo(ArchiveFormat.Bsa);
        await Assert.That(ArchiveReader.DetectFormat(plugin)).IsEqualTo(ArchiveFormat.Unknown);
        await Assert.That(ArchiveReader.DetectFormat(tiny)).IsEqualTo(ArchiveFormat.Unknown);
    }

    [Test]
    public async Task Detection_uses_only_the_requested_io_mode()
    {
        byte[] bytes = BsaFixture.Create();
        using var sync = new ModeStream(bytes, false);
        using var asyncStream = new ModeStream(bytes, true);
        await Assert.That(ArchiveReader.DetectFormat(sync)).IsEqualTo(ArchiveFormat.Bsa);
        await Assert.That(await ArchiveReader.DetectFormatAsync(asyncStream)).IsEqualTo(ArchiveFormat.Bsa);
        await Assert.That(asyncStream.AsyncReads > 0).IsTrue();
    }

    [Test]
    public async Task Rejects_unknown_formats_and_unusable_streams()
    {
        using var stream = new MemoryStream("TES4 not an archive"u8.ToArray());
        var reader = new ArchiveReader(stream);
        await Assert.That(() => reader.ReadArchive()).Throws<InvalidDataException>();
        await Assert.That(async () => await reader.ReadArchiveAsync()).Throws<InvalidDataException>();
        await Assert.That(() => new ArchiveReader(new NonSeekable())).Throws<ArgumentException>();
    }

    [Test]
    public async Task Bsa_reader_identifies_ba2_input()
    {
        using var stream = new MemoryStream(Ba2Fixture.CreateGeneral());
        await Assert.That(() => new BsaArchiveReader(stream).ReadBsaArchive()).Throws<InvalidDataException>().WithMessageContaining("BA2");
    }

    [Test]
    public async Task Cancellation_propagates_through_the_unified_api()
    {
        using var stream = new ModeStream(BsaFixture.Create(compressed: true), true);
        var reader = new ArchiveReader(stream);
        var archive = await reader.ReadArchiveAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.That(async () => await reader.ReadArchiveAsync(cancellation.Token)).Throws<OperationCanceledException>();
        await Assert.That(async () => await reader.ReadFileAsync(archive.Entries[0], cancellation.Token)).Throws<OperationCanceledException>();
        await Assert.That(async () => await reader.CopyFileToAsync(archive.Entries[0], Stream.Null, cancellation.Token)).Throws<OperationCanceledException>();
        await Assert.That(async () => await ArchiveReader.DetectFormatAsync(stream, cancellation.Token)).Throws<OperationCanceledException>();
    }

    private sealed class NonSeekable : MemoryStream
    {
        public override bool CanSeek => false;
    }
}
