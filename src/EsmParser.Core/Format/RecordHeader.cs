using System.Buffers.Binary;

namespace EsmParser.Core.Format;

/// <summary>
/// The fixed 24-byte header preceding every record's data.
/// </summary>
/// <remarks>
/// Layout: signature (4), data size (4), flags (4), form id (4), timestamp (2),
/// version control info (2), form version (2), unknown (2). Starfield writes
/// form version 581 as of the format revision this parser targets.
/// </remarks>
public readonly record struct RecordHeader(
    Signature Signature,
    uint DataSize,
    RecordFlags Flags,
    FormId FormId,
    ushort Timestamp,
    ushort VersionControlInfo,
    ushort FormVersion,
    ushort Unknown)
{
    /// <summary>Size of the header on disk, in bytes.</summary>
    public const int Size = 24;

    public bool IsCompressed => (Flags & RecordFlags.Compressed) != 0;

    public bool IsDeleted => (Flags & RecordFlags.Deleted) != 0;

    /// <summary>
    /// Decodes the bit-packed timestamp (0bYYYYYYYMMMMDDDDD, years since 2000),
    /// or <see langword="null"/> when the stored value is not a valid date.
    /// </summary>
    public DateOnly? TimestampAsDate
    {
        get
        {
            if (Timestamp == 0)
            {
                return null;
            }

            int year = 2000 + (Timestamp >> 9);
            int month = (Timestamp >> 5) & 0xF;
            int day = Timestamp & 0x1F;
            if (month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month))
            {
                return null;
            }

            return new DateOnly(year, month, day);
        }
    }

    /// <summary>
    /// Reads a header from the first <see cref="Size"/> bytes of <paramref name="source"/>.
    /// The caller is responsible for supplying a large-enough span; no content validation is performed.
    /// </summary>
    public static RecordHeader Read(ReadOnlySpan<byte> source) => new(
        Signature.Read(source),
        BinaryPrimitives.ReadUInt32LittleEndian(source[4..]),
        (RecordFlags)BinaryPrimitives.ReadUInt32LittleEndian(source[8..]),
        new FormId(BinaryPrimitives.ReadUInt32LittleEndian(source[12..])),
        BinaryPrimitives.ReadUInt16LittleEndian(source[16..]),
        BinaryPrimitives.ReadUInt16LittleEndian(source[18..]),
        BinaryPrimitives.ReadUInt16LittleEndian(source[20..]),
        BinaryPrimitives.ReadUInt16LittleEndian(source[22..]));

    /// <summary>Writes the 24-byte on-disk representation to <paramref name="destination"/>.</summary>
    public void WriteTo(Span<byte> destination)
    {
        Signature.WriteTo(destination);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[4..], DataSize);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[8..], (uint)Flags);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[12..], FormId.Value);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[16..], Timestamp);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[18..], VersionControlInfo);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[20..], FormVersion);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[22..], Unknown);
    }
}
