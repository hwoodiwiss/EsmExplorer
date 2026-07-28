using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace EsmParser.Core;

/// <summary>
/// The absence of a meaningful value; the success payload of effect-only operations.
/// </summary>
public readonly record struct Unit
{
    public static Unit Value => default;
}

/// <summary>Categorises the ways plugin data can fail to parse.</summary>
public enum ParseErrorKind
{
    /// <summary>A magic/type signature was not what the format requires (e.g. the file does not start with TES4).</summary>
    InvalidSignature,

    /// <summary>The data ended before a complete structure could be read.</summary>
    UnexpectedEndOfData,

    /// <summary>A structure declares an impossible shape (e.g. a group smaller than its own header).</summary>
    StructureInvalid,

    /// <summary>A node's declared size runs past the end of its container.</summary>
    StructureOverrun,

    /// <summary>An XXXX extended-size field was malformed.</summary>
    ExtendedSizeInvalid,

    /// <summary>A field header or field payload was malformed, or a field could not be decoded as the requested type.</summary>
    FieldInvalid,

    /// <summary>Compressed record data could not be inflated, or its size did not match the declared size.</summary>
    DecompressionFailed,

    /// <summary>The TES4 plugin header record is missing required content.</summary>
    HeaderInvalid,

    /// <summary>The underlying data source failed to provide data.</summary>
    Io,
}

/// <summary>
/// Describes why plugin data failed to parse, with the absolute file offset where the problem was detected when known.
/// </summary>
public sealed record ParseError(ParseErrorKind Kind, string Message, long? Offset = null)
{
    public override string ToString() => Offset is { } offset
        ? string.Create(CultureInfo.InvariantCulture, $"{Kind}: {Message} (at file offset 0x{offset:X})")
        : string.Create(CultureInfo.InvariantCulture, $"{Kind}: {Message}");
}

/// <summary>Successful outcome of an operation, carrying the produced value.</summary>
public readonly record struct Success<T>(T Value);

/// <summary>
/// The outcome of a parsing operation: either <see cref="Success{T}"/> or a <see cref="ParseError"/>.
/// </summary>
public union ParseResult<T>(Success<T>, ParseError)
{
    public bool IsSuccess => this is Success<T>;

    /// <summary>
    /// Extracts either the success value or the error. Exactly one of
    /// <paramref name="value"/>/<paramref name="error"/> is populated.
    /// </summary>
    public bool TryGet([MaybeNullWhen(false)] out T value, [NotNullWhen(false)] out ParseError? error)
    {
        if (this is Success<T>(var success))
        {
            value = success;
            error = null;
            return true;
        }

        if (this is ParseError parseError)
        {
            value = default;
            error = parseError;
            return false;
        }

        throw new InvalidOperationException("This ParseResult was default-initialized and holds no outcome.");
    }
}
