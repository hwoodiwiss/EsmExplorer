using System.Buffers.Binary;
using BethesdaArchiveParser.Core.Reader;

namespace BethesdaArchiveParser.Core.Tests;

public sealed class ArchiveTests
{
    [Test]
    [Arguments(103u, false, false, false)]
    [Arguments(103u, true, false, false)]
    [Arguments(104u, false, false, false)]
    [Arguments(104u, true, false, false)]
    [Arguments(104u, false, true, false)]
    [Arguments(104u, true, true, false)]
    [Arguments(104u, false, false, true)]
    [Arguments(104u, true, true, true)]
    [Arguments(105u, true, false, false)]
    [Arguments(105u, true, true, true)]
    [Arguments(105u, false, true, true)]
    public async Task Reads_exact_entries_in_sync_and_genuinely_async_modes(uint version, bool compressed, bool defaultCompressed, bool embedded)
    {
        byte[] bytes = BsaFixture.Create(version, compressed, defaultCompressed, embedded);
        using var sync = new ModeStream(bytes, false);
        using var asyncStream = new ModeStream(bytes, true);
        var syncReader = new BsaArchiveReader(sync);
        var asyncReader = new BsaArchiveReader(asyncStream);
        var archive = syncReader.ReadBsaArchive();
        var asyncArchive = await asyncReader.ReadBsaArchiveAsync();
        await Assert.That(archive.Entries.Count).IsEqualTo(2);
        await Assert.That((uint)archive.Header.Version).IsEqualTo(version);
        await Assert.That(archive.Entries[0].Path).IsEqualTo("meshes\\first.bin");
        await Assert.That(archive.GetFileByPath("MESHES/first.BIN")).IsEqualTo(archive.Entries[0].File);
        await Assert.That(archive.Folders[0].FileOffsets[1] - archive.Folders[0].FileOffsets[0]).IsEqualTo(16L);
        for (int i = 0; i < 2; i++)
        {
            byte[] expected = i == 0 ? "first payload"u8.ToArray() : "second payload must not leak"u8.ToArray();
            await Assert.That(archive.Entries[i].File.IsCompressed).IsEqualTo(compressed);
            long position = sync.Position;
            await Assert.That(syncReader.ReadBsaFile(archive.Entries[i].File)).IsEquivalentTo(expected);
            await Assert.That(sync.Position).IsEqualTo(position);
            await Assert.That(await asyncReader.ReadBsaFileAsync(asyncArchive.Entries[i].File)).IsEquivalentTo(expected);
            using var destination = new AsyncOutput();
            await Assert.That(await asyncReader.CopyBsaFileToAsync(asyncArchive.Entries[i].File, destination)).IsEqualTo((long)expected.Length);
            await Assert.That(destination.ToArray()).IsEquivalentTo(expected);
        }
        await Assert.That(sync.CanRead).IsTrue();
        await Assert.That(asyncStream.AsyncReads > 0).IsTrue();
    }

    [Test]
    [Arguments(103u, BsaVersion.Oblivion)]
    [Arguments(104u, BsaVersion.Fallout3AndSkyrim)]
    [Arguments(105u, BsaVersion.SkyrimSpecialEdition)]
    public async Task Maps_wire_versions_to_named_formats(uint wireVersion, BsaVersion expected)
    {
        using var stream = new MemoryStream(BsaFixture.Create(version: wireVersion));
        var reader = new BsaArchiveReader(stream);
        await Assert.That(reader.ReadBsaArchive().Header.Version).IsEqualTo(expected);
        await Assert.That((await reader.ReadBsaArchiveAsync()).Header.Version).IsEqualTo(expected);
    }

