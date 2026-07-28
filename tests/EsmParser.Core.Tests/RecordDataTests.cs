using System.Buffers.Binary;
using EsmParser.Core.Format;
using EsmParser.Core.IO;
using EsmParser.Core.Navigation;
using EsmParser.Core.Tests.TestData;

namespace EsmParser.Core.Tests;

public sealed class RecordDataTests
{
    [Test]
    public async Task Uncompressed_Fields_Carry_Absolute_File_Offsets()
    {
        MemoryDataSource source = new PluginBuilder()
            .AddRecord("WEAP", 0x100, f => f
                .AddZString("EDID", "TestWeapon")
                .AddUInt32("DATA", 1234))
            .BuildSource();

        await using PluginFile plugin = (await PluginFile.OpenAsync(source)).ShouldSucceed();
        var record = (RecordNode)(await plugin.GetTopLevelNodesAsync()).ShouldSucceed()[0];
        RecordData data = (await plugin.ReadRecordDataAsync(record)).ShouldSucceed();

        await Assert.That(data.WasCompressed).IsFalse();
        await Assert.That(data.Fields.Count).IsEqualTo(2);

        RecordField edid = data.Fields[0];
        await Assert.That(edid.Signature).IsEqualTo(KnownSignatures.Edid);
        await Assert.That(edid.Size).IsEqualTo(11); // "TestWeapon" + NUL
        await Assert.That(edid.DataOffset).IsEqualTo(6);
        await Assert.That(edid.AbsoluteDataOffset).IsEqualTo(record.DataOffset + 6);
        await Assert.That(edid.ReadZString().ShouldSucceed()).IsEqualTo("TestWeapon");

        RecordField dataField = data.Fields[1];
        await Assert.That(dataField.AbsoluteDataOffset).IsEqualTo(record.DataOffset + 6 + 11 + 6);
        await Assert.That(dataField.ReadUInt32().ShouldSucceed()).IsEqualTo(1234u);

        // The raw bytes at the reported absolute offset really are the field payload.
        byte[] raw = (await plugin.ReadBytesAsync(edid.AbsoluteDataOffset!.Value, edid.Size)).ShouldSucceed();
        await Assert.That(raw).IsEquivalentTo(edid.Data.ToArray());
    }

    [Test]
    public async Task Zero_Size_Fields_Parse()
    {
        MemoryDataSource source = new PluginBuilder()
            .AddRecord("GBFM", 0x100, f => f
                .AddField("BFCB", PluginBuilder.ZString("Component"))
                .AddField("BFCE"))
            .BuildSource();

        await using PluginFile plugin = (await PluginFile.OpenAsync(source)).ShouldSucceed();
        var record = (RecordNode)(await plugin.GetTopLevelNodesAsync()).ShouldSucceed()[0];
        RecordData data = (await plugin.ReadRecordDataAsync(record)).ShouldSucceed();

        await Assert.That(data.Fields.Count).IsEqualTo(2);
        await Assert.That(data.Fields[1].Size).IsEqualTo(0);
        await Assert.That(data.Fields[1].Data.Length).IsEqualTo(0);
    }

    [Test]
    public async Task Compressed_Records_Inflate_And_Parse()
    {
        MemoryDataSource source = new PluginBuilder()
            .AddRecord(
                "NPC_",
                0x1337,
                f => f
                    .AddZString("EDID", "CompressedNpc")
                    .AddUInt32("ACBS", 99),
                RecordFlags.Compressed)
            .BuildSource();

        await using PluginFile plugin = (await PluginFile.OpenAsync(source)).ShouldSucceed();
        var record = (RecordNode)(await plugin.GetTopLevelNodesAsync()).ShouldSucceed()[0];
        await Assert.That(record.IsCompressed).IsTrue();

        RecordData data = (await plugin.ReadRecordDataAsync(record)).ShouldSucceed();
        await Assert.That(data.WasCompressed).IsTrue();
        await Assert.That(data.Fields.Count).IsEqualTo(2);
        await Assert.That(data.Fields[0].ReadZString().ShouldSucceed()).IsEqualTo("CompressedNpc");
        await Assert.That(data.Fields[1].ReadUInt32().ShouldSucceed()).IsEqualTo(99u);

        // Fields inside compressed records have no absolute file position.
        await Assert.That(data.Fields[0].AbsoluteDataOffset).IsNull();
        await Assert.That(data.Fields[0].DataOffset).IsEqualTo(6);
    }

