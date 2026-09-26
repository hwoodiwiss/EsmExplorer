using Spectre.Console;

namespace BsaParser.Cli;

internal sealed class ExtractionApplication(IAnsiConsole console, bool terminalInteractive)
{
    private bool CanInteract(bool nonInteractive) => !nonInteractive && terminalInteractive && console.Profile.Capabilities.Interactive;

    public Task<int> UnpackAsync(string input, string output, bool nonInteractive, bool overwrite, CancellationToken token) =>
        RunAsync([input], output, CanInteract(nonInteractive), overwrite, token);

    public async Task<int> UnpackDirectoryAsync(string input, string output, string[] names, bool all,
        bool nonInteractive, bool overwrite, CancellationToken token)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            var candidates = Directory.EnumerateFiles(input)
                .Where(static path => Path.GetExtension(path).Equals(".bsa", StringComparison.OrdinalIgnoreCase))
                .Order(StringComparer.OrdinalIgnoreCase).ToArray();
            if (candidates.Length == 0) throw new IOException($"No BSA files found in {input}.");
            bool interactive = CanInteract(nonInteractive);
            string[] selected;
            if (all) selected = candidates;
            else if (names.Length > 0)
            {
                selected = names.Select(name => candidates.FirstOrDefault(path => string.Equals(Path.GetFileName(path), name, StringComparison.OrdinalIgnoreCase))
                    ?? throw new IOException($"BSA not found in input directory: {name}")).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            }
            else if (interactive)
            {
                selected = (await console.PromptAsync(new MultiSelectionPrompt<string>()
                    .Title("Select [green]BSA archives[/] to unpack")
                    .Required().PageSize(15).UseConverter(static path => Markup.Escape(Path.GetFileName(path)))
                    .AddChoices(candidates), token).ConfigureAwait(false)).ToArray();
            }
            else
            {
                console.WriteLine("Non-interactive mode requires --all or one or more --archive <filename> options.");
                return 2;
            }
            return await RunAsync(selected, output, interactive, overwrite, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { console.WriteLine("Cancelled."); return 130; }
        catch (Exception ex) when (IsExpected(ex)) { WriteError(ex.Message); return 1; }
    }

    private async Task<int> RunAsync(string[] archives, string output, bool interactive, bool overwrite, CancellationToken token)
    {
        int failures = 0;
        int fileCount = 0;
        try
        {
            foreach (string archive in archives)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    int count;
                    if (interactive)
                    {
                        count = await console.Progress().AutoClear(true).StartAsync(async context =>
                        {
                            var task = context.AddTask(Markup.Escape(Path.GetFileName(archive)));
                            return await ArchiveExtractor.ExtractAsync(archive, output, overwrite, (done, total) =>
                            {
                                task.MaxValue = Math.Max(total, 1);
                                task.Value = total == 0 ? 1 : done;
                            }, token).ConfigureAwait(false);
                        }).ConfigureAwait(false);
                    }
                    else count = await ArchiveExtractor.ExtractAsync(archive, output, overwrite, null, token).ConfigureAwait(false);
                    fileCount += count;
                    console.WriteLine($"Unpacked {Path.GetFileName(archive)}: {count} files -> {output}");
                }
                catch (Exception ex) when (IsExpected(ex)) { failures++; WriteError($"{archive}: {ex.Message}"); }
            }
            console.WriteLine($"Finished: {archives.Length - failures} archive(s) unpacked, {fileCount} file(s), {failures} failed.");
            return failures == 0 ? 0 : 1;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { console.WriteLine("Cancelled."); return 130; }
    }

    private static bool IsExpected(Exception ex) => ex is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException or ArgumentException;
    private void WriteError(string message) => console.MarkupLine($"[red]Error:[/] {Markup.Escape(message)}");
}
