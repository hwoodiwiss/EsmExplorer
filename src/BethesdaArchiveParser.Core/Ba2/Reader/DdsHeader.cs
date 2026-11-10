using System.Buffers.Binary;

namespace BethesdaArchiveParser.Core.Ba2.Reader;

// Writes DDS_HEADER + DDS_HEADER_DXT10 per https://learn.microsoft.com/windows/win32/direct3ddds/dds-header.
internal static class DdsHeader
{
    public const int Length = 4 + 124 + 20;

    private const uint Caps = 0x1, Height = 0x2, Width = 0x4, Pitch = 0x8, PixelFormat = 0x1000, MipMapCount = 0x20000, LinearSize = 0x80000;
    private const uint FourCc = 0x4;
    private const uint CapsComplex = 0x8, CapsTexture = 0x1000, CapsMipMap = 0x400000;
    private const uint Caps2CubemapAllFaces = 0xFE00;
    private const uint Dimension2D = 3, MiscTextureCube = 0x4;

    public static byte[] Create(Ba2TextureInfo texture)
    {
        byte[] bytes = new byte[Length];
        Span<byte> span = bytes;
        "DDS "u8.CopyTo(span);
        var (pitch, isBlockCompressed) = GetPitchOrLinearSize(texture);
        uint flags = Caps | Height | Width | PixelFormat | MipMapCount;
        if (pitch != 0)
        {
            flags |= isBlockCompressed ? LinearSize : Pitch;
        }
        uint caps = CapsTexture;
        if (texture.MipCount > 1)
        {
            caps |= CapsComplex | CapsMipMap;
        }
        if (texture.IsCubemap)
        {
            caps |= CapsComplex;
        }
        BinaryPrimitives.WriteUInt32LittleEndian(span[4..], 124);
        BinaryPrimitives.WriteUInt32LittleEndian(span[8..], flags);
        BinaryPrimitives.WriteUInt32LittleEndian(span[12..], texture.Height);
        BinaryPrimitives.WriteUInt32LittleEndian(span[16..], texture.Width);
        BinaryPrimitives.WriteUInt32LittleEndian(span[20..], pitch);
        BinaryPrimitives.WriteUInt32LittleEndian(span[28..], Math.Max((uint)texture.MipCount, 1));
        BinaryPrimitives.WriteUInt32LittleEndian(span[76..], 32);
        BinaryPrimitives.WriteUInt32LittleEndian(span[80..], FourCc);
        "DX10"u8.CopyTo(span[84..]);
        BinaryPrimitives.WriteUInt32LittleEndian(span[108..], caps);
        BinaryPrimitives.WriteUInt32LittleEndian(span[112..], texture.IsCubemap ? Caps2CubemapAllFaces : 0);
        BinaryPrimitives.WriteUInt32LittleEndian(span[128..], texture.DxgiFormat);
        BinaryPrimitives.WriteUInt32LittleEndian(span[132..], Dimension2D);
        BinaryPrimitives.WriteUInt32LittleEndian(span[136..], texture.IsCubemap ? MiscTextureCube : 0);
        BinaryPrimitives.WriteUInt32LittleEndian(span[140..], 1);
        return bytes;
    }

    // Top-mip pitch (uncompressed) or linear size (block-compressed). 0 when the format is not recognised.
    private static (uint Value, bool BlockCompressed) GetPitchOrLinearSize(Ba2TextureInfo texture)
    {
        uint width = Math.Max(texture.Width, (ushort)1);
        uint height = Math.Max(texture.Height, (ushort)1);
        uint? blockBytes = texture.DxgiFormat switch
        {
            >= 70 and <= 72 or >= 79 and <= 81 => 8,   // BC1, BC4
            >= 73 and <= 78 or >= 82 and <= 84 or >= 94 and <= 99 => 16, // BC2, BC3, BC5, BC6H, BC7
            _ => null,
        };
        if (blockBytes is { } block)
        {
            return (Math.Max(1, (width + 3) / 4) * Math.Max(1, (height + 3) / 4) * block, true);
        }
        uint bitsPerPixel = texture.DxgiFormat switch
        {
            2 => 128,                                    // R32G32B32A32_FLOAT
            10 or 11 or 13 => 64,                        // R16G16B16A16
            >= 23 and <= 32 or >= 87 and <= 93 => 32, // R10G10B10A2, R11G11B10, R8G8B8A8, B8G8R8A8/X8
            >= 48 and <= 52 or >= 85 and <= 86 or 115 => 16, // R8G8, B5G6R5, B5G5R5A1, B4G4R4A4
            >= 60 and <= 65 => 8,                        // R8, A8
            _ => 0,
        };
        return ((width * bitsPerPixel + 7) / 8, false);
    }
}
