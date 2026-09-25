
using System.Text;
using BethesdaArchiveParser.Core.Extensions;

namespace BethesdaArchiveParser.Core.Reader;

public sealed class BsaArchiveReader(Stream stream)
{
    public BsaArchive ReadBsaArchive()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        BinaryBsaHeader bsaHeader = BsaReader.ReadHeader(stream, false).AsSync();
        var bsaReader = new BsaReader(bsaHeader, stream, false);
        var bsaContentReader = new BsaContentReader(bsaHeader, bsaReader);
        var (folders, fileBlocks, fileNames) = bsaContentReader.ReadBsaArchive().AsSync();
        return new BsaArchive(bsaHeader, folders, fileBlocks, fileNames!);
    }

    public async Task<BsaArchive> ReadBsaArchiveAsync()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        BinaryBsaHeader bsaHeader = await BsaReader.ReadHeader(stream, true);
        var bsaReader = new BsaReader(bsaHeader, stream, true);
        var bsaContentReader = new BsaContentReader(bsaHeader, bsaReader);
        var (folders, fileBlocks, fileNames) = await bsaContentReader.ReadBsaArchive();
        return new BsaArchive(bsaHeader, folders, fileBlocks, fileNames!);
    }

    public byte[] ReadBsaFile(BsaFileRecord file)
    {
        ArgumentNullException.ThrowIfNull(file);
        var bsaReader = new BsaReader(file.Header, stream, false);
        var bsaContentReader = new BsaContentReader(file.Header, bsaReader);
        var fileData = bsaContentReader.ReadBsaFileData(file).AsSync();
        var data = fileData switch
        {
            BsaCompressedFile compressedFile => compressedFile.GetContent(),
            BsaUncompressedFile uncompressedFile => uncompressedFile.GetContent(),
            _ => throw new InvalidOperationException("Unknown file data type.")
        };
        return data;
    }

    public async Task<byte[]> ReadBsaFileAsync(BsaFileRecord file)
    {
        ArgumentNullException.ThrowIfNull(file);
        var bsaReader = new BsaReader(file.Header, stream, true);
        var bsaContentReader = new BsaContentReader(file.Header, bsaReader);
        var fileData = await bsaContentReader.ReadBsaFileData(file);
        var data = fileData switch
        {
            BsaCompressedFile compressedFile => compressedFile.GetContent(),
            BsaUncompressedFile uncompressedFile => uncompressedFile.GetContent(),
            _ => throw new InvalidOperationException("Unknown file data type.")
        };
        return data;
    }
}
