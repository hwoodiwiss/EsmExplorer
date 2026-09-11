namespace BethesdaArchiveParser.Core;

public static class BsaHash
{
    public static ulong Compute(string name)
    {
        name = name.Replace('/', '\\');
        return GetHash(Path.ChangeExtension(name, null), Path.GetExtension(name));
    }

    private static ulong GetHash(string name, string ext)
    {
        name = name.ToLowerInvariant();
        ext = ext.ToLowerInvariant();
        byte[] hashBytes =
        [
            (byte)(name.Length == 0 ? '\0' : name[^1]),
            (byte)(name.Length < 3 ? '\0' : name[^2]),
            (byte)name.Length,
            (byte)name[0]
        ];
        var hash1 = BitConverter.ToUInt32(hashBytes, 0);
        uint contentTypeBit = ext switch
        {
            ".kf" => 0x80,
            ".nif" => 0x8000,
            ".dds" => 0x8080,
            ".wav" => 0x80000000,
            _ => 0
        };
        hash1 |= contentTypeBit;

        uint hash2 = 0;
        for (var i = 1; i < name.Length - 2; i++)
        {
            hash2 = hash2 * 0x1003f + (byte)name[i];
        }

        uint hash3 = 0;
        for (var i = 0; i < ext.Length; i++)
        {
            hash3 = hash3 * 0x1003f + (byte)ext[i];
        }

        return (((ulong)(hash2 + hash3)) << 32) + hash1;
    }
}
