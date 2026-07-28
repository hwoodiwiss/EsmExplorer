using System.Globalization;
using EsmParser.Core;
using EsmParser.Core.IO;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace EsmParser.Web.Services;

/// <summary>
/// An <see cref="IDataSource"/> over a browser <c>File</c>, reading slices on demand
/// through JS interop so that arbitrarily large plugins never have to fit in WASM memory.
/// Wrap it in a <see cref="CachingDataSource"/> to keep interop chatter down.
/// </summary>
public sealed class BlobDataSource : IDataSource
{
    private readonly IJSObjectReference _module;
    private readonly int _fileId;

    private BlobDataSource(IJSObjectReference module, int fileId, string name, long length)
    {
        _module = module;
        _fileId = fileId;
        Name = name;
        Length = length;
    }

    public string Name { get; }

    public long Length { get; }

    /// <summary>
    /// Registers the file currently selected in <paramref name="fileInput"/> (an
    /// <c>&lt;input type="file"&gt;</c> element), or returns <see langword="null"/> when none is selected.
    /// </summary>
    public static async ValueTask<BlobDataSource?> CreateFromInputAsync(IJSRuntime jsRuntime, ElementReference fileInput)
    {
        ArgumentNullException.ThrowIfNull(jsRuntime);
        IJSObjectReference module = await jsRuntime.InvokeAsync<IJSObjectReference>("import", "./js/fileAccess.js");
        BlobFileInfo? info = await module.InvokeAsync<BlobFileInfo?>("register", fileInput);
        if (info is null)
        {
            await module.DisposeAsync();
            return null;
        }

        return new BlobDataSource(module, info.Id, info.Name, info.Size);
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
            byte[] bytes = await _module.InvokeAsync<byte[]>("readSlice", cancellationToken, _fileId, offset, destination.Length);
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
        try
        {
            await _module.InvokeVoidAsync("release", _fileId);
            await _module.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
            // The circuit/runtime is gone; nothing left to release.
        }
    }

    private sealed record BlobFileInfo(int Id, string Name, long Size);
}
