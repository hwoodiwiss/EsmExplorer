using System.Globalization;
using EsmParser.Core;
using EsmParser.Core.IO;
using Microsoft.JSInterop;

namespace EsmParser.Web.Services;

/// <summary>
/// An <see cref="IDataSource"/> over a file snapshot opened from the data root directory handle.
/// Bytes are sliced on demand through JS interop, so large plugins never have to fit in WASM memory.
/// The module is owned by <see cref="GameDataService"/>; disposal only releases this file.
/// </summary>
public sealed class DataRootDataSource : IDataSource
{
    private readonly IJSObjectReference _module;
    private readonly int _fileId;
    private int _disposed;

    private DataRootDataSource(IJSObjectReference module, int fileId, string name, long length)
    {
        _module = module;
        _fileId = fileId;
        Name = name;
        Length = length;
    }

    public string Name { get; }

    public long Length { get; }

    internal static async Task<DataRootDataSource?> OpenAsync(IJSObjectReference module, string name)
    {
        try
        {
            OpenedFile? file = await module.InvokeAsync<OpenedFile?>("openFile", name);
            return file is null ? null : new DataRootDataSource(module, file.Id, name, file.Length);
        }
        catch (JSException ex)
        {
            throw new IOException($"Could not open browser file '{name}'.", ex);
        }
    }

    public async ValueTask<ParseResult<Unit>> ReadExactlyAsync(long offset, Memory<byte> destination, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        if (offset + destination.Length > Length)
        {
            return new ParseError(
                ParseErrorKind.UnexpectedEndOfData,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Requested {destination.Length} bytes at offset 0x{offset:X}, but the file is only {Length} bytes long."),
                offset);
        }

        try
        {
            byte[] bytes = await _module.InvokeAsync<byte[]>("readFileChunk", cancellationToken, _fileId, offset, destination.Length);
            if (bytes.Length != destination.Length)
            {
                return new ParseError(
                    ParseErrorKind.UnexpectedEndOfData,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"The browser returned {bytes.Length} bytes where {destination.Length} were requested."),
                    offset);
            }

            bytes.CopyTo(destination);
            return new Success<Unit>(Unit.Value);
        }
        catch (JSException exception)
        {
            return new ParseError(ParseErrorKind.Io, exception.Message, offset);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }
        try
        {
            await _module.InvokeVoidAsync("closeFile", _fileId);
        }
        catch (JSDisconnectedException)
        {
            // The runtime is gone; nothing left to release.
        }
        catch (ObjectDisposedException)
        {
            // The owning module was already disposed with its service.
        }
    }

    private sealed record OpenedFile(int Id, long Length);
}
