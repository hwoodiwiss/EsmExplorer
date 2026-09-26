namespace BethesdaArchiveParser.Core.Reader;

internal sealed class BsaContentReader(BinaryBsaHeader header, BsaReader reader)
{
    public async ValueTask<BsaArchive> ReadBsaArchive()
    {
        reader.Seek(header.RecordOffset);
        var folders = new List<BsaFolderRecord>((int)header.FolderCount);
        long fileCount = 0;
        for (int i = 0; i < header.FolderCount; i++)
        {
            ulong hash = await reader.ReadUInt64().ConfigureAwait(false);
            uint count = await reader.ReadUInt32().ConfigureAwait(false);
            if (header.Version == 105)
            {
                _ = await reader.ReadUInt32().ConfigureAwait(false);
            }
            ulong offset = header.Version == 105
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
            folders.Add(new BsaFolderRecord(header, hash, count, offset, null, []));
        }
        if (fileCount != header.FileCount)
        {
            throw new InvalidDataException("Folder file counts do not match the header.");
        }

        // Filename order follows the physical file blocks, not hash order.
        folders.Sort(static (a, b) => a.Offset.CompareTo(b.Offset));
        var blocks = new List<BsaFileBlockRecord>(folders.Count);
        for (int i = 0; i < folders.Count; i++)
        {
            var folder = folders[i];
            if ((long)folder.FileBlockOffset < reader.Position)
            {
                throw new InvalidDataException("Overlapping folder blocks.");
            }
            reader.Seek((long)folder.FileBlockOffset);
            string name = await reader.ReadBzString().ConfigureAwait(false);
            var files = new List<BsaFileRecord>((int)folder.FileCount);
            for (int j = 0; j < folder.FileCount; j++)
            {
                folder.FileOffsets.Add(reader.Position);
                files.Add(new BsaFileRecord(header, await reader.ReadUInt64().ConfigureAwait(false),
                    await reader.ReadUInt32().ConfigureAwait(false), await reader.ReadUInt32().ConfigureAwait(false)));
            }
            folders[i] = folder with { Name = name };
            blocks.Add(new BsaFileBlockRecord(name, files));
        }
        var names = await reader.ReadFileNames(header.TotalFileNameLength, header.FileCount).ConfigureAwait(false);
        return new BsaArchive(header, folders, blocks, names);
    }
}
