namespace BethesdaArchiveParser.Core.Bsa.Reader;

internal sealed class BsaContentReader(BinaryBsaHeader header, BsaReader reader)
{
    private readonly record struct Folder(ulong FileBlockOffset, uint FileCount);

    public async ValueTask<BsaArchive> ReadBsaArchive()
    {
        reader.Seek(header.RecordOffset);
        var folders = new List<Folder>((int)header.FolderCount);
        long fileCount = 0;
        for (int i = 0; i < header.FolderCount; i++)
        {
            _ = await reader.ReadUInt64().ConfigureAwait(false); // Folder hash; lookup uses the name tables.
            uint count = await reader.ReadUInt32().ConfigureAwait(false);
            if (header.Version == BsaVersion.SkyrimSpecialEdition)
            {
                _ = await reader.ReadUInt32().ConfigureAwait(false);
            }
            // 105 offsets are 64-bit; keep them rather than truncating.
            ulong offset = header.Version == BsaVersion.SkyrimSpecialEdition
                ? await reader.ReadUInt64().ConfigureAwait(false)
                : await reader.ReadUInt32().ConfigureAwait(false);
            if (offset < header.TotalFileNameLength || offset > long.MaxValue)
            {
                throw new InvalidDataException("Invalid folder offset.");
            }
            fileCount += count;
            if (fileCount > header.FileCount)
            {
                throw new InvalidDataException("Folder file counts exceed the header count.");
            }
            // Stored offsets include the filename table length.
            folders.Add(new Folder(offset - header.TotalFileNameLength, count));
        }
        if (fileCount != header.FileCount)
        {
            throw new InvalidDataException("Folder file counts do not match the header.");
        }

        // Filename order follows the physical file blocks, not hash order.
        folders.Sort(static (a, b) => a.FileBlockOffset.CompareTo(b.FileBlockOffset));
        var files = new List<(string Folder, BsaFileRecord File)>((int)header.FileCount);
        foreach (var folder in folders)
        {
            if ((long)folder.FileBlockOffset < reader.Position)
            {
                throw new InvalidDataException("Overlapping folder blocks.");
            }
            reader.Seek((long)folder.FileBlockOffset);
            string name = await reader.ReadBzString().ConfigureAwait(false);
            for (int j = 0; j < folder.FileCount; j++)
            {
                files.Add((name, new BsaFileRecord(header, await reader.ReadUInt64().ConfigureAwait(false),
                    await reader.ReadUInt32().ConfigureAwait(false), await reader.ReadUInt32().ConfigureAwait(false))));
            }
        }
        var names = await reader.ReadFileNames(header.TotalFileNameLength, header.FileCount).ConfigureAwait(false);
        var entries = new List<BsaEntry>(files.Count);
        for (int i = 0; i < files.Count; i++)
        {
            var (folder, file) = files[i];
            entries.Add(new BsaEntry(string.IsNullOrEmpty(folder) ? names[i] : $"{folder}\\{names[i]}", file));
        }
        return new BsaArchive(header, entries);
    }
}
