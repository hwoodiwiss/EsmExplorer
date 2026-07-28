using EsmParser.Core.Format;
using EsmParser.Core.IO;
using EsmParser.Core.Navigation;
using EsmParser.Core.Tests.TestData;

namespace EsmParser.Core.Tests;

/// <summary>Locates the Starfield.esm test resource by walking up from the test output directory.</summary>
internal static class StarfieldEsm
{
    internal static string? FilePath { get; } = Locate();

    private static string? Locate()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "Resources", "Starfield.esm");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }
}

public sealed class SkipUnlessStarfieldEsmAttribute() : SkipAttribute("Resources/Starfield.esm was not found")
{
    public override Task<bool> ShouldSkip(TestRegisteredContext context)
        => Task.FromResult(StarfieldEsm.FilePath is null);
}

/// <summary>
/// Integration tests against the real Starfield master file. All expected values were
/// independently verified with a standalone structural scan of the resource before this
/// parser was written.
/// </summary>
[SkipUnlessStarfieldEsm]
public sealed class StarfieldEsmTests
{
    // Facts about the pinned Resources/Starfield.esm, established by the independent scan.
    private const uint ExpectedRecordAndGroupCount = 3_829_247; // HEDR count, includes TES4 itself
    private const long ExpectedRecordCount = 3_829_246;
    private const long ExpectedGroupCount = 101_341;
    private const long ExpectedCompressedCount = 91_149;
    private const int ExpectedTopGroupCount = 176;

    private static async Task<PluginFile> OpenAsync()
        => (await PluginFile.OpenAsync(FileDataSource.Open(StarfieldEsm.FilePath!))).ShouldSucceed();

    [Test]
    public async Task The_Header_Is_Interpreted_Correctly()
    {
        await using PluginFile plugin = await OpenAsync();

        await Assert.That(plugin.Header.Version).IsEqualTo(0.96f);
        await Assert.That(plugin.Header.RecordAndGroupCount).IsEqualTo(ExpectedRecordAndGroupCount);
        await Assert.That(plugin.Header.RecordHeader.FormVersion).IsEqualTo((ushort)581);
        await Assert.That(plugin.Header.RecordHeader.Flags).IsEqualTo(RecordFlags.Master | RecordFlags.Localized);
        await Assert.That(plugin.Header.Kind).IsEqualTo(PluginKind.Master);
        await Assert.That(plugin.Header.IsLocalized).IsTrue();
        await Assert.That(plugin.Header.Masters.Count).IsEqualTo(0);
        await Assert.That(plugin.Header.Author).IsEqualTo("Python");
        await Assert.That(plugin.Header.Description).IsEqualTo("Script");
        await Assert.That(plugin.Header.RecordHeader.TimestampAsDate).IsEqualTo(new DateOnly(2026, 6, 1));
    }

    [Test]
    public async Task Top_Level_Groups_Tile_The_File_Exactly()
    {
        await using PluginFile plugin = await OpenAsync();
        IReadOnlyList<PluginNode> top = (await plugin.GetTopLevelNodesAsync()).ShouldSucceed();

        await Assert.That(top.Count).IsEqualTo(ExpectedTopGroupCount);

        long position = plugin.HeaderRecord.EndOffset;
        foreach (PluginNode node in top)
        {
            await Assert.That(node.Offset).IsEqualTo(position);
            await Assert.That(node).IsTypeOf<GroupNode>();
            await Assert.That(((GroupNode)node).Header.GroupType).IsEqualTo(GroupType.Top);
            position = node.EndOffset;
        }

        await Assert.That(position).IsEqualTo(plugin.Length);

        var labels = top.Cast<GroupNode>().Select(g => g.Header.LabelAsSignature.ToString()).ToList();
        await Assert.That(labels[0]).IsEqualTo("GMST");
        await Assert.That(labels[1]).IsEqualTo("KYWD");
        await Assert.That(labels[2]).IsEqualTo("FFKW");
        await Assert.That(labels).Contains("PNDT"); // planets: Starfield-specific
        await Assert.That(labels).Contains("STDT"); // star systems: Starfield-specific
    }

    [Test]
    public async Task Game_Setting_Records_Enumerate_With_Consistent_Offsets()
    {
        await using PluginFile plugin = await OpenAsync();
        IReadOnlyList<PluginNode> top = (await plugin.GetTopLevelNodesAsync()).ShouldSucceed();
        var gmst = (GroupNode)top[0];

        IReadOnlyList<PluginNode> children = (await plugin.GetChildrenAsync(gmst)).ShouldSucceed();
        await Assert.That(children.Count).IsEqualTo(2312);

        foreach (PluginNode child in children)
        {
            var record = (RecordNode)child;
            if (record.Header.Signature != Signature.FromString("GMST"))
            {
                throw new InvalidOperationException($"Unexpected record type {record} in the GMST top group.");
            }
        }

        // Spot-check field parsing and the offset contract on the first record.
        var first = (RecordNode)children[0];
        RecordData data = (await plugin.ReadRecordDataAsync(first)).ShouldSucceed();
        RecordField? edid = data.FindField(KnownSignatures.Edid);
        await Assert.That(edid).IsNotNull();
        string editorId = edid!.ReadZString().ShouldSucceed();
        await Assert.That(editorId.Length).IsGreaterThan(0);

        byte[] rawAtReportedOffset = (await plugin.ReadBytesAsync(edid.AbsoluteDataOffset!.Value, edid.Size)).ShouldSucceed();
        await Assert.That(rawAtReportedOffset).IsEquivalentTo(edid.Data.ToArray());
    }

