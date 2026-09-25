namespace BethesdaArchiveParser.Core.Extensions;

internal static class ValueTaskExtensions
{
    /// <summary>
    /// Coerces a ValueTask`1 to be consumed as if synchronous, for context where the ValueTask is known to be non-awaiting.
    /// </summary>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
    public static T AsSync<T>(this ValueTask<T> source)
    {
        if (!source.IsCompleted)
        {
            throw new InvalidOperationException("Synchronous operation unexpectedly ran asynchronously.");
        }
        return source.Result;
    }
}
