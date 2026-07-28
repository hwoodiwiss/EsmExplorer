using EsmParser.Core.Format;
using EsmParser.Core.IO;
using EsmParser.Core.Navigation;
using EsmParser.Core.Schema;
using EsmParser.Core.Tests.TestData;

namespace EsmParser.Core.Tests;

public sealed class RecordDecoderTests
{
    private static async Task<IReadOnlyList<DecodedField>> DecodeAsync(
        string recordType,
        Action<RecordFieldsBuilder> fields,
        bool localized = false)
    {
        MemoryDataSource source = new PluginBuilder()
            .AddRecord(recordType, 0x100, fields)
            .BuildSource();

        PluginFile plugin = (await PluginFile.OpenAsync(source)).ShouldSucceed();
        await using (plugin)
        {
            var record = (RecordNode)(await plugin.GetTopLevelNodesAsync()).ShouldSucceed()[0];
            RecordData data = (await plugin.ReadRecordDataAsync(record)).ShouldSucceed();
            return RecordDecoder.Decode(data, localized);
        }
    }

    [Test]
    public async Task Editor_Id_And_Name_Decode_As_Text()
    {
        IReadOnlyList<DecodedField> fields = await DecodeAsync("MISC", f => f
            .AddZString("EDID", "MyItem")
            .AddZString("FULL", "My Item"));

        await Assert.That(fields[0].Name).IsEqualTo("Editor ID");
        await Assert.That(fields[0].Value).IsEqualTo((DecodedValue)new TextValue("MyItem"));
        await Assert.That(fields[1].Name).IsEqualTo("Name");
        await Assert.That(fields[1].Value).IsEqualTo((DecodedValue)new TextValue("My Item"));
        await Assert.That(fields[0].IsDecoded).IsTrue();
    }

    [Test]
    public async Task Localized_Plugins_Show_Translatable_Strings_As_Indices()
    {
        IReadOnlyList<DecodedField> fields = await DecodeAsync(
            "MISC",
            f => f.AddUInt32("FULL", 0x1234),
            localized: true);

        await Assert.That(fields[0].Value).IsEqualTo((DecodedValue)new LocalizedTextValue(0x1234));
    }

    [Test]
    public async Task Object_Bounds_Decode_As_A_Struct()
    {
        IReadOnlyList<DecodedField> fields = await DecodeAsync("MISC", f => f.AddField(
            "OBND",
            0xFF, 0xFF, // X1 = -1
            0x02, 0x00, // Y1 = 2
            0x03, 0x00, // Z1 = 3
            0x04, 0x00, // X2 = 4
            0x05, 0x00, // Y2 = 5
            0x06, 0x00)); // Z2 = 6

        if (fields[0].Value is not StructValue bounds)
        {
            throw new InvalidOperationException($"Expected a struct, got {fields[0].Value}");
        }

        await Assert.That(fields[0].Name).IsEqualTo("Object Bounds");
        await Assert.That(bounds.Members.Count).IsEqualTo(6);
        await Assert.That(bounds.Members[0]).IsEqualTo(new DecodedMember("X1", new IntegerValue(-1)));
        await Assert.That(bounds.Members[5]).IsEqualTo(new DecodedMember("Z2", new IntegerValue(6)));
    }

    [Test]
    public async Task Keywords_Decode_As_A_Reference_List()
    {
        IReadOnlyList<DecodedField> fields = await DecodeAsync("MISC", f => f
            .AddUInt32("KSIZ", 2)
            .AddField("KWDA", 0x01, 0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x01));

        await Assert.That(fields[0].Value).IsEqualTo((DecodedValue)new IntegerValue(2));

        if (fields[1].Value is not ReferenceListValue keywords)
        {
            throw new InvalidOperationException($"Expected a reference list, got {fields[1].Value}");
        }

        await Assert.That(keywords.FormIds.Count).IsEqualTo(2);
        await Assert.That(keywords.FormIds[0]).IsEqualTo(new FormId(1));
        await Assert.That(keywords.FormIds[1]).IsEqualTo(new FormId(0x01000002));
    }

    [Test]
    [Arguments("bAllowRotation", new byte[] { 1, 0, 0, 0 }, "BooleanValue")]
    [Arguments("iMaxCount", new byte[] { 0xFE, 0xFF, 0xFF, 0xFF }, "IntegerValue")]
    [Arguments("fJumpHeight", new byte[] { 0x00, 0x00, 0x80, 0x3F }, "RealValue")]
    [Arguments("sGreeting", new byte[] { (byte)'H', (byte)'i', 0 }, "TextValue")]
    public async Task Game_Setting_Values_Are_Typed_By_Their_Editor_Id(string editorId, byte[] payload, string expectedKind)
    {
        IReadOnlyList<DecodedField> fields = await DecodeAsync("GMST", f => f
            .AddZString("EDID", editorId)
            .AddField("DATA", payload));

        DecodedField value = fields[1];
        await Assert.That(value.Name).IsEqualTo("Value");
        string kind = value.Value switch
        {
            BooleanValue => "BooleanValue",
            IntegerValue => "IntegerValue",
            RealValue => "RealValue",
            TextValue => "TextValue",
            _ => "other",
        };
        await Assert.That(kind).IsEqualTo(expectedKind);
    }

