using BethesdaArchiveParser.Core.Reader;

using var fs = new FileStream("D:\\source\\repos\\EsmExplorer\\Resources\\Fallout - Meshes.bsa", FileMode.Open, FileAccess.Read);
var reader = new BsaArchiveReader(fs);

var archive = reader.ReadBsaArchive();

const string FileName = "meshes\\dungeons\\caves\\epic\\caveepicdoorboth02.nif";

var randomFileHere = archive.GetFileByPath(FileName);
var fileContent = reader.ReadBsaFile(randomFileHere!);
File.WriteAllBytes($".\\{FileName.Replace('\\', '_')}", fileContent);

Console.WriteLine($"randomFileHere: {randomFileHere}");