    [Test]
    public async Task Compressed_Records_Inflate()
    {
        await using PluginFile plugin = await OpenAsync();
        IReadOnlyList<PluginNode> top = (await plugin.GetTopLevelNodesAsync()).ShouldSucceed();
        var npcGroup = top.Cast<GroupNode>().First(g => g.Header.LabelAsSignature == Signature.FromString("NPC_"));

        IReadOnlyList<PluginNode> children = (await plugin.GetChildrenAsync(npcGroup)).ShouldSucceed();
        RecordNode compressed = children.OfType<RecordNode>().First(r => r.IsCompressed);

        RecordData data = (await plugin.ReadRecordDataAsync(compressed)).ShouldSucceed();
        await Assert.That(data.WasCompressed).IsTrue();
        await Assert.That(data.Fields.Count).IsGreaterThan(0);
        await Assert.That(data.Fields[0].Signature).IsEqualTo(KnownSignatures.Edid);
        await Assert.That(data.Fields[0].AbsoluteDataOffset).IsNull();
        await Assert.That(data.Data.Length).IsGreaterThan((int)compressed.DataSize);
    }

    [Test]
    public async Task Extended_Size_Fields_Are_Read()
    {
        await using PluginFile plugin = await OpenAsync();
        IReadOnlyList<PluginNode> top = (await plugin.GetTopLevelNodesAsync()).ShouldSucceed();
        var gbfmGroup = top.Cast<GroupNode>().First(g => g.Header.LabelAsSignature == Signature.FromString("GBFM"));

        IReadOnlyList<PluginNode> children = (await plugin.GetChildrenAsync(gbfmGroup)).ShouldSucceed();
        foreach (RecordNode record in children.OfType<RecordNode>().Where(r => r.DataSize > 70_000 && !r.IsCompressed))
        {
            RecordData data = (await plugin.ReadRecordDataAsync(record)).ShouldSucceed();
            RecordField? extended = data.Fields.FirstOrDefault(f => f.HasExtendedSize);
            if (extended is not null)
            {
                await Assert.That(extended.Size).IsGreaterThan(ushort.MaxValue);
                return;
            }
        }

        throw new InvalidOperationException("Expected at least one GBFM record with an XXXX extended-size field.");
    }

    [Test]
    public async Task Find_Locates_A_Record_By_Form_Id()
    {
        await using PluginFile plugin = await OpenAsync();
        IReadOnlyList<PluginNode> top = (await plugin.GetTopLevelNodesAsync()).ShouldSucceed();
        var expected = (RecordNode)(await plugin.GetChildrenAsync((GroupNode)top[0])).ShouldSucceed()[0];

        SearchResult result = await plugin.FindRecordAsync(expected.Header.FormId);
        if (result is not RecordFound(var found))
        {
            throw new InvalidOperationException($"Expected to find {expected}, got {result}");
        }

        await Assert.That(found.Offset).IsEqualTo(expected.Offset);
    }

    [Test]
    public async Task The_Caching_Source_Supports_Navigation()
    {
        var caching = new CachingDataSource(FileDataSource.Open(StarfieldEsm.FilePath!), pageSize: 64 * 1024, maxPages: 64);
        PluginFile plugin = (await PluginFile.OpenAsync(caching)).ShouldSucceed();
        await using (plugin)
        {
            IReadOnlyList<PluginNode> top = (await plugin.GetTopLevelNodesAsync()).ShouldSucceed();
            await Assert.That(top.Count).IsEqualTo(ExpectedTopGroupCount);
            await Assert.That(caching.PageLoadCount).IsGreaterThan(0L);
        }
    }

    [Test]
    public async Task Full_Structural_Statistics_Match_The_Independent_Scan()
    {
        await using PluginFile plugin = await OpenAsync();
        PluginStatistics stats = (await plugin.ComputeStatisticsAsync()).ShouldSucceed();

        await Assert.That(stats.GroupCount).IsEqualTo(ExpectedGroupCount);
        await Assert.That(stats.RecordCount).IsEqualTo(ExpectedRecordCount);
        await Assert.That(stats.CompressedRecordCount).IsEqualTo(ExpectedCompressedCount);
        await Assert.That(stats.MaxDepth).IsEqualTo(6);
        await Assert.That(stats.RecordCount + 1).IsEqualTo((long)plugin.Header.RecordAndGroupCount);

        // Group population by type, exactly as independently scanned.
        var expectedGroupCounts = new Dictionary<GroupType, long>
        {
            [GroupType.Top] = 176,
            [GroupType.WorldChildren] = 432,
            [GroupType.InteriorCellBlock] = 10,
            [GroupType.InteriorCellSubBlock] = 100,
            [GroupType.ExteriorCellBlock] = 1_801,
            [GroupType.ExteriorCellSubBlock] = 1_936,
            [GroupType.CellChildren] = 15_230,
            [GroupType.TopicChildren] = 63_460,
            [GroupType.CellPersistentChildren] = 1_953,
            [GroupType.CellTemporaryChildren] = 14_788,
            [GroupType.QuestChildren] = 1_455,
        };
        await Assert.That(stats.GroupCountsByType.Count).IsEqualTo(expectedGroupCounts.Count);
        foreach ((GroupType type, long count) in expectedGroupCounts)
        {
            await Assert.That(stats.GroupCountsByType[type]).IsEqualTo(count);
        }

        await Assert.That(stats.RecordCountsBySignature[Signature.FromString("REFR")]).IsEqualTo(3_291_860L);
        await Assert.That(stats.RecordCountsBySignature[Signature.FromString("PNDT")]).IsEqualTo(1_765L);
    }
}
