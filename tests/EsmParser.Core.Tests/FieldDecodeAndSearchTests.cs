using EsmParser.Core.Format;
using EsmParser.Core.IO;
using EsmParser.Core.Navigation;
using EsmParser.Core.Tests.TestData;

namespace EsmParser.Core.Tests;

public sealed class RecordFieldDecodeTests
{
    private static async Task<RecordField> SingleFieldAsync(Action<RecordFieldsBuilder> build)
    {
        MemoryDataSource source = new PluginBuilder()
            .AddRecord("MISC", 0x100, build)
            .BuildSource();

        PluginFile plugin = (await PluginFile.OpenAsync(source)).ShouldSucceed();
        await using (plugin)
        {
            var record = (RecordNode)(await plugin.GetTopLevelNodesAsync()).ShouldSucceed()[0];
            RecordData data = (await plugin.ReadRecordDataAsync(record)).ShouldSucceed();
            return data.Fields[0];
        }
    }

    [Test]
    public async Task ZString_Trims_The_Terminator()
    {
        RecordField field = await SingleFieldAsync(f => f.AddZString("EDID", "Hello"));
        await Assert.That(field.ReadZString().ShouldSucceed()).IsEqualTo("Hello");
    }

    [Test]
    public async Task ZString_Tolerates_A_Missing_Terminator()
    {
        RecordField field = await SingleFieldAsync(f => f.AddField("EDID", "Hi"u8.ToArray()));
        await Assert.That(field.ReadZString().ShouldSucceed()).IsEqualTo("Hi");
    }

    [Test]
    public async Task Empty_ZString_Is_Empty()
    {
        RecordField field = await SingleFieldAsync(f => f.AddField("EDID", [0]));
        await Assert.That(field.ReadZString().ShouldSucceed()).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task Scalars_Decode_Little_Endian()
    {
        RecordField uint32 = await SingleFieldAsync(f => f.AddField("DATA", 0x78, 0x56, 0x34, 0x12));
        await Assert.That(uint32.ReadUInt32().ShouldSucceed()).IsEqualTo(0x12345678u);
        await Assert.That(uint32.ReadInt32().ShouldSucceed()).IsEqualTo(0x12345678);
        await Assert.That(uint32.ReadFormId().ShouldSucceed()).IsEqualTo(new FormId(0x12345678));

        RecordField uint16 = await SingleFieldAsync(f => f.AddField("DATA", 0xCD, 0xAB));
        await Assert.That(uint16.ReadUInt16().ShouldSucceed()).IsEqualTo((ushort)0xABCD);

        RecordField uint64 = await SingleFieldAsync(f => f.AddField("DATA", 1, 0, 0, 0, 0, 0, 0, 0x80));
        await Assert.That(uint64.ReadUInt64().ShouldSucceed()).IsEqualTo(0x8000_0000_0000_0001UL);

        RecordField float32 = await SingleFieldAsync(f => f.AddField("DATA", 0x8F, 0xC2, 0x75, 0x3F));
        await Assert.That(float32.ReadFloat32().ShouldSucceed()).IsEqualTo(0.96f);
    }

    [Test]
    public async Task Scalar_Size_Mismatches_Are_Field_Errors()
    {
        RecordField threeBytes = await SingleFieldAsync(f => f.AddField("DATA", 1, 2, 3));

        await Assert.That(threeBytes.ReadUInt32().ShouldFail().Kind).IsEqualTo(ParseErrorKind.FieldInvalid);
        await Assert.That(threeBytes.ReadInt32().ShouldFail().Kind).IsEqualTo(ParseErrorKind.FieldInvalid);
        await Assert.That(threeBytes.ReadUInt16().ShouldFail().Kind).IsEqualTo(ParseErrorKind.FieldInvalid);
        await Assert.That(threeBytes.ReadUInt64().ShouldFail().Kind).IsEqualTo(ParseErrorKind.FieldInvalid);
        await Assert.That(threeBytes.ReadFloat32().ShouldFail().Kind).IsEqualTo(ParseErrorKind.FieldInvalid);
        await Assert.That(threeBytes.ReadFormId().ShouldFail().Kind).IsEqualTo(ParseErrorKind.FieldInvalid);
    }

    [Test]
    public async Task FormId_Arrays_Decode_As_Packed_Values()
    {
        RecordField field = await SingleFieldAsync(f => f.AddField(
            "ONAM",
            0x01, 0x00, 0x00, 0x00,
            0x02, 0x00, 0x00, 0x01));

        IReadOnlyList<FormId> ids = field.ReadFormIdArray().ShouldSucceed();
        await Assert.That(ids.Count).IsEqualTo(2);
        await Assert.That(ids[0]).IsEqualTo(new FormId(1));
        await Assert.That(ids[1]).IsEqualTo(new FormId(0x01000002));
    }

    [Test]
    public async Task A_Misaligned_FormId_Array_Is_A_Field_Error()
    {
        RecordField field = await SingleFieldAsync(f => f.AddField("ONAM", 1, 2, 3, 4, 5));
        await Assert.That(field.ReadFormIdArray().ShouldFail().Kind).IsEqualTo(ParseErrorKind.FieldInvalid);
    }
}

public sealed class SearchAndStatisticsTests
{
    private static MemoryDataSource BuildNestedPlugin() => new PluginBuilder()
        .AddTopGroup("GMST", g => g
            .AddRecord("GMST", 0x100)
            .AddRecord("GMST", 0x101))
        .AddTopGroup("CELL", g => g
            .AddRecord("CELL", 0x200)
            .AddGroup(GroupType.CellChildren, 0x200, cell => cell
                .AddGroup(GroupType.CellTemporaryChildren, 0x200, temp => temp
                    .AddRecord("REFR", 0x201, flags: RecordFlags.Compressed))))
        .BuildSource();

