using EsmParser.Core.Format;

namespace EsmParser.Core.Tests;

public sealed class SignatureTests
{
    [Test]
    public async Task FromString_RoundTrips_Through_ToString()
    {
        var signature = Signature.FromString("GRUP");
        await Assert.That(signature.ToString()).IsEqualTo("GRUP");
    }

    [Test]
    public async Task Read_Interprets_Bytes_As_LittleEndian_FourCC()
    {
        byte[] bytes = [(byte)'T', (byte)'E', (byte)'S', (byte)'4'];
        var signature = Signature.Read(bytes);
        await Assert.That(signature).IsEqualTo(KnownSignatures.Tes4);
        await Assert.That(signature.Value).IsEqualTo(0x34534554u);
    }

    [Test]
    public async Task WriteTo_Produces_Original_Bytes()
    {
        byte[] destination = new byte[4];
        KnownSignatures.Grup.WriteTo(destination);
        await Assert.That(destination).IsEquivalentTo(new byte[] { (byte)'G', (byte)'R', (byte)'U', (byte)'P' });
    }

    [Test]
    public async Task ToString_Falls_Back_To_Hex_For_NonPrintable_Bytes()
    {
        var signature = new Signature(0x00010203);
        await Assert.That(signature.IsPrintable).IsFalse();
        await Assert.That(signature.ToString()).IsEqualTo("0x00010203");
    }

    [Test]
    public async Task Equality_Compares_By_Value()
    {
        await Assert.That(Signature.FromString("EDID")).IsEqualTo(KnownSignatures.Edid);
        await Assert.That(Signature.FromString("EDID")).IsNotEqualTo(KnownSignatures.Full);
    }

    [Test]
    [Arguments("ABC")]
    [Arguments("ABCDE")]
    [Arguments("")]
    public void FromString_Rejects_Wrong_Lengths(string text)
        => Assert.Throws<ArgumentException>(() => Signature.FromString(text));

    [Test]
    public void FromString_Rejects_NonAscii()
        => Assert.Throws<ArgumentException>(() => Signature.FromString("GRÜP"));
}

public sealed class FormIdTests
{
    [Test]
    public async Task Decomposes_Master_Index_And_Object_Id()
    {
        var formId = new FormId(0x0301B2C3);
        await Assert.That(formId.MasterIndex).IsEqualTo((byte)0x03);
        await Assert.That(formId.ObjectId).IsEqualTo(0x01B2C3u);
    }

    [Test]
    public async Task ToString_Uses_Eight_Hex_Digits()
    {
        await Assert.That(new FormId(0x4F2B1).ToString()).IsEqualTo("0004F2B1");
        await Assert.That(new FormId(0xFE00_0001).ToString()).IsEqualTo("FE000001");
    }

    [Test]
    public async Task IsNull_Only_For_Zero()
    {
        await Assert.That(new FormId(0).IsNull).IsTrue();
        await Assert.That(new FormId(1).IsNull).IsFalse();
    }
}

public sealed class RecordFlagsTests
{
    [Test]
    public async Task GetFlagNames_Names_Known_Bits()
    {
        var flags = RecordFlags.Master | RecordFlags.Localized | RecordFlags.Compressed;
        IReadOnlyList<string> names = flags.GetFlagNames();
        await Assert.That(names).Contains("Master");
        await Assert.That(names).Contains("Localized");
        await Assert.That(names).Contains("Compressed");
        await Assert.That(names.Count).IsEqualTo(3);
    }

    [Test]
    public async Task GetFlagNames_Renders_Unknown_Bits_As_Hex()
    {
        var flags = (RecordFlags)0x0000_4000;
        IReadOnlyList<string> names = flags.GetFlagNames();
        await Assert.That(names.Count).IsEqualTo(1);
        await Assert.That(names[0]).IsEqualTo("0x00004000");
    }

    [Test]
    public async Task GetFlagNames_Is_Empty_For_None()
    {
        await Assert.That(RecordFlags.None.GetFlagNames().Count).IsEqualTo(0);
    }

    [Test]
    public async Task Starfield_Plugin_Scale_Bits_Have_Starfield_Values()
    {
        // Starfield moved the light-master bit relative to Skyrim SE; pin the exact values.
        RecordFlags light = RecordFlags.LightMaster;
        RecordFlags overlay = RecordFlags.Overlay;
        RecordFlags medium = RecordFlags.MediumMaster;
        RecordFlags compressed = RecordFlags.Compressed;
        await Assert.That((uint)light).IsEqualTo(0x100u);
        await Assert.That((uint)overlay).IsEqualTo(0x200u);
        await Assert.That((uint)medium).IsEqualTo(0x400u);
        await Assert.That((uint)compressed).IsEqualTo(0x40000u);
    }
}
