using System.Text;

namespace BethesdaArchiveParser.Core.IO;

internal static class ArchiveEncoding
{
    /// <summary>Bethesda archive names are Windows-1252.</summary>
    public static Encoding Windows1252 { get; } = Create();

    private static Encoding Create()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1252);
    }
}
