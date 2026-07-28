using System.Buffers.Binary;
using System.Collections.Frozen;
using System.Globalization;
using EsmParser.Core.Format;
using EsmParser.Core.Navigation;

namespace EsmParser.Core.Schema;

/// <summary>
/// Decodes record fields into editor-style named values using a conservative schema
/// registry: only well-established, format-stable fields are given names and decoders;
/// everything else degrades to <see cref="RawValue"/> rather than risking a mislabel.
/// Decoding never fails — shape mismatches also degrade to raw.
/// </summary>
public static class RecordDecoder
{
    private delegate DecodedValue FieldDecoder(RecordField field, in DecodeContext context);

    private sealed record FieldSchema(string Name, FieldDecoder Decode);

    private readonly record struct DecodeContext(bool Localized, string? EditorId);

    /// <summary>
    /// Decodes all fields of a record. <paramref name="localizedStrings"/> should be the
    /// owning plugin's localized flag; it controls whether translatable strings are
    /// string-table indices or inline text.
    /// </summary>
    public static IReadOnlyList<DecodedField> Decode(RecordData data, bool localizedStrings)
    {
        ArgumentNullException.ThrowIfNull(data);

        string? editorId = null;
        if (data.FindField(KnownSignatures.Edid) is { } edid && edid.ReadZString().TryGet(out string? id, out _))
        {
            editorId = id;
        }

        var context = new DecodeContext(localizedStrings, editorId);
        Signature recordType = data.Record.Header.Signature;
        FrozenDictionary<Signature, FieldSchema>? perRecord = PerRecordSchemas.GetValueOrDefault(recordType);

        var decoded = new List<DecodedField>(data.Fields.Count);
        foreach (RecordField field in data.Fields)
        {
            FieldSchema? schema = null;
            perRecord?.TryGetValue(field.Signature, out schema);
            schema ??= CommonSchemas.GetValueOrDefault(field.Signature);

            decoded.Add(schema is null
                ? new DecodedField(field.Signature.ToString(), field, new RawValue())
                : new DecodedField(schema.Name, field, schema.Decode(field, context)));
        }

        return decoded;
    }

    // ---- Value decoders (all tolerant: mismatches produce RawValue notes) ----

    private static DecodedValue ZString(RecordField field, in DecodeContext context)
        => field.ReadZString().TryGet(out string? text, out _) ? new TextValue(text) : Unexpected(field);

    private static DecodedValue LString(RecordField field, in DecodeContext context)
    {
        if (context.Localized)
        {
            return field.ReadUInt32().TryGet(out uint index, out _)
                ? new LocalizedTextValue(index)
                : Unexpected(field);
        }

        return ZString(field, context);
    }

    private static DecodedValue UInt32(RecordField field, in DecodeContext context)
        => field.ReadUInt32().TryGet(out uint value, out _) ? new IntegerValue(value) : Unexpected(field);

    private static DecodedValue Int32(RecordField field, in DecodeContext context)
        => field.ReadInt32().TryGet(out int value, out _) ? new IntegerValue(value) : Unexpected(field);

    private static DecodedValue Float32(RecordField field, in DecodeContext context)
        => field.ReadFloat32().TryGet(out float value, out _) ? new RealValue(value) : Unexpected(field);

    private static DecodedValue Reference(RecordField field, in DecodeContext context)
        => field.ReadFormId().TryGet(out FormId formId, out _) ? new ReferenceValue(formId) : Unexpected(field);

    private static DecodedValue ReferenceList(RecordField field, in DecodeContext context)
        => field.ReadFormIdArray().TryGet(out IReadOnlyList<FormId>? ids, out _)
            ? new ReferenceListValue(ids)
            : Unexpected(field);

    /// <summary>OBND: six int16 bounds (min XYZ, max XYZ).</summary>
    private static DecodedValue ObjectBounds(RecordField field, in DecodeContext context)
    {
        if (field.Size != 12)
        {
            return Unexpected(field);
        }

        ReadOnlySpan<byte> span = field.Data.Span;
        string[] names = ["X1", "Y1", "Z1", "X2", "Y2", "Z2"];
        var members = new DecodedMember[6];
        for (int i = 0; i < 6; i++)
        {
            members[i] = new DecodedMember(names[i], new IntegerValue(BinaryPrimitives.ReadInt16LittleEndian(span[(i * 2)..])));
        }

        return new StructValue(members);
    }

    /// <summary>Placed-reference DATA: position XYZ and rotation XYZ as floats.</summary>
    private static DecodedValue PositionRotation(RecordField field, in DecodeContext context)
    {
        if (field.Size != 24)
        {
            return Unexpected(field);
        }

        ReadOnlySpan<byte> span = field.Data.Span;
        string[] names = ["Position X", "Position Y", "Position Z", "Rotation X", "Rotation Y", "Rotation Z"];
        var members = new DecodedMember[6];
        for (int i = 0; i < 6; i++)
        {
            members[i] = new DecodedMember(names[i], new RealValue(BinaryPrimitives.ReadSingleLittleEndian(span[(i * 4)..])));
        }

        return new StructValue(members);
    }

    /// <summary>CELL XCLC: grid X/Y (tolerating the optional trailing flag dword).</summary>
    private static DecodedValue CellGrid(RecordField field, in DecodeContext context)
    {
        if (field.Size is not (8 or 12))
        {
            return Unexpected(field);
        }

        ReadOnlySpan<byte> span = field.Data.Span;
        var members = new List<DecodedMember>
        {
            new("Grid X", new IntegerValue(BinaryPrimitives.ReadInt32LittleEndian(span))),
            new("Grid Y", new IntegerValue(BinaryPrimitives.ReadInt32LittleEndian(span[4..]))),
        };
        if (field.Size == 12)
        {
            members.Add(new DecodedMember("Flags", new IntegerValue(BinaryPrimitives.ReadUInt32LittleEndian(span[8..]), Hexadecimal: true)));
        }

        return new StructValue(members);
    }

