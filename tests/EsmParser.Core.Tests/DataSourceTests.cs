using EsmParser.Core.IO;
using EsmParser.Core.Tests.TestData;

namespace EsmParser.Core.Tests;

public sealed class MemoryDataSourceTests
{
    [Test]
    public async Task Reads_The_Requested_Range()
    {
        byte[] data = [0, 1, 2, 3, 4, 5, 6, 7];
        await using var source = new MemoryDataSource(data, "test");

        byte[] buffer = new byte[3];
        (await source.ReadExactlyAsync(2, buffer)).ShouldSucceed();
        await Assert.That(buffer).IsEquivalentTo(new byte[] { 2, 3, 4 });
        await Assert.That(source.Length).IsEqualTo(8L);
        await Assert.That(source.Name).IsEqualTo("test");
    }

    [Test]
    public async Task Reading_Past_The_End_Is_A_Data_Error()
    {
        await using var source = new MemoryDataSource(new byte[4], "test");
        ParseError error = (await source.ReadExactlyAsync(2, new byte[3])).ShouldFail();
        await Assert.That(error.Kind).IsEqualTo(ParseErrorKind.UnexpectedEndOfData);
        await Assert.That(error.Offset).IsEqualTo(2L);
    }

    [Test]
    public async Task Negative_Offset_Is_A_Contract_Violation()
    {
        await using var source = new MemoryDataSource(new byte[4], "test");
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            async () => await source.ReadExactlyAsync(-1, new byte[1]));
    }
}

public sealed class FileDataSourceTests
{
    [Test]
    public async Task Reads_From_A_File_On_Disk()
    {
        string path = Path.Combine(Path.GetTempPath(), $"esmparser-test-{Guid.NewGuid():N}.bin");
        byte[] data = new byte[1024];
        Random.Shared.NextBytes(data);
        await File.WriteAllBytesAsync(path, data);
        try
        {
            await using FileDataSource source = FileDataSource.Open(path);
            await Assert.That(source.Length).IsEqualTo(1024L);

            byte[] buffer = new byte[100];
            (await source.ReadExactlyAsync(512, buffer)).ShouldSucceed();
            await Assert.That(buffer).IsEquivalentTo(data.AsSpan(512, 100).ToArray());

            ParseError error = (await source.ReadExactlyAsync(1000, new byte[100])).ShouldFail();
            await Assert.That(error.Kind).IsEqualTo(ParseErrorKind.UnexpectedEndOfData);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

public sealed class CachingDataSourceTests
{
    [Test]
    public async Task Serves_Repeat_Reads_From_Cache()
    {
        byte[] data = new byte[256];
        Random.Shared.NextBytes(data);
        var caching = new CachingDataSource(new MemoryDataSource(data, "test"), pageSize: 64, maxPages: 4);
        await using (caching)
        {
            byte[] buffer = new byte[16];
            (await caching.ReadExactlyAsync(0, buffer)).ShouldSucceed();
            (await caching.ReadExactlyAsync(16, buffer)).ShouldSucceed();
            (await caching.ReadExactlyAsync(32, buffer)).ShouldSucceed();
            await Assert.That(caching.PageLoadCount).IsEqualTo(1L);
        }
    }

    [Test]
    public async Task Assembles_Reads_That_Span_Pages()
    {
        byte[] data = new byte[256];
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = (byte)i;
        }

        await using var caching = new CachingDataSource(new MemoryDataSource(data, "test"), pageSize: 64, maxPages: 4);
        byte[] buffer = new byte[100];
        (await caching.ReadExactlyAsync(30, buffer)).ShouldSucceed();
        await Assert.That(buffer).IsEquivalentTo(data.AsSpan(30, 100).ToArray());
        await Assert.That(caching.PageLoadCount).IsEqualTo(3L);
    }

    [Test]
    public async Task Evicts_Least_Recently_Used_Pages()
    {
        byte[] data = new byte[64 * 4];
        await using var caching = new CachingDataSource(new MemoryDataSource(data, "test"), pageSize: 64, maxPages: 2);

        byte[] buffer = new byte[1];
        (await caching.ReadExactlyAsync(0, buffer)).ShouldSucceed();    // load page 0
        (await caching.ReadExactlyAsync(64, buffer)).ShouldSucceed();   // load page 1
        (await caching.ReadExactlyAsync(0, buffer)).ShouldSucceed();    // hit page 0 (freshens it)
        (await caching.ReadExactlyAsync(128, buffer)).ShouldSucceed();  // load page 2, evicting page 1
        (await caching.ReadExactlyAsync(0, buffer)).ShouldSucceed();    // still cached
        await Assert.That(caching.PageLoadCount).IsEqualTo(3L);

        (await caching.ReadExactlyAsync(64, buffer)).ShouldSucceed();   // page 1 was evicted: reload
        await Assert.That(caching.PageLoadCount).IsEqualTo(4L);
    }

    [Test]
    public async Task Handles_The_Short_Final_Page()
    {
        byte[] data = new byte[100]; // pages: 64 + 36
        data[99] = 0xAB;
        await using var caching = new CachingDataSource(new MemoryDataSource(data, "test"), pageSize: 64, maxPages: 4);
        byte[] buffer = new byte[1];
        (await caching.ReadExactlyAsync(99, buffer)).ShouldSucceed();
        await Assert.That(buffer[0]).IsEqualTo((byte)0xAB);
    }

    [Test]
    public async Task Propagates_Range_Errors()
    {
        await using var caching = new CachingDataSource(new MemoryDataSource(new byte[10], "test"), pageSize: 64, maxPages: 4);
        ParseError error = (await caching.ReadExactlyAsync(5, new byte[10])).ShouldFail();
        await Assert.That(error.Kind).IsEqualTo(ParseErrorKind.UnexpectedEndOfData);
    }
}
