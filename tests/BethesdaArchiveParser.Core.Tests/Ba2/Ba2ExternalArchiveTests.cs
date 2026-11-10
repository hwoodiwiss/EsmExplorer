using System.Security.Cryptography;
using BethesdaArchiveParser.Core.Ba2;
using BethesdaArchiveParser.Core.Reader;

namespace BethesdaArchiveParser.Core.Tests;

public sealed class SkipUnlessBa2FixtureAttribute() : SkipAttribute("Set BA2_TEST_ARCHIVE and BA2_TEST_MANIFEST to verify a supplied BA2 regression fixture.")
{
    public override Task<bool> ShouldSkip(TestRegisteredContext context) => Task.FromResult(
        string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BA2_TEST_ARCHIVE"))
        || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BA2_TEST_MANIFEST")));
}

public sealed class SkipUnlessLocalBa2ArchivesAttribute() : SkipAttribute("Set BA2_VERIFY_DIRECTORY for an opt-in local BA2 smoke check.")
{
    public override Task<bool> ShouldSkip(TestRegisteredContext context) =>
        Task.FromResult(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BA2_VERIFY_DIRECTORY")));
}

public sealed class Ba2ExternalArchiveTests
{
    [Test, SkipUnlessBa2Fixture]
    public async Task Supplied_archive_matches_independently_verified_manifest()
    {
        // Format: SHA256<TAB>archive-relative path. Obtain hashes from Archive2/BSArch extractions, never this parser.
        var expected = (await File.ReadAllLinesAsync(Environment.GetEnvironmentVariable("BA2_TEST_MANIFEST")!))
            .Where(static line => line.Length > 0 && !line.StartsWith('#'))
            .Select(static line => line.Split('\t', 2))
            .ToDictionary(static fields => fields[1].Replace('/', '\\'), static fields => fields[0], StringComparer.OrdinalIgnoreCase);
        await using var stream = File.OpenRead(Environment.GetEnvironmentVariable("BA2_TEST_ARCHIVE")!);
        var reader = new ArchiveReader(stream);
        var archive = await reader.ReadArchiveAsync();
        await Assert.That(archive.Format).IsEqualTo(ArchiveFormat.Ba2);
        await Assert.That(archive.Entries.Count).IsEqualTo(expected.Count);
        foreach (var entry in archive.Entries)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            using var destination = new HashOutput(hash);
            await reader.CopyFileToAsync(entry, destination);
            await Assert.That(Convert.ToHexString(hash.GetHashAndReset())).IsEqualTo(expected[entry.Path].ToUpperInvariant());
            reader.CopyFileTo(entry, destination);
            await Assert.That(Convert.ToHexString(hash.GetHashAndReset())).IsEqualTo(expected[entry.Path].ToUpperInvariant());
        }
    }

    [Test, SkipUnlessLocalBa2Archives]
    public async Task Local_archives_parse_hash_and_stream_successfully()
    {
        var paths = Directory.EnumerateFiles(Environment.GetEnvironmentVariable("BA2_VERIFY_DIRECTORY")!)
            .Where(static path => Path.GetExtension(path).Equals(".ba2", StringComparison.OrdinalIgnoreCase)).ToArray();
        await Assert.That(paths.Length > 0).IsTrue();
        foreach (string path in paths)
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous);
            var reader = new ArchiveReader(stream);
            Archive archive;
            try
            {
                archive = await reader.ReadArchiveAsync();
            }
            catch (InvalidDataException ex)
            {
                throw new InvalidDataException($"{path}: {ex.Message}", ex);
            }
            var details = (Ba2Archive)archive.Details.Value!;
            await Assert.That(archive.Entries.Count).IsEqualTo((int)details.Header.FileCount);
            var mismatches = new List<string>();
            int nonAsciiMismatches = 0;
            foreach (var entry in details.Entries)
            {
                var hash = Ba2Hash.Compute(entry.Path);
                if (hash.Name != entry.File.NameHash || hash.Extension != entry.File.Extension || hash.Directory != entry.File.DirectoryHash)
                {
                    // Shipped hashes for non-ASCII names (e.g. Fallout4 - Voices.ba2 "María_F.fuz") match no known
                    // encoding; hashes are never used for lookup here, so only ASCII paths are asserted.
                    if (!entry.Path.All(char.IsAscii))
                    {
                        nonAsciiMismatches++;
                        continue;
                    }
                    mismatches.Add($"{entry.Path}: computed {hash}, stored ({entry.File.NameHash:X8}, {entry.File.Extension:X8}, {entry.File.DirectoryHash:X8})");
                }
            }
            await Assert.That(string.Join(Environment.NewLine, mismatches)).IsEqualTo("");
            foreach (var entry in archive.Entries)
            {
                await reader.CopyFileToAsync(entry, Stream.Null);
            }
            Console.WriteLine($"Verified {Path.GetFileName(path)}: {details.Header.Version} {details.Header.Type} " +
                $"{details.Header.CompressionMethod}, {archive.Entries.Count} entries, {nonAsciiMismatches} non-ASCII hash mismatches");
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
        public override void Write(ReadOnlySpan<byte> buffer) => hash.AppendData(buffer);
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