    /// <summary>GMST DATA: the value type is encoded in the editor id's first character.</summary>
    private static DecodedValue GameSettingValue(RecordField field, in DecodeContext context)
    {
        switch (context.EditorId is { Length: > 0 } id ? char.ToLowerInvariant(id[0]) : '\0')
        {
            case 'b':
                return field.ReadUInt32().TryGet(out uint boolean, out _)
                    ? new BooleanValue(boolean != 0)
                    : Unexpected(field);
            case 'i':
                return Int32(field, context);
            case 'u':
                return UInt32(field, context);
            case 'f':
                return Float32(field, context);
            case 's':
                return LString(field, context);
            default:
                return new RawValue("Value type is unknown without an EDID prefix.");
        }
    }

    /// <summary>GLOB FNAM: a single type character.</summary>
    private static DecodedValue GlobalType(RecordField field, in DecodeContext context)
        => field.Size == 1 && field.Data.Span[0] is >= 0x20 and <= 0x7E
            ? new TextValue(((char)field.Data.Span[0]).ToString())
            : Unexpected(field);

    /// <summary>TES4 HEDR: version, record count, next object id.</summary>
    private static DecodedValue PluginHeaderStats(RecordField field, in DecodeContext context)
    {
        if (field.Size < 12)
        {
            return Unexpected(field);
        }

        ReadOnlySpan<byte> span = field.Data.Span;
        return new StructValue(
        [
            new DecodedMember("Version", new RealValue(BinaryPrimitives.ReadSingleLittleEndian(span))),
            new DecodedMember("Records", new IntegerValue(BinaryPrimitives.ReadUInt32LittleEndian(span[4..]))),
            new DecodedMember("Next Object ID", new IntegerValue(BinaryPrimitives.ReadUInt32LittleEndian(span[8..]), Hexadecimal: true)),
        ]);
    }

    private static RawValue Unexpected(RecordField field) => new(string.Create(
        CultureInfo.InvariantCulture,
        $"Unexpected size {field.Size} for this field's schema."));

    // ---- Schema registry ----

    /// <summary>Fields whose meaning is stable across (almost) all record types.</summary>
    private static readonly FrozenDictionary<Signature, FieldSchema> CommonSchemas = Build(new Dictionary<string, FieldSchema>(StringComparer.Ordinal)
    {
        ["EDID"] = new("Editor ID", ZString),
        ["FULL"] = new("Name", LString),
        ["DESC"] = new("Description", LString),
        ["OBND"] = new("Object Bounds", ObjectBounds),
        ["MODL"] = new("Model File Name", ZString),
        ["KSIZ"] = new("Keyword Count", UInt32),
        ["KWDA"] = new("Keywords", ReferenceList),
        ["NAME"] = new("Base Object", Reference),
        ["XSCL"] = new("Scale", Float32),
    });

    /// <summary>Record-type-specific fields; these take precedence over the common set.</summary>
    private static readonly FrozenDictionary<Signature, FrozenDictionary<Signature, FieldSchema>> PerRecordSchemas =
        new Dictionary<string, Dictionary<string, FieldSchema>>(StringComparer.Ordinal)
        {
            ["GMST"] = new(StringComparer.Ordinal)
            {
                ["DATA"] = new("Value", GameSettingValue),
            },
            ["GLOB"] = new(StringComparer.Ordinal)
            {
                ["FNAM"] = new("Type", GlobalType),
                ["FLTV"] = new("Value", Float32),
            },
            ["CELL"] = new(StringComparer.Ordinal)
            {
                ["XCLC"] = new("Grid", CellGrid),
            },
            ["REFR"] = new(StringComparer.Ordinal)
            {
                ["DATA"] = new("Position / Rotation", PositionRotation),
            },
            ["ACHR"] = new(StringComparer.Ordinal)
            {
                ["DATA"] = new("Position / Rotation", PositionRotation),
            },
            ["PGRE"] = new(StringComparer.Ordinal)
            {
                ["DATA"] = new("Position / Rotation", PositionRotation),
            },
            ["PHZD"] = new(StringComparer.Ordinal)
            {
                ["DATA"] = new("Position / Rotation", PositionRotation),
            },
            ["FLST"] = new(StringComparer.Ordinal)
            {
                ["LNAM"] = new("Object", Reference),
            },
            ["NPC_"] = new(StringComparer.Ordinal)
            {
                ["RNAM"] = new("Race", Reference),
            },
            ["TES4"] = new(StringComparer.Ordinal)
            {
                ["HEDR"] = new("Header Statistics", PluginHeaderStats),
                ["CNAM"] = new("Author", ZString),
                ["SNAM"] = new("Description", ZString),
                ["MAST"] = new("Master File", ZString),
                ["ONAM"] = new("Overridden Forms", ReferenceList),
                ["INTV"] = new("INTV", Int32),
                ["INCC"] = new("Interior Cell Count", Int32),
            },
        }.ToFrozenDictionary(
            static pair => Signature.FromString(pair.Key),
            static pair => Build(pair.Value));

    private static FrozenDictionary<Signature, FieldSchema> Build(IDictionary<string, FieldSchema> source)
        => source.ToFrozenDictionary(static pair => Signature.FromString(pair.Key), static pair => pair.Value);
}
