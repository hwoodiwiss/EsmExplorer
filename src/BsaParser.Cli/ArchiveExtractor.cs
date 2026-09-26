using BethesdaArchiveParser.Core.Reader;

namespace BsaParser.Cli;

internal static class ArchiveExtractor
{
    public static async Task<int> ExtractAsync(string archivePath, string outputDirectory, bool overwrite,
        Action<int, int>? progress, CancellationToken cancellationToken)
    {
        await using var source = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            64 * 1024, FileOptions.Asynchronous | FileOptions.RandomAccess);
        var reader = new BsaArchiveReader(source);
        var archive = await reader.ReadBsaArchiveAsync(cancellationToken).ConfigureAwait(false);
        string root = Path.GetFullPath(outputDirectory);
        // Validate the whole plan before writing anything, including platform-specific collisions.
        var paths = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var targets = new List<string>(archive.Entries.Count);
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string target = GetDestination(root, entry.Path);
            if (!paths.Add(target))
            {
                throw new IOException($"Multiple entries map to {target}.");
            }
            if (string.Equals(target, Path.GetFullPath(archivePath), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            {
                throw new IOException("An entry would overwrite the input archive.");
            }
            CheckExistingPath(target);
            if (File.Exists(target) && !overwrite)
            {
                throw new IOException($"File already exists: {target}. Use --overwrite to replace it.");
            }
            targets.Add(target);
        }
        int completed = 0;
        progress?.Invoke(0, targets.Count);
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string target = targets[completed];
            string parent = Path.GetDirectoryName(target)!;
            CheckExistingPath(target);
            Directory.CreateDirectory(parent);
            string temporary = Path.Combine(parent, $".bsa-{Guid.NewGuid():N}.tmp");
            try
            {
                await using (var destination = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    64 * 1024, FileOptions.Asynchronous))
                {
                    await reader.CopyBsaFileToAsync(entry.File, destination, cancellationToken).ConfigureAwait(false);
                }
                cancellationToken.ThrowIfCancellationRequested();
                File.Move(temporary, target, overwrite);
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
            completed++;
            progress?.Invoke(completed, targets.Count);
        }
        return completed;
    }

    internal static string GetDestination(string root, string entryPath)
    {
        // Use both archive separators on every host; reject drive/ADS syntax and Windows aliases too.
        string[] parts = entryPath.Replace('\\', '/').Split('/');
        if (parts.Any(static part => part.Length == 0 || part is "." or ".." || part.EndsWith('.') || part.EndsWith(' ')
            || part.Any(static c => c < 32 || "<>:\"|?*".Contains(c)) || IsDeviceName(part)))
        {
            throw new InvalidDataException($"Unsafe archive path: {entryPath}");
        }
        return Path.Combine([Path.GetFullPath(root), .. parts]);
    }

    private static bool IsDeviceName(string part)
    {
        string name = part.Split('.')[0];
        return name.Equals("CON", StringComparison.OrdinalIgnoreCase) || name.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            || name.Equals("AUX", StringComparison.OrdinalIgnoreCase) || name.Equals("NUL", StringComparison.OrdinalIgnoreCase)
            || (name.Length == 4 && name[3] is >= '1' and <= '9'
                && (name.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || name.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)));
    }

    private static void CheckExistingPath(string path)
    {
        for (string? current = path; current is not null; current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new IOException($"Extraction through symbolic links/reparse points is not supported: {current}");
                }
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }
}
