using System.Text.Json;
using BethesdaArchiveParser.Core.Tests;
using EsmParser.Web.Interop;
using EsmParser.Web.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;

namespace EsmParser.Web.Tests;

public sealed class GameDataTests
{
    [Test]
    public async Task Resolves_archive_by_container_name_with_normalized_paths_and_loose_overrides()
    {
        var js = new BrowserFiles();
        js.Files["a.bsa"] = BsaFixture.Create(compressed: true);
        js.Files["z.bsa"] = BsaFixture.Create(content: "last archive"u8.ToArray());
        await using var service = new GameDataService(js, NullLogger<GameDataService>.Instance);
        await Assert.That(await service.PickAsync()).IsTrue();
        await Assert.That(service.ArchiveCount).IsEqualTo(2);
        await Assert.That(await service.ResolveAsync("Data\\MESHES\\FIRST.BIN")).IsEquivalentTo("last archive"u8.ToArray());
        js.Files["meshes/first.bin"] = "loose override"u8.ToArray();
        await Assert.That(await service.ResolveAsync("meshes/first.bin")).IsEquivalentTo("loose override"u8.ToArray());
        await Assert.That(await service.ResolveAsync("missing.nif")).IsNull();
        await Assert.That(js.OpenCount).IsEqualTo(0);
    }

    [Test]
    public async Task Root_reselection_clears_stale_entries_and_bad_archives_do_not_block_good_ones()
    {
        var js = new BrowserFiles();
        js.Files["good.bsa"] = BsaFixture.Create(compressed: true);
        js.Files["bad.bsa"] = new byte[36];
        await using var service = new GameDataService(js, NullLogger<GameDataService>.Instance);
        await service.PickAsync();
        await Assert.That(service.ArchiveErrors.Count).IsEqualTo(1);
        await Assert.That(await service.ResolveAsync("meshes/first.bin")).IsEquivalentTo("first payload"u8.ToArray());
        js.CancelPicker = true;
        await Assert.That(await service.PickAsync()).IsFalse();
        await Assert.That(service.ArchiveCount).IsEqualTo(1);
        js.CancelPicker = false;
        js.Files.Clear();
        await service.PickAsync();
        await Assert.That(service.ArchiveCount).IsEqualTo(0);
        await Assert.That(service.ArchiveErrors.Count).IsEqualTo(0);
        await Assert.That(await service.ResolveAsync("meshes/first.bin")).IsNull();
        await Assert.That(js.OpenCount).IsEqualTo(0);
    }

    [Test]
    public async Task Failed_payload_releases_browser_handle_and_concurrent_requests_use_independent_streams()
    {
        var js = new BrowserFiles();
        byte[] archiveBytes = BsaFixture.Create(compressed: true);
        js.Files["test.bsa"] = archiveBytes;
        await using var service = new GameDataService(js, NullLogger<GameDataService>.Instance);
        await service.PickAsync();
        var results = await Task.WhenAll(service.ResolveAsync("meshes/first.bin"), service.ResolveAsync("meshes/second.bin"));
        await Assert.That(results[0]).IsEquivalentTo("first payload"u8.ToArray());
        await Assert.That(results[1]).IsEquivalentTo("second payload must not leak"u8.ToArray());
        using var metadata = new MemoryStream(archiveBytes);
        var file = new BethesdaArchiveParser.Core.Reader.BsaArchiveReader(metadata).ReadBsaArchive().Entries[0].File;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(archiveBytes.AsSpan((int)file.Offset), 1);
        await Assert.That(async () => await service.ResolveAsync("meshes/first.bin")).Throws<InvalidDataException>();
        await Assert.That(js.OpenCount).IsEqualTo(0);
    }

    [Test]
    public async Task Browser_stream_supports_seek_after_eof_chunking_and_cancellation()
    {
        var js = new BrowserFiles();
        js.Files["large.bin"] = new byte[100000];
        await using (var stream = await SeekablePullFromJSDataStream.OpenAsync(js, "large.bin"))
        {
            byte[] buffer = new byte[100000];
            await Assert.That(await stream!.ReadAsync(buffer)).IsEqualTo(65536);
            await Assert.That(stream.Seek(-1, SeekOrigin.End)).IsEqualTo(99999L);
            await Assert.That(await stream.ReadAsync(buffer)).IsEqualTo(1);
            await Assert.That(await stream.ReadAsync(buffer)).IsEqualTo(0);
            stream.Position = 0;
            await Assert.That(await stream.ReadAsync(buffer)).IsEqualTo(65536);
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            await Assert.That(async () => await stream.ReadAsync(buffer, cts.Token)).Throws<OperationCanceledException>();
        }
        await Assert.That(js.OpenCount).IsEqualTo(0);
    }

    private sealed class BrowserFiles : IJSRuntime, IJSObjectReference
    {
        public Dictionary<string, byte[]> Files { get; } = [with(StringComparer.OrdinalIgnoreCase)];
        private readonly Dictionary<int, byte[]> _open = [];
        private int _id;
        public bool CancelPicker { get; set; }
        public int OpenCount => _open.Count;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            try
            {
                return await DispatchAsync<TValue>(identifier, args, default);
            }
            catch (JSException)
            {
                throw;
            }
        }
        public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            try
            {
                return await DispatchAsync<TValue>(identifier, args, cancellationToken);
            }
            catch (JSException)
            {
                throw;
            }
        }

        private async ValueTask<TValue> DispatchAsync<TValue>(string identifier, object?[]? args, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            object? result = null;
            switch (identifier)
            {
                case "import": return (TValue)(object)this;
                case "isSupported": result = true; break;
                case "pickDataRoot": result = CancelPicker ? null : "Data"; break;
                case "listFiles": result = Files.Keys.Where(static p => !p.Contains('/')).ToArray(); break;
                case "openFile":
                    if (Files.TryGetValue((string)args![0]!, out var bytes))
                    {
                        _open[++_id] = bytes;
                        result = new { Id = _id, Length = (long)bytes.Length };
                    }
                    break;
                case "readFileChunk":
                    result = _open[(int)args![0]!].AsSpan((int)(long)args[1]!, (int)args[2]!).ToArray();
                    break;
                case "closeFile": _open.Remove((int)args![0]!); break;
                default: throw new InvalidOperationException(identifier);
            }
            return result is null ? default! : result is TValue value ? value : JsonSerializer.Deserialize<TValue>(JsonSerializer.Serialize(result))!;
        }
    }
}