    [Test]
    [Arguments(0u)]
    [Arguments(102u)]
    [Arguments(106u)]
    [Arguments(uint.MaxValue)]
    public async Task Rejects_unknown_wire_versions_in_both_modes(uint version)
    {
        byte[] bytes = BsaFixture.Create();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), version);
        using var stream = new MemoryStream(bytes);
        var reader = new BsaArchiveReader(stream);
        await Assert.That(() => reader.ReadBsaArchive()).Throws<InvalidDataException>();
        await Assert.That(async () => await reader.ReadBsaArchiveAsync()).Throws<InvalidDataException>();
    }

    [Test]
    public async Task Accepts_overstated_filename_table_length_like_shipped_voice_archives()
    {
        byte[] bytes = BsaFixture.Create();
        uint length = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(28));
        uint folderOffset = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(48));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(28), length + 5);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(48), folderOffset + 5);
        using var stream = new MemoryStream(bytes);
        var reader = new BsaArchiveReader(stream);
        var archive = reader.ReadBsaArchive();
        await Assert.That(archive.Entries.Count).IsEqualTo(2);
        await Assert.That(reader.ReadBsaFile(archive.Entries[0].File)).IsEquivalentTo("first payload"u8.ToArray());
    }

    [Test]
    public async Task Supports_root_files_empty_payloads_and_windows_1252_names()
    {
        using var stream = new MemoryStream(BsaFixture.Create(folder: "", firstName: "caf\u00e9.bin", content: []));
        var reader = new BsaArchiveReader(stream);
        var archive = reader.ReadBsaArchive();
        await Assert.That(archive.GetFileByPath("CAFÉ.BIN")).IsNotNull();
        await Assert.That(reader.ReadBsaFile(archive.Entries[0].File).Length).IsEqualTo(0);
        await Assert.That(BsaHash.Compute("")).IsEqualTo(0UL);
        await Assert.That(BsaHash.Compute("a")).IsEqualTo(0x61010061UL);
    }

    [Test]
    [Arguments(0, 0u)]
    [Arguments(4, 999u)]
    [Arguments(8, 0u)]
    [Arguments(16, uint.MaxValue)]
    [Arguments(20, 3u)]
    [Arguments(28, uint.MaxValue)]
    public async Task Rejects_invalid_headers(int offset, uint value)
    {
        byte[] bytes = BsaFixture.Create();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
        using var stream = new MemoryStream(bytes);
        await Assert.That(() => new BsaArchiveReader(stream).ReadBsaArchive()).Throws<InvalidDataException>();
    }

    [Test]
    public async Task Rejects_truncation_and_invalid_payload_ranges()
    {
        using var shortStream = new MemoryStream(new byte[10]);
        await Assert.That(() => new BsaArchiveReader(shortStream).ReadBsaArchive()).Throws<EndOfStreamException>();
        using var stream = new MemoryStream(BsaFixture.Create());
        var reader = new BsaArchiveReader(stream);
        var file = reader.ReadBsaArchive().Entries[0].File with { Offset = uint.MaxValue };
        await Assert.That(() => reader.ReadBsaFile(file)).Throws<InvalidDataException>();
    }

    [Test]
    [Arguments(1u)]
    [Arguments(1000u)]
    public async Task Rejects_incorrect_decompressed_sizes(uint declaredSize)
    {
        byte[] bytes = BsaFixture.Create(compressed: true);
        using var stream = new MemoryStream(bytes);
        var reader = new BsaArchiveReader(stream);
        var file = reader.ReadBsaArchive().Entries[0].File;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan((int)file.Offset), declaredSize);
        await Assert.That(() => reader.ReadBsaFile(file)).Throws<InvalidDataException>();
    }

    [Test]
    public async Task Cancellation_propagates_to_archive_and_payload_reads()
    {
        using var stream = new ModeStream(BsaFixture.Create(), true);
        var reader = new BsaArchiveReader(stream);
        var archive = await reader.ReadBsaArchiveAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.That(async () => await reader.ReadBsaArchiveAsync(cancellation.Token)).Throws<OperationCanceledException>();
        await Assert.That(async () => await reader.ReadBsaFileAsync(archive.Entries[0].File, cancellation.Token)).Throws<OperationCanceledException>();
    }

    private sealed class ModeStream(byte[] bytes, bool asyncOnly) : MemoryStream(bytes, 0, bytes.Length, false, true)
    {
        public int AsyncReads { get; private set; }
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (asyncOnly)
            {
                throw new InvalidOperationException("Synchronous I/O in async mode.");
            }
            return base.Read(buffer, offset, Math.Min(count, 7));
        }
        public override int Read(Span<byte> buffer)
        {
            if (asyncOnly)
            {
                throw new InvalidOperationException("Synchronous I/O in async mode.");
            }
            return base.Read(buffer[..Math.Min(buffer.Length, 7)]);
        }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!asyncOnly)
            {
                throw new InvalidOperationException("Async I/O in sync mode.");
            }
            cancellationToken.ThrowIfCancellationRequested();
            AsyncReads++;
            await Task.Yield();
            // Copy directly to avoid MemoryStream dispatching back to the sync overrides.
            int count = (int)Math.Min(Math.Min(buffer.Length, 7), Length - Position);
            GetBuffer().AsMemory((int)Position, count).CopyTo(buffer);
            Position += count;
            return count;
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    private sealed class AsyncOutput : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count) => throw new InvalidOperationException("Synchronous output in async mode.");
        public override void Write(ReadOnlySpan<byte> buffer) => throw new InvalidOperationException("Synchronous output in async mode.");
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            byte[] bytes = buffer.ToArray();
            base.Write(bytes, 0, bytes.Length);
        }
    }
}
