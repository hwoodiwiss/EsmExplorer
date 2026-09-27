using System.Diagnostics;

namespace BethesdaArchiveParser.Core.Tests;

public sealed class FixtureManifestTests
{
    [Test]
    public async Task PowerShell_manifest_hashes_originals_and_preserves_existing_output()
    {
        string temporary = Path.Combine(Path.GetTempPath(), $"bsa-manifest-{Guid.NewGuid():N}");
        string originals = Path.Combine(temporary, "original files");
        Directory.CreateDirectory(Path.Combine(originals, "meshes"));
        try
        {
            await File.WriteAllTextAsync(Path.Combine(originals, "meshes", "test.bin"), "abc");
            string output = Path.Combine(temporary, "manifest.tsv");
            await Assert.That(await RunAsync(originals, output)).IsEqualTo(0);
            const string Expected = "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD\tmeshes/test.bin\n";
            await Assert.That(await File.ReadAllTextAsync(output)).IsEqualTo(Expected);
            await Assert.That((await File.ReadAllBytesAsync(output))[0]).IsEqualTo((byte)'B');
            await Assert.That(await RunAsync(originals, output)).IsNotEqualTo(0);
            await Assert.That(await File.ReadAllTextAsync(output)).IsEqualTo(Expected);
            await Assert.That(await RunAsync(originals, Path.Combine(originals, "inside.tsv"))).IsNotEqualTo(0);
            await Assert.That(File.Exists(Path.Combine(originals, "inside.tsv"))).IsFalse();
        }
        finally
        {
            Directory.Delete(temporary, recursive: true);
        }
    }

    [Test]
    public async Task PowerShell_manifest_rejects_empty_input_and_missing_output_parent()
    {
        string temporary = Path.Combine(Path.GetTempPath(), $"bsa-manifest-{Guid.NewGuid():N}");
        string originals = Path.Combine(temporary, "originals");
        Directory.CreateDirectory(originals);
        try
        {
            string output = Path.Combine(temporary, "manifest.tsv");
            await Assert.That(await RunAsync(originals, output)).IsNotEqualTo(0);
            await Assert.That(File.Exists(output)).IsFalse();
            await File.WriteAllTextAsync(Path.Combine(originals, "test.bin"), "abc");
            await Assert.That(await RunAsync(originals, Path.Combine(temporary, "missing", "manifest.tsv"))).IsNotEqualTo(0);
        }
        finally
        {
            Directory.Delete(temporary, recursive: true);
        }
    }

    private static async Task<int> RunAsync(string originals, string output)
    {
        var start = new ProcessStartInfo("pwsh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-File",
            Path.Combine(AppContext.BaseDirectory, "Create-BsaFixtureManifest.ps1"),
            "-OriginalFilesRoot", originals, "-OutputPath", output })
        {
            start.ArgumentList.Add(argument);
        }
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start PowerShell 7 (pwsh).");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
        Console.WriteLine(await stdout);
        Console.WriteLine(await stderr);
        return process.ExitCode;
    }
}
