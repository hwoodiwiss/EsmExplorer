using System.Security.Cryptography;
using BethesdaArchiveParser.Core.Reader;

namespace BethesdaArchiveParser.Core.Tests;

public sealed class SkipUnlessBsaFixtureAttribute() : SkipAttribute("Set BSA_TEST_ARCHIVE and BSA_TEST_MANIFEST to verify a supplied regression fixture.")
{
    public override Task<bool> ShouldSkip(TestRegisteredContext context) => Task.FromResult(
        string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BSA_TEST_ARCHIVE"))
        || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BSA_TEST_MANIFEST")));
}

public sealed class SkipUnlessLocalArchivesAttribute() : SkipAttribute("Set BSA_VERIFY_DIRECTORY for an opt-in local archive smoke check.")
{
    public override Task<bool> ShouldSkip(TestRegisteredContext context) =>
        Task.FromResult(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BSA_VERIFY_DIRECTORY")));
}

public sealed class ExternalArchiveTests
{
    [Test, SkipUnlessBsaFixture]
    public async Task Supplied_archive_matches_independently_verified_manifest()
    {
        // Format: SHA256<TAB>archive-relative path. Obtain hashes from independently extracted originals.
        string archivePath = Environment.GetEnvironmentVariable("BSA_TEST_ARCHIVE")!;
        string manifestPath = Environment.GetEnvironmentVariable("BSA_TEST_MANIFEST")!;
        var expected = (await File.ReadAllLinesAsync(manifestPath))
            .Where(static line => line.Length > 0 && !line.StartsWith('#'))
            .Select(static line => line.Split('\t', 2))
            .ToDictionary(static fields => fields[1].Replace('/', '\\'), static fields => fields[0], StringComparer.OrdinalIgnoreCase);
        await using var stream = File.OpenRead(archivePath);
        var reader = new BsaArchiveReader(stream);
        var archive = await reader.ReadBsaArchiveAsync();
        await Assert.That(archive.Entries.Count).IsEqualTo(expected.Count);
        foreach (var entry in archive.Entries)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            using var destination = new HashOutput(hash);
            await reader.CopyBsaFileToAsync(entry.File, destination);
            await Assert.That(Convert.ToHexString(hash.GetHashAndReset())).IsEqualTo(expected[entry.Path].ToUpperInvariant());
            reader.CopyBsaFileTo(entry.File, destination);
            await Assert.That(Convert.ToHexString(hash.GetHashAndReset())).IsEqualTo(expected[entry.Path].ToUpperInvariant());
        }
    }

    [Test, SkipUnlessLocalArchives]
    public async Task Local_archives_parse_and_every_payload_streams_successfully()
    {
        var paths = Directory.EnumerateFiles(Environment.GetEnvironmentVariable("BSA_VERIFY_DIRECTORY")!)
            .Where(static path => Path.GetExtension(path).Equals(".bsa", StringComparison.OrdinalIgnoreCase)).ToArray();
        await Assert.That(paths.Length > 0).IsTrue();
        foreach (string path in paths)
        {
            using var stream = File.OpenRead(path);
            try { _ = new BsaArchiveReader(stream).ReadBsaArchive(); }
            catch (InvalidDataException ex) { throw new InvalidDataException($"{path}: {ex.Message}", ex); }
        }
        foreach (string path in paths)
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous);
            var reader = new BsaArchiveReader(stream);
            var archive = await reader.ReadBsaArchiveAsync();
            await Assert.That(archive.Entries.Count).IsEqualTo((int)archive.Header.FileCount);
            foreach (var entry in archive.Entries)
            {
                await reader.CopyBsaFileToAsync(entry.File, Stream.Null);
            }
            Console.WriteLine($"Verified {Path.GetFileName(path)}: {archive.Entries.Count} entries");
        }
    }

    private sealed class HashOutput(IncrementalHash hash) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Write(byte[] buffer, int offset, int count) => hash.AppendData(buffer, offset, count);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            hash.AppendData(buffer.Span);
            return ValueTask.CompletedTask;
        }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
