using BethesdaArchiveParser.Core;

using var fs = new FileStream("D:\\source\\repos\\EsmExplorer\\Resources\\Fallout - Meshes.bsa", FileMode.Open, FileAccess.Read);
using var reader = new BsaReader(fs);

BinaryBsaHeader header = reader.ReadHeader();

var archive = reader.ReadBsaArchive(header);

Console.WriteLine($"Magic Bytes: {header.MagicBytes}");
