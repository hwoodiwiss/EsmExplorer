using EsmParser.Core.Format;

namespace EsmParser.Core.Tests;

public sealed class RecordHeaderTests
{
    /// <summary>The actual first 24 bytes of Starfield.esm, captured from a hex dump.</summary>
    private static readonly byte[] StarfieldTes4Header =
    [
        0x54, 0x45, 0x53, 0x34, // "TES4"
        0x6B, 0x7B, 0x00, 0x00, // data size 31595
        0x81, 0x00, 0x00, 0x00, // flags: Master | Localized
        0x00, 0x00, 0x00, 0x00, // form id 0
        0xC1, 0x34,             // timestamp
        0x20, 0x00,             // version control info
        0x45, 0x02,             // form version 581
        0x00, 0x00,             // unknown
    ];

    [Test]
    public async Task Read_Parses_The_Real_Starfield_Tes4_Header()
    {
        var header = RecordHeader.Read(StarfieldTes4Header);
        await Assert.That(header.Signature).IsEqualTo(KnownSignatures.Tes4);
        await Assert.That(header.DataSize).IsEqualTo(31595u);
        await Assert.That(header.Flags).IsEqualTo(RecordFlags.Master | RecordFlags.Localized);
        await Assert.That(header.FormId).IsEqualTo(new FormId(0));
        await Assert.That(header.Timestamp).IsEqualTo((ushort)0x34C1);
        await Assert.That(header.VersionControlInfo).IsEqualTo((ushort)0x0020);
        await Assert.That(header.FormVersion).IsEqualTo((ushort)581);
        await Assert.That(header.Unknown).IsEqualTo((ushort)0);
        await Assert.That(header.IsCompressed).IsFalse();
        await Assert.That(header.IsDeleted).IsFalse();
    }

    [Test]
    public async Task WriteTo_Round_Trips()
    {
        var header = RecordHeader.Read(StarfieldTes4Header);
        byte[] written = new byte[RecordHeader.Size];
        header.WriteTo(written);
        await Assert.That(written).IsEquivalentTo(StarfieldTes4Header);
    }

    [Test]
    public async Task Timestamp_Decodes_The_BitPacked_Date()
    {
        // 0b0010101_0001_11001 = 25 Jan 2021, the worked example from the spec.
        var header = new RecordHeader(KnownSignatures.Tes4, 0, RecordFlags.None, new FormId(0), 0x2A39, 0, 581, 0);
        await Assert.That(header.TimestampAsDate).IsEqualTo(new DateOnly(2021, 1, 25));
    }

    [Test]
    public async Task Timestamp_Zero_Has_No_Date()
    {
        var header = new RecordHeader(KnownSignatures.Tes4, 0, RecordFlags.None, new FormId(0), 0, 0, 581, 0);
        await Assert.That(header.TimestampAsDate).IsNull();
    }

    [Test]
    public async Task Timestamp_With_Invalid_Month_Or_Day_Has_No_Date()
    {
        // month 0
        var monthZero = new RecordHeader(KnownSignatures.Tes4, 0, RecordFlags.None, new FormId(0), 0b0000001_0000_00001, 0, 581, 0);
        await Assert.That(monthZero.TimestampAsDate).IsNull();

        // 31 February
        var badDay = new RecordHeader(KnownSignatures.Tes4, 0, RecordFlags.None, new FormId(0), 0b0000001_0010_11111, 0, 581, 0);
        await Assert.That(badDay.TimestampAsDate).IsNull();
    }

    [Test]
    public async Task Compressed_Flag_Is_Reflected()
    {
        var header = new RecordHeader(KnownSignatures.Edid, 0, RecordFlags.Compressed, new FormId(1), 0, 0, 581, 0);
        await Assert.That(header.IsCompressed).IsTrue();
    }
}

public sealed class GroupHeaderTests
{
    [Test]
    public async Task Read_And_WriteTo_Round_Trip()
    {
        var header = new GroupHeader(0x1234, 0x00ABCDEF, GroupType.CellChildren, 0x2A39, 7, 42);
        byte[] bytes = new byte[GroupHeader.Size];
        header.WriteTo(bytes);

        await Assert.That(Signature.Read(bytes)).IsEqualTo(KnownSignatures.Grup);
        var read = GroupHeader.Read(bytes);
        await Assert.That(read).IsEqualTo(header);
    }

    [Test]
    public async Task ContentSize_Excludes_The_Header()
    {
        var header = new GroupHeader(24 + 100, 0, GroupType.Top, 0, 0, 0);
        await Assert.That(header.ContentSize).IsEqualTo(100L);
    }

    [Test]
    public async Task Top_Group_Label_Is_A_Signature()
    {
        var header = new GroupHeader(24, KnownSignatures.Tes4.Value, GroupType.Top, 0, 0, 0);
        await Assert.That(header.LabelAsSignature).IsEqualTo(KnownSignatures.Tes4);
        await Assert.That(header.GetLabelDescription()).IsEqualTo("TES4");
    }

    [Test]
    public async Task Block_Label_Is_A_Signed_Number()
    {
        var header = new GroupHeader(24, unchecked((uint)-3), GroupType.InteriorCellBlock, 0, 0, 0);
        await Assert.That(header.LabelAsBlockNumber).IsEqualTo(-3);
        await Assert.That(header.GetLabelDescription()).IsEqualTo("Block -3");
    }

    [Test]
    public async Task Grid_Label_Stores_Y_First()
    {
        // Y = -2 (0xFFFE) in the low word, X = 5 in the high word.
        uint rawLabel = (5u << 16) | 0xFFFE;
        var header = new GroupHeader(24, rawLabel, GroupType.ExteriorCellBlock, 0, 0, 0);
        await Assert.That(header.LabelAsGridCell).IsEqualTo(((short)-2, (short)5));
        await Assert.That(header.GetLabelDescription()).IsEqualTo("Block (5, -2)");
    }

    [Test]
    [Arguments(GroupType.WorldChildren, "World 0100002A")]
    [Arguments(GroupType.CellChildren, "Cell 0100002A")]
    [Arguments(GroupType.TopicChildren, "Topic 0100002A")]
    [Arguments(GroupType.CellPersistentChildren, "Cell 0100002A (Persistent)")]
    [Arguments(GroupType.CellTemporaryChildren, "Cell 0100002A (Temporary)")]
    [Arguments(GroupType.QuestChildren, "Quest 0100002A")]
    public async Task FormId_Labelled_Groups_Describe_Their_Parent(GroupType type, string expected)
    {
        var header = new GroupHeader(24, 0x0100002A, type, 0, 0, 0);
        await Assert.That(header.LabelAsFormId).IsEqualTo(new FormId(0x0100002A));
        await Assert.That(header.GetLabelDescription()).IsEqualTo(expected);
    }

    [Test]
    public async Task Unknown_Group_Types_Describe_The_Raw_Label()
    {
        var header = new GroupHeader(24, 0xDEADBEEF, (GroupType)99, 0, 0, 0);
        await Assert.That(header.GetLabelDescription()).IsEqualTo("0xDEADBEEF");
    }
}