    [Test]
    public async Task Xxxx_Extended_Sizes_Are_Applied_To_The_Following_Field()
    {
        byte[] bigPayload = new byte[70_000]; // exceeds ushort.MaxValue
        Random.Shared.NextBytes(bigPayload);

        MemoryDataSource source = new PluginBuilder()
            .AddRecord("GBFM", 0x200, f => f
                .AddZString("EDID", "BigForm")
                .AddFieldWithXxxx("NVNM", bigPayload)
                .AddUInt32("DATA", 5))
            .BuildSource();

        await using PluginFile plugin = (await PluginFile.OpenAsync(source)).ShouldSucceed();
        var record = (RecordNode)(await plugin.GetTopLevelNodesAsync()).ShouldSucceed()[0];
        RecordData data = (await plugin.ReadRecordDataAsync(record)).ShouldSucceed();

        // XXXX is merged into the following field rather than surfaced separately.
        await Assert.That(data.Fields.Count).IsEqualTo(3);

        RecordField nvnm = data.Fields[1];
        await Assert.That(nvnm.Signature).IsEqualTo(Signature.FromString("NVNM"));
        await Assert.That(nvnm.Size).IsEqualTo(bigPayload.Length);
        await Assert.That(nvnm.HasExtendedSize).IsTrue();
        await Assert.That(nvnm.ExtendedSizeHeaderOffset).IsEqualTo(6 + 8); // after EDID field
        await Assert.That(nvnm.Data.ToArray()).IsEquivalentTo(bigPayload);

        // Parsing resumes correctly after the large field.
        await Assert.That(data.Fields[2].ReadUInt32().ShouldSucceed()).IsEqualTo(5u);
        await Assert.That(data.Fields[0].HasExtendedSize).IsFalse();
    }

    [Test]
    public async Task Xxxx_With_A_Size_Other_Than_Four_Is_Invalid()
    {
        MemoryDataSource source = new PluginBuilder()
            .AddRecord("MISC", 0x300, f => f
                .AddField("XXXX", 1, 2) // declared size 2: unsupported
                .AddUInt32("DATA", 5))
            .BuildSource();

        await using PluginFile plugin = (await PluginFile.OpenAsync(source)).ShouldSucceed();
        var record = (RecordNode)(await plugin.GetTopLevelNodesAsync()).ShouldSucceed()[0];

        ParseError error = (await plugin.ReadRecordDataAsync(record)).ShouldFail();
        await Assert.That(error.Kind).IsEqualTo(ParseErrorKind.ExtendedSizeInvalid);
    }

    [Test]
    public async Task Xxxx_Not_Followed_By_A_Field_Is_Invalid()
    {
        MemoryDataSource source = new PluginBuilder()
            .AddRecord("MISC", 0x300, f => f
                .AddField("XXXX", 16, 0, 0, 0)) // valid XXXX, then nothing
            .BuildSource();

        await using PluginFile plugin = (await PluginFile.OpenAsync(source)).ShouldSucceed();
        var record = (RecordNode)(await plugin.GetTopLevelNodesAsync()).ShouldSucceed()[0];

        ParseError error = (await plugin.ReadRecordDataAsync(record)).ShouldFail();
        await Assert.That(error.Kind).IsEqualTo(ParseErrorKind.ExtendedSizeInvalid);
    }

    [Test]
    public async Task A_Field_Overrunning_The_Record_Is_Invalid()
    {
        // Field claims 100 bytes but the record data ends after its header.
        byte[] rawData = new byte[6];
        Signature.FromString("EDID").WriteTo(rawData);
        BinaryPrimitives.WriteUInt16LittleEndian(rawData.AsSpan(4), 100);

        MemoryDataSource source = new PluginBuilder()
            .AddRecordWithRawData("MISC", 0x400, rawData)
            .BuildSource();

        await using PluginFile plugin = (await PluginFile.OpenAsync(source)).ShouldSucceed();
        var record = (RecordNode)(await plugin.GetTopLevelNodesAsync()).ShouldSucceed()[0];

        ParseError error = (await plugin.ReadRecordDataAsync(record)).ShouldFail();
        await Assert.That(error.Kind).IsEqualTo(ParseErrorKind.FieldInvalid);
    }

