using System.Runtime.InteropServices;

namespace BethesdaArchiveParser.Core;

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct BinaryBsaHeader
{
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 4)]
    public string MagicBytes;
    public uint Version;
    public uint RecordOffset;
    public BsaArchiveFlags ArchiveFlags;
    public uint FolderCount;
    public uint FileCount;
    public uint TotalFolderNameLength;
    public uint TotalFileNameLength;
    public BsaFileFlags FileFlags;
    public ushort Padding;
}