
using System.Text;

namespace BethesdaArchiveParser.Core.Reader;

public sealed class BsaArchiveReader(Stream stream)
{
    public async Task<BsaArchive> ReadBsaArchiveAsync()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        BinaryBsaHeader bsaHeader = BsaReader.ReadHeader(stream);
        var bsaReader = new BsaReader(bsaHeader, stream);
        var bsaContentReader = new BsaContentReader(bsaHeader, bsaReader);
        var (folders, fileBlocks, fileNames) = bsaContentReader.ReadBsaArchive();
        return new BsaArchive(bsaHeader, folders, fileBlocks, fileNames!);
    }

    public byte[] ReadBsaFile(BsaFileRecord file)
    {
        ArgumentNullException.ThrowIfNull(file);
        var bsaReader = new BsaReader(file.Header, stream);
        var bsaContentReader = new BsaContentReader(file.Header, bsaReader);
        var fileData = bsaContentReader.ReadBsaFileData(file);
        var data = fileData switch
        {
            BsaCompressedFile compressedFile => compressedFile.GetContent(),
            BsaUncompressedFile uncompressedFile => uncompressedFile.GetContent(),
            _ => throw new InvalidOperationException("Unknown file data type.")
        };
        return data;
    }
}
