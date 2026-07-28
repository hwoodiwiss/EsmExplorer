using System.Globalization;
using Microsoft.Win32.SafeHandles;

namespace EsmParser.Core.IO;

/// <summary>
/// An <see cref="IDataSource"/> over a file on disk, using positional reads so that
/// concurrent readers never contend over a shared stream position.
/// </summary>
public sealed class FileDataSource : IDataSource
{
    private readonly SafeFileHandle _handle;

    private FileDataSource(SafeFileHandle handle, string name, long length)
    {
        _handle = handle;
        Name = name;
        Length = length;
    }

    public string Name { get; }

    public long Length { get; }

    /// <summary>Opens <paramref name="path"/> for shared random-access reading.</summary>
    public static FileDataSource Open(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        SafeFileHandle handle = File.OpenHandle(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            FileOptions.RandomAccess | FileOptions.Asynchronous);
        try
        {
            return new FileDataSource(handle, Path.GetFileName(path), RandomAccess.GetLength(handle));
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    public async ValueTask<ParseResult<Unit>> ReadExactlyAsync(long offset, Memory<byte> destination, CancellationToken cancellationToken = default)
    {
        if (DataSourceGuard.CheckRange(offset, destination.Length, Length) is { } rangeError)
        {
            return rangeError;
        }

        try
        {
            int filled = 0;
            while (filled < destination.Length)
            {
                int read = await RandomAccess.ReadAsync(_handle, destination[filled..], offset + filled, cancellationToken);
                if (read == 0)
                {
                    return new ParseError(
                        ParseErrorKind.UnexpectedEndOfData,
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"The file ended after {filled} of {destination.Length} requested bytes."),
                        offset + filled);
                }

                filled += read;
            }

            return new Success<Unit>(Unit.Value);
        }
        catch (IOException exception)
        {
            return new ParseError(ParseErrorKind.Io, exception.Message, offset);
        }
    }

    public ValueTask DisposeAsync()
    {
        _handle.Dispose();
        return ValueTask.CompletedTask;
    }
}