    [Test]
    public async Task Finds_A_Deeply_Nested_Record()
    {
        await using PluginFile plugin = (await PluginFile.OpenAsync(BuildNestedPlugin())).ShouldSucceed();

        SearchResult result = await plugin.FindRecordAsync(new FormId(0x201));
        if (result is not RecordFound(var record))
        {
            throw new InvalidOperationException($"Expected the record to be found, got {result}");
        }

        await Assert.That(record.Header.Signature).IsEqualTo(Signature.FromString("REFR"));
        await Assert.That(record.Depth).IsEqualTo(3);
    }

    [Test]
    public async Task Reports_Clean_Not_Found()
    {
        await using PluginFile plugin = (await PluginFile.OpenAsync(BuildNestedPlugin())).ShouldSucceed();
        SearchResult result = await plugin.FindRecordAsync(new FormId(0xDEAD));
        await Assert.That(result is RecordNotFound).IsTrue();
    }

    [Test]
    public async Task Search_Can_Be_Rooted_To_A_Subtree()
    {
        await using PluginFile plugin = (await PluginFile.OpenAsync(BuildNestedPlugin())).ShouldSucceed();
        IReadOnlyList<PluginNode> top = (await plugin.GetTopLevelNodesAsync()).ShouldSucceed();
        var gmstGroup = (GroupNode)top[0];

        // 0x201 lives under CELL, not GMST.
        SearchResult inGmst = await plugin.FindRecordAsync(new FormId(0x201), gmstGroup);
        await Assert.That(inGmst is RecordNotFound).IsTrue();

        SearchResult inCell = await plugin.FindRecordAsync(new FormId(0x201), (GroupNode)top[1]);
        await Assert.That(inCell is RecordFound).IsTrue();
    }

    [Test]
    public async Task Search_Surfaces_Structural_Errors()
    {
        var invalidGroup = new GroupHeader(TotalSize: 10, 0, GroupType.Top, 0, 0, 0);
        byte[] groupBytes = new byte[GroupHeader.Size];
        invalidGroup.WriteTo(groupBytes);

        MemoryDataSource source = new PluginBuilder().AddRawBytes(groupBytes).BuildSource();
        await using PluginFile plugin = (await PluginFile.OpenAsync(source)).ShouldSucceed();

        SearchResult result = await plugin.FindRecordAsync(new FormId(1));
        if (result is not ParseError error)
        {
            throw new InvalidOperationException($"Expected a parse error, got {result}");
        }

        await Assert.That(error.Kind).IsEqualTo(ParseErrorKind.StructureInvalid);
    }

    [Test]
    public async Task Statistics_Count_The_Whole_Tree()
    {
        await using PluginFile plugin = (await PluginFile.OpenAsync(BuildNestedPlugin())).ShouldSucceed();
        PluginStatistics stats = (await plugin.ComputeStatisticsAsync()).ShouldSucceed();

        await Assert.That(stats.GroupCount).IsEqualTo(4L);
        await Assert.That(stats.RecordCount).IsEqualTo(4L);
        await Assert.That(stats.CompressedRecordCount).IsEqualTo(1L);
        await Assert.That(stats.MaxDepth).IsEqualTo(3);
        await Assert.That(stats.TotalNodeCount).IsEqualTo(8L);

        await Assert.That(stats.RecordCountsBySignature[Signature.FromString("GMST")]).IsEqualTo(2L);
        await Assert.That(stats.RecordCountsBySignature[Signature.FromString("REFR")]).IsEqualTo(1L);
        await Assert.That(stats.GroupCountsByType[GroupType.Top]).IsEqualTo(2L);
        await Assert.That(stats.GroupCountsByType[GroupType.CellChildren]).IsEqualTo(1L);
        await Assert.That(stats.GroupCountsByType[GroupType.CellTemporaryChildren]).IsEqualTo(1L);
    }

    [Test]
    public async Task Statistics_Can_Be_Rooted_To_A_Subtree()
    {
        await using PluginFile plugin = (await PluginFile.OpenAsync(BuildNestedPlugin())).ShouldSucceed();
        IReadOnlyList<PluginNode> top = (await plugin.GetTopLevelNodesAsync()).ShouldSucceed();

        PluginStatistics stats = (await plugin.ComputeStatisticsAsync((GroupNode)top[0])).ShouldSucceed();
        await Assert.That(stats.GroupCount).IsEqualTo(0L);
        await Assert.That(stats.RecordCount).IsEqualTo(2L);
        await Assert.That(stats.MaxDepth).IsEqualTo(1);
    }
}
