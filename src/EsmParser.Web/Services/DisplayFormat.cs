using System.Globalization;
using EsmParser.Core.Navigation;

namespace EsmParser.Web.Services;

/// <summary>Display-side formatting for offsets, sizes, and field previews.</summary>
public static class DisplayFormat
{
    public static string Hex(long value) => string.Create(CultureInfo.InvariantCulture, $"0x{value:X}");

    public static string Bytes(long size) => size switch
    {
        < 1024 => Invariant($"{size} B"),
        < 1024 * 1024 => Invariant($"{size / 1024.0:0.#} KiB"),
        < 1024 * 1024 * 1024 => Invariant($"{size / (1024.0 * 1024.0):0.#} MiB"),
        _ => Invariant($"{size / (1024.0 * 1024.0 * 1024.0):0.##} GiB"),
    };

    public static string OffsetRange(PluginNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return Invariant($"{Hex(node.Offset)} – {Hex(node.EndOffset)}");
    }

    /// <summary>A best-effort one-line preview of a field's payload.</summary>
    public static string FieldPreview(RecordField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        ReadOnlySpan<byte> data = field.Data.Span;
        if (data.Length == 0)
        {
            return "(empty)";
        }

        if (LooksLikeText(data, out string? text))
        {
            return Invariant($"\"{text}\"");
        }

        if (data.Length == 4)
        {
            uint value = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(data);
            float single = System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian(data);
            string floatPart = float.IsFinite(single) && Math.Abs(single) is 0 or (> 1e-6f and < 1e12f)
                ? Invariant($" | f32 {single:0.####}")
                : string.Empty;
            return Invariant($"u32 {value} | id {new Core.Format.FormId(value)}{floatPart}");
        }

        int shown = Math.Min(data.Length, 64);
        string hex = Convert.ToHexString(data[..shown]);
        return data.Length > shown ? Invariant($"{hex}… ({data.Length} bytes)") : hex;
    }

    private static bool LooksLikeText(ReadOnlySpan<byte> data, out string? text)
    {
        text = null;
        ReadOnlySpan<byte> body = data[^1] == 0 ? data[..^1] : data;
        if (body.Length is 0 or > 256)
        {
            return false;
        }

        foreach (byte b in body)
        {
            if (b is < 0x20 or > 0x7E)
            {
                return false;
            }
        }

        text = System.Text.Encoding.ASCII.GetString(body);
        return true;
    }

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
