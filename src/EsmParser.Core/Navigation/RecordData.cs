namespace EsmParser.Core.Navigation;

/// <summary>
/// A record's fully-materialized data: the (decompressed) payload bytes and the
/// fields parsed out of them.
/// </summary>
public sealed class RecordData
{
    internal RecordData(RecordNode record, bool wasCompressed, ReadOnlyMemory<byte> data, IReadOnlyList<RecordField> fields)
    {
        Record = record;
        WasCompressed = wasCompressed;
        Data = data;
        Fields = fields;
    }

    public RecordNode Record { get; }

    /// <summary>Whether the on-disk payload was zlib-compressed.</summary>
    public bool WasCompressed { get; }

    /// <summary>The field bytes (inflated when <see cref="WasCompressed"/>).</summary>
    public ReadOnlyMemory<byte> Data { get; }

    public IReadOnlyList<RecordField> Fields { get; }

    /// <summary>Returns the first field with the given signature, or <see langword="null"/>.</summary>
    public RecordField? FindField(Format.Signature signature)
    {
        foreach (RecordField field in Fields)
        {
            if (field.Signature == signature)
            {
                return field;
            }
        }

        return null;
    }
}
