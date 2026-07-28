using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using EsmParser.Core.Format;

namespace EsmParser.Core.Navigation;

/// <summary>
/// One field (subrecord) within a record's data, including exactly where its
/// payload lives.
/// </summary>
/// <remarks>
/// For uncompressed records <see cref="AbsoluteDataOffset"/> gives the payload's
/// position in the file; for compressed records the field only exists in the
/// inflated buffer, so only the record-relative <see cref="DataOffset"/> applies.
/// </remarks>
public sealed class RecordField
{
    internal RecordField(
        Signature signature,
        int dataOffset,
        long? absoluteDataOffset,
        int? extendedSizeHeaderOffset,
        ReadOnlyMemory<byte> data)
    {
        Signature = signature;
        DataOffset = dataOffset;
        AbsoluteDataOffset = absoluteDataOffset;
        ExtendedSizeHeaderOffset = extendedSizeHeaderOffset;
        Data = data;
    }

    /// <summary>Size of a field header on disk (4CC + 16-bit size), in bytes.</summary>
    public const int HeaderSize = 6;

    public Signature Signature { get; }

    /// <summary>The field's payload bytes.</summary>
    public ReadOnlyMemory<byte> Data { get; }

    /// <summary>Payload size in bytes (from the XXXX override when present).</summary>
    public int Size => Data.Length;

    /// <summary>Offset of the payload within the record's (decompressed) data.</summary>
    public int DataOffset { get; }

    /// <summary>Absolute file offset of the payload; <see langword="null"/> for fields inside compressed records.</summary>
    public long? AbsoluteDataOffset { get; }

    /// <summary>
    /// When the field's size came from a preceding XXXX field, the record-relative offset
    /// of that XXXX field's header; otherwise <see langword="null"/>.
    /// </summary>
    public int? ExtendedSizeHeaderOffset { get; }

    public bool HasExtendedSize => ExtendedSizeHeaderOffset is not null;

    /// <summary>Decodes the payload as a NUL-terminated UTF-8 string (the terminator is optional on disk).</summary>
    public ParseResult<string> ReadZString()
    {
        ReadOnlySpan<byte> span = Data.Span;
        if (span.Length > 0 && span[^1] == 0)
        {
            span = span[..^1];
        }

        return new Success<string>(Encoding.UTF8.GetString(span));
    }

    public ParseResult<ushort> ReadUInt16()
        => Data.Length == 2
            ? new Success<ushort>(BinaryPrimitives.ReadUInt16LittleEndian(Data.Span))
            : SizeMismatch("a 16-bit integer", 2);

    public ParseResult<uint> ReadUInt32()
        => Data.Length == 4
            ? new Success<uint>(BinaryPrimitives.ReadUInt32LittleEndian(Data.Span))
            : SizeMismatch("a 32-bit integer", 4);

    public ParseResult<int> ReadInt32()
        => Data.Length == 4
            ? new Success<int>(BinaryPrimitives.ReadInt32LittleEndian(Data.Span))
            : SizeMismatch("a 32-bit integer", 4);

    public ParseResult<ulong> ReadUInt64()
        => Data.Length == 8
            ? new Success<ulong>(BinaryPrimitives.ReadUInt64LittleEndian(Data.Span))
            : SizeMismatch("a 64-bit integer", 8);

    public ParseResult<float> ReadFloat32()
        => Data.Length == 4
            ? new Success<float>(BinaryPrimitives.ReadSingleLittleEndian(Data.Span))
            : SizeMismatch("a 32-bit float", 4);

    public ParseResult<FormId> ReadFormId()
        => Data.Length == 4
            ? new Success<FormId>(new FormId(BinaryPrimitives.ReadUInt32LittleEndian(Data.Span)))
            : SizeMismatch("a form id", 4);

    /// <summary>Decodes the payload as a tightly-packed array of form ids.</summary>
    public ParseResult<IReadOnlyList<FormId>> ReadFormIdArray()
    {
        if (Data.Length % 4 != 0)
        {
            return SizeMismatchError("an array of form ids", "a multiple of 4");
        }

        ReadOnlySpan<byte> span = Data.Span;
        var ids = new FormId[span.Length / 4];
        for (int i = 0; i < ids.Length; i++)
        {
            ids[i] = new FormId(BinaryPrimitives.ReadUInt32LittleEndian(span[(i * 4)..]));
        }

        return new Success<IReadOnlyList<FormId>>(ids);
    }

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Signature} ({Size} bytes)");

    private ParseError SizeMismatch(string what, int expected)
        => SizeMismatchError(what, expected.ToString(CultureInfo.InvariantCulture));

    private ParseError SizeMismatchError(string what, string expected) => new(
        ParseErrorKind.FieldInvalid,
        string.Create(
            CultureInfo.InvariantCulture,
            $"Field {Signature} is {Size} bytes, but {what} requires {expected}."),
        AbsoluteDataOffset);
}
