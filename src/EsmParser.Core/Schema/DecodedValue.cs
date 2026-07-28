using EsmParser.Core.Format;
using EsmParser.Core.Navigation;

namespace EsmParser.Core.Schema;

/// <summary>Plain text (editor ids, file paths, unlocalized strings).</summary>
public sealed record TextValue(string Text);

/// <summary>An index into the plugin's .STRINGS localization files (which are not part of the plugin).</summary>
public sealed record LocalizedTextValue(uint Index);

public sealed record BooleanValue(bool Value);

public sealed record IntegerValue(long Value, bool Hexadecimal = false);

public sealed record RealValue(double Value);

/// <summary>A reference to another form, followable across the workspace.</summary>
public sealed record ReferenceValue(FormId FormId);

/// <summary>A packed list of form references (e.g. keyword arrays).</summary>
public sealed record ReferenceListValue(IReadOnlyList<FormId> FormIds);

public sealed record DecodedMember(string Name, DecodedValue Value);

/// <summary>A fixed-layout struct with named members (bounds, positions, grids…).</summary>
public sealed record StructValue(IReadOnlyList<DecodedMember> Members);

/// <summary>No schema (or the payload did not match the expected shape); display the raw bytes.</summary>
public sealed record RawValue(string? Note = null);

/// <summary>
/// A field payload decoded per the record schema, in the shape an editor would present it.
/// </summary>
public union DecodedValue(
    TextValue,
    LocalizedTextValue,
    BooleanValue,
    IntegerValue,
    RealValue,
    ReferenceValue,
    ReferenceListValue,
    StructValue,
    RawValue);

/// <summary>A record field paired with its schema name and decoded value.</summary>
public sealed record DecodedField(string Name, RecordField Source, DecodedValue Value)
{
    public bool IsDecoded => Value is not RawValue;
}
