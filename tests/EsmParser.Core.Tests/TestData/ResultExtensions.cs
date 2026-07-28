using System.Globalization;

namespace EsmParser.Core.Tests.TestData;

/// <summary>Terse unwrapping of result unions inside tests.</summary>
internal static class ResultExtensions
{
    public static T ShouldSucceed<T>(this ParseResult<T> result)
    {
        if (!result.TryGet(out T? value, out ParseError? error))
        {
            throw new InvalidOperationException($"Expected success but got: {error}");
        }

        return value;
    }

    public static ParseError ShouldFail<T>(this ParseResult<T> result)
    {
        if (result.TryGet(out T? value, out ParseError? error))
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"Expected a parse error but the operation succeeded with: {value}"));
        }

        return error;
    }
}
