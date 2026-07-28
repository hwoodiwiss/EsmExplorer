using System.Globalization;

namespace EsmParser.Core.IO;

/// <summary>Shared range validation for <see cref="IDataSource"/> implementations.</summary>
internal static class DataSourceGuard
{
    /// <summary>
    /// Validates a read request against the source length. Negative offsets are a caller
    /// contract violation and throw; ranges beyond the end are a data-shape problem and
    /// produce an error result.
    /// </summary>
    internal static ParseError? CheckRange(long offset, int count, long length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);

        if (offset + count > length)
        {
            return new ParseError(
                ParseErrorKind.UnexpectedEndOfData,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Requested {count} bytes at offset 0x{offset:X}, but the source is only {length} bytes long."),
                offset);
        }

        return null;
    }
}
