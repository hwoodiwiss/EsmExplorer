using EsmParser.Core.Format;
using EsmParser.Core.IO;
using EsmParser.Core.Navigation;
using EsmParser.Core.Tests.TestData;

namespace EsmParser.Core.Tests;

public sealed class NavigationTests
{
    [Test]
    public async Task Top_Level_Nodes_Report_Exact_Offsets_And_Sizes()
    {
        MemoryDataSource source = new PluginBuilder()
            .AddTopGroup("GMST", g => g
                .AddRecord("GMST", 0x100, f => f.AddZString("EDID", "fTestSetting")))
            .AddRecord("WEAP", 0x200, f => f.AddUInt32("DATA", 7))
            .BuildSource();

        await using PluginFile plugin = (await PluginFile.OpenAsync(source)).ShouldSucceed();
        IReadOnlyList<PluginNode> nodes = (await plugin.GetTopLevelNodesAsync()).ShouldSucceed();

        await Assert.That(nodes.Count).IsEqualTo(2);

        // Top-level layout is contiguous from the end of TES4 to the end of the file.
        await Assert.That(nodes[0].Offset).IsEqualTo(plugin.HeaderRecord.EndOffset);
        await Assert.That(nodes[1].Offset).IsEqualTo(nodes[0].EndOffset);
        await Assert.That(nodes[1].EndOffset).IsEqualTo(plugin.Length);

        var group = (GroupNode)nodes[0];
        await Assert.That(group.Header.GroupType).IsEqualTo(GroupType.Top);
        await Assert.That(group.Header.LabelAsSignature).IsEqualTo(Signature.FromString("GMST"));
        await Assert.That(group.Depth).IsEqualTo(0);
        await Assert.That(group.DataOffset).IsEqualTo(group.Offset + 24);

        var record = (RecordNode)nodes[1];
        await Assert.That(record.Header.Signature).IsEqualTo(Signature.FromString("WEAP"));
        await Assert.That(record.Header.FormId).IsEqualTo(new FormId(0x200));
        await Assert.That(record.DataSize).IsEqualTo(10L); // 6-byte field header + 4-byte payload
    }

    [Test]
    public async Task Group_Children_Are_Enumerated_With_Incremented_Depth()
    {
        MemoryDataSource source = new PluginBuilder()
            .AddTopGroup("CELL", g => g
                .AddRecord("CELL", 0x300)
                .AddGroup(GroupType.CellChildren, 0x300, cell => cell
                    .AddGroup(GroupType.CellTemporaryChildren, 0x300, temp => temp
                        .AddRecord("REFR", 0x301)
                        .AddRecord("REFR", 0x302))))
            .BuildSource();

        await using PluginFile plugin = (await PluginFile.OpenAsync(source)).ShouldSucceed();
        IReadOnlyList<PluginNode> top = (await plugin.GetTopLevelNodesAsync()).ShouldSucceed();
        var cellTop = (GroupNode)top[0];

        IReadOnlyList<PluginNode> cellChildren = (await plugin.GetChildrenAsync(cellTop)).ShouldSucceed();
        await Assert.That(cellChildren.Count).IsEqualTo(2);
        await Assert.That(cellChildren[0]).IsTypeOf<RecordNode>();
        await Assert.That(cellChildren[1]).IsTypeOf<GroupNode>();
        await Assert.That(cellChildren[0].Depth).IsEqualTo(1);

        var childrenGroup = (GroupNode)cellChildren[1];
        await Assert.That(childrenGroup.Header.GroupType).IsEqualTo(GroupType.CellChildren);

        IReadOnlyList<PluginNode> temporary = (await plugin.GetChildrenAsync(childrenGroup)).ShouldSucceed();
        await Assert.That(temporary.Count).IsEqualTo(1);
        var temporaryGroup = (GroupNode)temporary[0];
        await Assert.That(temporaryGroup.Depth).IsEqualTo(2);

        IReadOnlyList<PluginNode> refs = (await plugin.GetChildrenAsync(temporaryGroup)).ShouldSucceed();
        await Assert.That(refs.Count).IsEqualTo(2);
        await Assert.That(refs[0].Depth).IsEqualTo(3);
        await Assert.That(((RecordNode)refs[1]).Header.FormId).IsEqualTo(new FormId(0x302));

        // Children exactly fill their parent group.
        await Assert.That(refs[0].Offset).IsEqualTo(temporaryGroup.DataOffset);
        await Assert.That(refs[1].EndOffset).IsEqualTo(temporaryGroup.EndOffset);
    }

