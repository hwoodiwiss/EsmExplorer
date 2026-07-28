using System.Buffers.Binary;
using System.Globalization;

namespace EsmParser.Core.Format;

/// <summary>
/// The fixed 24-byte header of a GRUP container.
/// </summary>
/// <remarks>
/// Layout: "GRUP" signature (4), total size including this header (4), label (4),
/// group type (4), timestamp (2), version control info (2), unknown (4).
/// The label's interpretation depends on <see cref="GroupType"/>; per the spec it is
/// not fully reliable (the CK's ignore flag can corrupt label bytes).
/// </remarks>
public readonly record struct GroupHeader(
    uint TotalSize,
    uint RawLabel,
    GroupType GroupType,
    ushort Timestamp,
    ushort VersionControlInfo,
    uint Unknown)
{
    /// <summary>Size of the header on disk, in bytes.</summary>
    public const int Size = 24;

    /// <summary>Size of the group's contents, excluding the 24-byte header.</summary>
    public long ContentSize => (long)TotalSize - Size;

    /// <summary>The label as a record-type signature (meaningful for <see cref="GroupType.Top"/>).</summary>
    public Signature LabelAsSignature => new(RawLabel);

    /// <summary>The label as a parent form id (world/cell/topic/quest children groups).</summary>
    public FormId LabelAsFormId => new(RawLabel);

    /// <summary>The label as a block number (interior cell block/sub-block groups).</summary>
    public int LabelAsBlockNumber => unchecked((int)RawLabel);

    /// <summary>The label as grid coordinates (exterior cell block/sub-block groups). Stored Y-first on disk.</summary>
    public (short Y, short X) LabelAsGridCell =>
        (unchecked((short)(RawLabel & 0xFFFF)), unchecked((short)(RawLabel >> 16)));

    /// <summary>
    /// Reads a header from the first <see cref="Size"/> bytes of <paramref name="source"/>.
    /// The caller is responsible for checking the GRUP signature and supplying a large-enough span.
    /// </summary>
    public static GroupHeader Read(ReadOnlySpan<byte> source) => new(
        BinaryPrimitives.ReadUInt32LittleEndian(source[4..]),
        BinaryPrimitives.ReadUInt32LittleEndian(source[8..]),
        (GroupType)BinaryPrimitives.ReadInt32LittleEndian(source[12..]),
        BinaryPrimitives.ReadUInt16LittleEndian(source[16..]),
        BinaryPrimitives.ReadUInt16LittleEndian(source[18..]),
        BinaryPrimitives.ReadUInt32LittleEndian(source[20..]));

    /// <summary>Writes the 24-byte on-disk representation (including the GRUP signature) to <paramref name="destination"/>.</summary>
    public void WriteTo(Span<byte> destination)
    {
        KnownSignatures.Grup.WriteTo(destination);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[4..], TotalSize);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[8..], RawLabel);
        BinaryPrimitives.WriteInt32LittleEndian(destination[12..], (int)GroupType);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[16..], Timestamp);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[18..], VersionControlInfo);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[20..], Unknown);
    }

    /// <summary>Interprets the label according to the group type, producing display text such as <c>"Cell 0001C3A4 (Temporary)"</c>.</summary>
    public string GetLabelDescription() => GroupType switch
    {
        GroupType.Top => LabelAsSignature.ToString(),
        GroupType.WorldChildren => Invariant($"World {LabelAsFormId}"),
        GroupType.InteriorCellBlock => Invariant($"Block {LabelAsBlockNumber}"),
        GroupType.InteriorCellSubBlock => Invariant($"Sub-Block {LabelAsBlockNumber}"),
        GroupType.ExteriorCellBlock => Invariant($"Block ({LabelAsGridCell.X}, {LabelAsGridCell.Y})"),
        GroupType.ExteriorCellSubBlock => Invariant($"Sub-Block ({LabelAsGridCell.X}, {LabelAsGridCell.Y})"),
        GroupType.CellChildren => Invariant($"Cell {LabelAsFormId}"),
        GroupType.TopicChildren => Invariant($"Topic {LabelAsFormId}"),
        GroupType.CellPersistentChildren => Invariant($"Cell {LabelAsFormId} (Persistent)"),
        GroupType.CellTemporaryChildren => Invariant($"Cell {LabelAsFormId} (Temporary)"),
        GroupType.QuestChildren => Invariant($"Quest {LabelAsFormId}"),
        _ => Invariant($"0x{RawLabel:X8}"),
    };

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
