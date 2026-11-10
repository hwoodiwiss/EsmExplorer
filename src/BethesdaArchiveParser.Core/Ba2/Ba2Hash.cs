using BethesdaArchiveParser.Core.IO;

namespace BethesdaArchiveParser.Core.Ba2;

/// <param name="Name">CRC of the file stem (no extension).</param>
/// <param name="Extension">Up to four extension bytes, packed little-endian.</param>
/// <param name="Directory">CRC of the parent directory path.</param>
public readonly record struct Ba2PathHash(uint Name, uint Extension, uint Directory);

/// <summary>
/// BA2 path hashing: CRC-32 (reflected 0xEDB88320) with an initial value of 0 and no final XOR.
/// This is unrelated to the 64-bit <see cref="Bsa.BsaHash"/>; only path normalisation is shared.
/// </summary>
public static class Ba2Hash
{
    private static readonly uint[] Table = CreateTable();

    public static Ba2PathHash Compute(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        string normalized = Normalize(path);
        int slash = normalized.LastIndexOf('\\');
        string directory = slash < 0 ? "" : normalized[..slash];
        string fileName = normalized[(slash + 1)..];
        int dot = fileName.LastIndexOf('.');
        string stem = dot < 0 ? fileName : fileName[..dot];
        string extension = dot < 0 ? "" : fileName[(dot + 1)..];
        Span<byte> ext = stackalloc byte[4];
        ext.Clear();
        byte[] extBytes = ArchiveEncoding.Windows1252.GetBytes(extension);
        extBytes.AsSpan(0, Math.Min(4, extBytes.Length)).CopyTo(ext);
        return new Ba2PathHash(Crc(stem), BitConverter.ToUInt32(ext), Crc(directory));
    }

    private static string Normalize(string path)
    {
        Span<char> chars = path.Length <= 256 ? stackalloc char[path.Length] : new char[path.Length];
        for (int i = 0; i < path.Length; i++)
        {
            char c = path[i];
            chars[i] = c switch
            {
                '/' => '\\',
                >= 'A' and <= 'Z' => (char)(c + 32),
                _ => c,
            };
        }
        return new string(chars).Trim('\\');
    }

    private static uint Crc(string text)
    {
        uint crc = 0;
        foreach (byte b in ArchiveEncoding.Windows1252.GetBytes(text))
        {
            crc = (crc >> 8) ^ Table[(crc ^ b) & 0xFF];
        }
        return crc;
    }

    private static uint[] CreateTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint value = i;
            for (int bit = 0; bit < 8; bit++)
            {
                value = (value & 1) != 0 ? (value >> 1) ^ 0xEDB88320u : value >> 1;
            }
            table[i] = value;
        }
        return table;
    }
}
