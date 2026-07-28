using System.Buffers.Binary;

namespace EsmParser.Core.Format;

/// <summary>
/// A four-character code (4CC) identifying a record, group, or field type,
/// stored as the little-endian 32-bit value exactly as it appears on disk.
/// </summary>
public readonly record struct Signature
{
    public Signature(uint value) => Value = value;

    /// <summary>The raw little-endian value as stored in the file.</summary>
    public uint Value { get; }

    /// <summary>Reads a signature from the first four bytes of <paramref name="source"/>.</summary>
    public static Signature Read(ReadOnlySpan<byte> source) => new(BinaryPrimitives.ReadUInt32LittleEndian(source));

    /// <summary>Creates a signature from a four-character ASCII string such as <c>"GRUP"</c>.</summary>
    public static Signature FromString(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length != 4)
        {
            throw new ArgumentException("A signature must be exactly four characters.", nameof(text));
        }

        Span<byte> bytes = stackalloc byte[4];
        for (int i = 0; i < 4; i++)
        {
            char c = text[i];
            if (c > 0x7F)
            {
                throw new ArgumentException("A signature may only contain ASCII characters.", nameof(text));
            }

            bytes[i] = (byte)c;
        }

        return Read(bytes);
    }

    /// <summary>Writes the signature to the first four bytes of <paramref name="destination"/>.</summary>
    public void WriteTo(Span<byte> destination) => BinaryPrimitives.WriteUInt32LittleEndian(destination, Value);

    /// <summary>Whether all four bytes are printable ASCII, making <see cref="ToString"/> render the 4CC text.</summary>
    public bool IsPrintable
    {
        get
        {
            for (int i = 0; i < 4; i++)
            {
                byte b = (byte)(Value >> (i * 8));
                if (b < 0x20 || b > 0x7E)
                {
                    return false;
                }
            }

            return true;
        }
    }

    public override string ToString()
    {
        if (!IsPrintable)
        {
            return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"0x{Value:X8}");
        }

        Span<char> chars = stackalloc char[4];
        for (int i = 0; i < 4; i++)
        {
            chars[i] = (char)(byte)(Value >> (i * 8));
        }

        return new string(chars);
    }
}

/// <summary>Signatures with structural meaning to the file format itself.</summary>
public static class KnownSignatures
{
    public static Signature Grup { get; } = Signature.FromString("GRUP");

    public static Signature Tes4 { get; } = Signature.FromString("TES4");

    /// <summary>Extended field-size carrier: holds the 32-bit size of the following field.</summary>
    public static Signature Xxxx { get; } = Signature.FromString("XXXX");

    public static Signature Hedr { get; } = Signature.FromString("HEDR");

    public static Signature Cnam { get; } = Signature.FromString("CNAM");

    public static Signature Snam { get; } = Signature.FromString("SNAM");

    public static Signature Mast { get; } = Signature.FromString("MAST");

    public static Signature Data { get; } = Signature.FromString("DATA");

    public static Signature Onam { get; } = Signature.FromString("ONAM");

    public static Signature Intv { get; } = Signature.FromString("INTV");

    public static Signature Incc { get; } = Signature.FromString("INCC");

    public static Signature Tnam { get; } = Signature.FromString("TNAM");

    public static Signature Edid { get; } = Signature.FromString("EDID");

    public static Signature Full { get; } = Signature.FromString("FULL");
}
