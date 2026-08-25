namespace BethesdaArchiveParser.Core;

[Flags]
public enum BsaFileFlags : ushort
{
    None = 0,
    Meshes = 1 << 0,
    Textures = 1 << 1,
    Menus = 1 << 2,
    Sounds = 1 << 3,
    Voices = 1 << 4,
    Shaders = 1 << 5,
    Trees = 1 << 6,
    Fonts = 1 << 7,
    Miscellaneous = 1 << 8,
}