    [Test]
    public async Task A_Truncated_Field_Header_Is_Invalid()
    {
        MemoryDataSource source = new PluginBuilder()
            .AddRecordWithRawData("MISC", 0x500, [1, 2, 3]) // 3 bytes cannot hold a field header
            .BuildSource();

        await using PluginFile plugin = (await PluginFile.OpenAsync(source)).ShouldSucceed();
        var record = (RecordNode)(await plugin.GetTopLevelNodesAsync()).ShouldSucceed()[0];

        ParseError error = (await plugin.ReadRecordDataAsync(record)).ShouldFail();
        await Assert.That(error.Kind).IsEqualTo(ParseErrorKind.FieldInvalid);
    }

    [Test]
    public async Task Garbage_In_A_Compressed_Record_Is_Invalid()
    {
        byte[] rawData = new byte[20];
        BinaryPrimitives.WriteUInt32LittleEndian(rawData, 100); // declared inflated size
        rawData.AsSpan(4).Fill(0xAA); // not zlib

        MemoryDataSource source = new PluginBuilder()
            .AddRecordWithRawData("NPC_", 0x600, rawData, RecordFlags.Compressed)
            .BuildSource();

        await using PluginFile plugin = (await PluginFile.OpenAsync(source)).ShouldSucceed();
        var record = (RecordNode)(await plugin.GetTopLevelNodesAsync()).ShouldSucceed()[0];

        ParseError error = (await plugin.ReadRecordDataAsync(record)).ShouldFail();
        await Assert.That(error.Kind).IsEqualTo(ParseErrorKind.DecompressionFailed);
    }

    [Test]
    public async Task A_Wrong_Declared_Decompressed_Size_Is_Invalid()
    {
        // Build a valid compressed record, then tamper with the declared size.
        byte[] bytes = new PluginBuilder()
            .AddRecord("NPC_", 0x700, f => f.AddZString("EDID", "Npc"), RecordFlags.Compressed)
            .Build();

        // The declared size is the first 4 bytes of the record data (after TES4 and the record header).
        int tes4End = 24 + BitConverter.ToInt32(bytes, 4);
        int declaredSizeOffset = tes4End + 24;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(declaredSizeOffset), 9999);

        await using var source = new MemoryDataSource(bytes, "tampered.esm");
        await using PluginFile plugin = (await PluginFile.OpenAsync(source, leaveOpen: true)).ShouldSucceed();
        var record = (RecordNode)(await plugin.GetTopLevelNodesAsync()).ShouldSucceed()[0];

        ParseError error = (await plugin.ReadRecordDataAsync(record)).ShouldFail();
        await Assert.That(error.Kind).IsEqualTo(ParseErrorKind.DecompressionFailed);
    }

    [Test]
    public async Task A_Compressed_Record_Too_Small_For_Its_Size_Prefix_Is_Invalid()
    {
        MemoryDataSource source = new PluginBuilder()
            .AddRecordWithRawData("NPC_", 0x800, [1, 2], RecordFlags.Compressed)
            .BuildSource();

        await using PluginFile plugin = (await PluginFile.OpenAsync(source)).ShouldSucceed();
        var record = (RecordNode)(await plugin.GetTopLevelNodesAsync()).ShouldSucceed()[0];

        ParseError error = (await plugin.ReadRecordDataAsync(record)).ShouldFail();
        await Assert.That(error.Kind).IsEqualTo(ParseErrorKind.DecompressionFailed);
    }

    [Test]
    public async Task FindField_Locates_Fields_By_Signature()
    {
        MemoryDataSource source = new PluginBuilder()
            .AddRecord("WEAP", 0x100, f => f
                .AddZString("EDID", "Weapon")
                .AddZString("FULL", "A Weapon"))
            .BuildSource();

        await using PluginFile plugin = (await PluginFile.OpenAsync(source)).ShouldSucceed();
        var record = (RecordNode)(await plugin.GetTopLevelNodesAsync()).ShouldSucceed()[0];
        RecordData data = (await plugin.ReadRecordDataAsync(record)).ShouldSucceed();

        await Assert.That(data.FindField(KnownSignatures.Full)!.ReadZString().ShouldSucceed()).IsEqualTo("A Weapon");
        await Assert.That(data.FindField(KnownSignatures.Mast)).IsNull();
    }
}