    [Test]
    public async Task Game_Setting_Boolean_And_Float_Values_Decode_Correctly()
    {
        IReadOnlyList<DecodedField> boolFields = await DecodeAsync("GMST", f => f
            .AddZString("EDID", "bEnabled")
            .AddField("DATA", 0, 0, 0, 0));
        await Assert.That(boolFields[1].Value).IsEqualTo((DecodedValue)new BooleanValue(false));

        IReadOnlyList<DecodedField> floatFields = await DecodeAsync("GMST", f => f
            .AddZString("EDID", "fGravity")
            .AddField("DATA", 0x00, 0x00, 0x80, 0x3F)); // 1.0f
        await Assert.That(floatFields[1].Value).IsEqualTo((DecodedValue)new RealValue(1.0f));
    }

    [Test]
    public async Task Placed_Reference_Data_Decodes_Position_And_Rotation()
    {
        byte[] data = new byte[24];
        BitConverter.GetBytes(10.5f).CopyTo(data, 0);
        BitConverter.GetBytes(-3.25f).CopyTo(data, 20);

        IReadOnlyList<DecodedField> fields = await DecodeAsync("REFR", f => f
            .AddUInt32("NAME", 0x000ABC)
            .AddField("DATA", data));

        await Assert.That(fields[0].Name).IsEqualTo("Base Object");
        await Assert.That(fields[0].Value).IsEqualTo((DecodedValue)new ReferenceValue(new FormId(0xABC)));

        if (fields[1].Value is not StructValue position)
        {
            throw new InvalidOperationException($"Expected a struct, got {fields[1].Value}");
        }

        await Assert.That(position.Members[0]).IsEqualTo(new DecodedMember("Position X", new RealValue(10.5f)));
        await Assert.That(position.Members[5]).IsEqualTo(new DecodedMember("Rotation Z", new RealValue(-3.25f)));
    }

    [Test]
    public async Task Global_Variables_Decode_Type_And_Value()
    {
        IReadOnlyList<DecodedField> fields = await DecodeAsync("GLOB", f => f
            .AddField("FNAM", (byte)'f')
            .AddField("FLTV", 0x00, 0x00, 0x20, 0x41)); // 10.0f

        await Assert.That(fields[0].Value).IsEqualTo((DecodedValue)new TextValue("f"));
        await Assert.That(fields[1].Value).IsEqualTo((DecodedValue)new RealValue(10.0f));
    }

    [Test]
    public async Task Unknown_Fields_Fall_Back_To_Raw()
    {
        IReadOnlyList<DecodedField> fields = await DecodeAsync("MISC", f => f
            .AddField("ZZZZ", 1, 2, 3));

        await Assert.That(fields[0].Name).IsEqualTo("ZZZZ");
        await Assert.That(fields[0].Value is RawValue).IsTrue();
        await Assert.That(fields[0].IsDecoded).IsFalse();
    }

    [Test]
    public async Task Shape_Mismatches_Degrade_To_Raw_Instead_Of_Failing()
    {
        IReadOnlyList<DecodedField> fields = await DecodeAsync("MISC", f => f
            .AddField("OBND", 1, 2, 3)); // bounds must be 12 bytes

        if (fields[0].Value is not RawValue raw)
        {
            throw new InvalidOperationException($"Expected raw fallback, got {fields[0].Value}");
        }

        await Assert.That(raw.Note).IsNotNull();
    }

    [Test]
    public async Task The_Plugin_Header_Record_Has_A_Schema()
    {
        MemoryDataSource source = new PluginBuilder()
            .WithAuthor("Someone")
            .WithMaster("Master.esm")
            .BuildSource();

        PluginFile plugin = (await PluginFile.OpenAsync(source)).ShouldSucceed();
        await using (plugin)
        {
            RecordData data = (await plugin.ReadRecordDataAsync(plugin.HeaderRecord)).ShouldSucceed();
            IReadOnlyList<DecodedField> fields = RecordDecoder.Decode(data, plugin.Header.IsLocalized);

            await Assert.That(fields[0].Name).IsEqualTo("Header Statistics");
            await Assert.That(fields[0].Value is StructValue).IsTrue();
            await Assert.That(fields.First(f => f.Name == "Author").Value).IsEqualTo((DecodedValue)new TextValue("Someone"));
            await Assert.That(fields.First(f => f.Name == "Master File").Value).IsEqualTo((DecodedValue)new TextValue("Master.esm"));
        }
    }
}