    [Test]
    public async Task An_Empty_Group_Has_No_Children()
    {
        MemoryDataSource source = new PluginBuilder()
            .AddTopGroup("KYWD")
            .BuildSource();

        await using PluginFile plugin = (await PluginFile.OpenAsync(source)).ShouldSucceed();
        IReadOnlyList<PluginNode> top = (await plugin.GetTopLevelNodesAsync()).ShouldSucceed();
        IReadOnlyList<PluginNode> children = (await plugin.GetChildrenAsync((GroupNode)top[0])).ShouldSucceed();
        await Assert.That(children.Count).IsEqualTo(0);
    }

    [Test]
    public async Task A_Group_Smaller_Than_Its_Header_Is_Invalid()
    {
        var invalidGroup = new GroupHeader(TotalSize: 10, 0, GroupType.Top, 0, 0, 0);
        byte[] groupBytes = new byte[GroupHeader.Size];
        invalidGroup.WriteTo(groupBytes);

        MemoryDataSource source = new PluginBuilder().AddRawBytes(groupBytes).BuildSource();
        await using PluginFile plugin = (await PluginFile.OpenAsync(source)).ShouldSucceed();

        ParseError error = (await plugin.GetTopLevelNodesAsync()).ShouldFail();
        await Assert.That(error.Kind).IsEqualTo(ParseErrorKind.StructureInvalid);
    }

    [Test]
    public async Task A_Record_Overrunning_Its_Container_Is_Invalid()
    {
        // A record claiming 1000 data bytes when only its header exists.
        var header = new RecordHeader(Signature.FromString("WEAP"), 1000, RecordFlags.None, new FormId(1), 0, 0, 581, 0);
        byte[] headerBytes = new byte[RecordHeader.Size];
        header.WriteTo(headerBytes);

        MemoryDataSource source = new PluginBuilder().AddRawBytes(headerBytes).BuildSource();
        await using PluginFile plugin = (await PluginFile.OpenAsync(source)).ShouldSucceed();

        ParseError error = (await plugin.GetTopLevelNodesAsync()).ShouldFail();
        await Assert.That(error.Kind).IsEqualTo(ParseErrorKind.StructureOverrun);
    }

    [Test]
    public async Task A_Group_Overrunning_Its_Parent_Is_Invalid()
    {
        // Outer group sized for exactly one empty inner group, but the inner claims more.
        var inner = new GroupHeader(TotalSize: GroupHeader.Size + 50, 0, GroupType.CellChildren, 0, 0, 0);
        byte[] innerBytes = new byte[GroupHeader.Size];
        inner.WriteTo(innerBytes);

        MemoryDataSource source = new PluginBuilder()
            .AddGroup(GroupType.Top, Signature.FromString("CELL").Value, g => g.AddRawBytes(innerBytes))
            .BuildSource();

        await using PluginFile plugin = (await PluginFile.OpenAsync(source)).ShouldSucceed();
        IReadOnlyList<PluginNode> top = (await plugin.GetTopLevelNodesAsync()).ShouldSucceed();

        ParseError error = (await plugin.GetChildrenAsync((GroupNode)top[0])).ShouldFail();
        await Assert.That(error.Kind).IsEqualTo(ParseErrorKind.StructureOverrun);
    }

    [Test]
    public async Task Trailing_Bytes_Too_Short_For_A_Header_Are_Invalid()
    {
        MemoryDataSource source = new PluginBuilder()
            .AddRecord("MISC", 0x10)
            .AddRawBytes(1, 2, 3) // 3 stray bytes at the end of the file
            .BuildSource();

        await using PluginFile plugin = (await PluginFile.OpenAsync(source)).ShouldSucceed();
        ParseError error = (await plugin.GetTopLevelNodesAsync()).ShouldFail();
        await Assert.That(error.Kind).IsEqualTo(ParseErrorKind.UnexpectedEndOfData);
    }

    [Test]
    public async Task ReadBytes_Returns_Raw_Data_For_Hex_Views()
    {
        MemoryDataSource source = new PluginBuilder().BuildSource();
        await using PluginFile plugin = (await PluginFile.OpenAsync(source)).ShouldSucceed();

        byte[] bytes = (await plugin.ReadBytesAsync(0, 4)).ShouldSucceed();
        await Assert.That(bytes).IsEquivalentTo("TES4"u8.ToArray());

        ParseError error = (await plugin.ReadBytesAsync(plugin.Length - 2, 4)).ShouldFail();
        await Assert.That(error.Kind).IsEqualTo(ParseErrorKind.UnexpectedEndOfData);
    }
}
