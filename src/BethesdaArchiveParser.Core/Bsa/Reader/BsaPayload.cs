using System.IO.Compression;
using BethesdaArchiveParser.Core.IO;
using K4os.Compression.LZ4.Streams;

namespace BethesdaArchiveParser.Core.Bsa.Reader;

internal static class BsaPayload
{
    public static ValueTask<long> Copy(Stream source, Stream destination, BsaVersion version, long offset,
        uint storedSize, uint expandedSize, bool compressed, bool isAsync, CancellationToken cancellationToken)
    {
        Func<Stream, Stream>? decoder = compressed
            ? version switch
            {
                BsaVersion.Oblivion or BsaVersion.Fallout3AndSkyrim => static input => new ZLibStream(input, CompressionMode.Decompress, true),
                BsaVersion.SkyrimSpecialEdition => static input => LZ4Stream.Decode(input, leaveOpen: true),
                _ => throw new InvalidDataException($"Unsupported BSA version {(uint)version}."),
            }
            : null;
        return BoundedPayload.Copy(source, destination, offset, storedSize, expandedSize, decoder, isAsync, cancellationToken);
    }
}
