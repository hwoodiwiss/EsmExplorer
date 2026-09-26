using BethesdaArchiveParser.Core.Tests;
using Spectre.Console.Testing;

namespace BsaParser.Cli.Tests;

public sealed class CliTests
{
    [Test]
    public async Task Unpacks_one_archive_and_requires_explicit_overwrite()
    {
        using var workspace = new Workspace();
        string source = workspace.AddArchive("test.bsa");
        using var console = new TestConsole();
        var root = new BsaRootCommand(new ExtractionApplication(console, false));
        string[] args = ["unpack", source, "-o", workspace.Output, "--non-interactive"];
        await Assert.That(await root.Parse(args).InvokeAsync()).IsEqualTo(0);
        string target = Path.Combine(workspace.Output, "meshes", "first.bin");
        await Assert.That(await File.ReadAllTextAsync(target)).IsEqualTo("first payload");
        await File.WriteAllTextAsync(target, "keep me");
        await Assert.That(await root.Parse(args).InvokeAsync()).IsEqualTo(1);
        await Assert.That(await File.ReadAllTextAsync(target)).IsEqualTo("keep me");
        await Assert.That(await root.Parse([.. args, "--overwrite"]).InvokeAsync()).IsEqualTo(0);
        await Assert.That(await File.ReadAllTextAsync(target)).IsEqualTo("first payload");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Batch_selection_merges_into_shared_output(bool all)
    {
        using var workspace = new Workspace();
        workspace.AddArchive("one.bsa", BsaFixture.Create(folder: "meshes"));
        workspace.AddArchive("two.BSA", BsaFixture.Create(folder: "textures"));
        workspace.AddArchive("three.bsa", BsaFixture.Create(folder: "sound"));
        using var console = new TestConsole();
        var root = new BsaRootCommand(new ExtractionApplication(console, false));
        string[] selection = all ? ["--all"] : ["--archive", "one.bsa", "--archive", "two.BSA"];
        await Assert.That(await root.Parse(["unpack-directory", workspace.Input, "-o", workspace.Output, .. selection]).InvokeAsync()).IsEqualTo(0);
        await Assert.That(File.Exists(Path.Combine(workspace.Output, "meshes", "first.bin"))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(workspace.Output, "textures", "first.bin"))).IsTrue();
        await Assert.That(Directory.Exists(Path.Combine(workspace.Output, "sound"))).IsEqualTo(all);
        await Assert.That(Directory.Exists(Path.Combine(workspace.Output, "one"))).IsFalse();
        await Assert.That(Directory.Exists(Path.Combine(workspace.Output, "two"))).IsFalse();
    }

    [Test]
    public async Task Noninteractive_requires_selection_and_rejects_conflicting_options()
    {
        using var workspace = new Workspace();
        workspace.AddArchive("one.bsa");
        using var console = new TestConsole();
        console.Profile.Capabilities.Interactive = true;
        var root = new BsaRootCommand(new ExtractionApplication(console, true));
        string[] args = ["unpack-directory", workspace.Input, "-o", workspace.Output, "--non-interactive"];
        await Assert.That(await root.Parse(args).InvokeAsync()).IsEqualTo(2);
        await Assert.That(console.Output).Contains("requires --all");
        await Assert.That(root.Parse([.. args, "--all", "--archive", "one.bsa"]).Errors.Count > 0).IsTrue();
        await Assert.That(root.Parse(["unpack", "one.bsa"]).Errors.Count > 0).IsTrue();
        await Assert.That(Directory.Exists(workspace.Output)).IsFalse();
    }

    [Test]
    public async Task Interactive_selection_unpacks_only_checked_archives()
    {
        using var workspace = new Workspace();
        workspace.AddArchive("one.bsa");
        workspace.AddArchive("two.bsa", BsaFixture.Create(folder: "textures"));
        using var console = new TestConsole();
        console.Profile.Capabilities.Interactive = true;
        console.Input.PushKey(ConsoleKey.Spacebar);
        console.Input.PushKey(ConsoleKey.Enter);
        var root = new BsaRootCommand(new ExtractionApplication(console, true));
        await Assert.That(await root.Parse(["unpack-directory", workspace.Input, "-o", workspace.Output]).InvokeAsync()).IsEqualTo(0);
        await Assert.That(File.Exists(Path.Combine(workspace.Output, "meshes", "first.bin"))).IsTrue();
        await Assert.That(Directory.Exists(Path.Combine(workspace.Output, "textures"))).IsFalse();
        await Assert.That(Directory.Exists(Path.Combine(workspace.Output, "one"))).IsFalse();
    }

    [Test]
    [Arguments(true, false, null, null, false)]
    [Arguments(false, true, null, null, false)]
    [Arguments(false, false, "true", null, false)]
    [Arguments(false, false, "1", null, false)]
    [Arguments(false, false, "false", "dumb", false)]
    [Arguments(false, false, "false", "xterm", true)]
    [Arguments(false, false, null, null, true)]
    public async Task Detects_noninteractive_environments(bool input, bool output, string? ci, string? term, bool expected) =>
        await Assert.That(TerminalMode.IsInteractive(input, output, ci, term)).IsEqualTo(expected);

    [Test]
    [Arguments("../escape.bin")]
    [Arguments("..\\escape.bin")]
    [Arguments("/absolute.bin")]
    [Arguments("C:\\escape.bin")]
    [Arguments("file:stream")]
    [Arguments("CON.txt")]
    [Arguments("file. ")]
    public async Task Rejects_unsafe_archive_paths_before_writing(string name)
    {
        using var workspace = new Workspace();
        string source = workspace.AddArchive("unsafe.bsa", BsaFixture.Create(folder: "", firstName: name));
        await Assert.That(async () => await ArchiveExtractor.ExtractAsync(source, workspace.Output, false, null, default)).Throws<InvalidDataException>();
        await Assert.That(Directory.Exists(workspace.Output)).IsFalse();
    }

    [Test]
    public async Task Continues_batch_after_bad_archive_and_returns_failure()
    {
        using var workspace = new Workspace();
        workspace.AddArchive("bad.bsa", new byte[40]);
        workspace.AddArchive("good.bsa");
        using var console = new TestConsole();
        var root = new BsaRootCommand(new ExtractionApplication(console, false));
        await Assert.That(await root.Parse(["unpack-directory", workspace.Input, "-o", workspace.Output, "--all"]).InvokeAsync()).IsEqualTo(1);
        await Assert.That(File.Exists(Path.Combine(workspace.Output, "meshes", "first.bin"))).IsTrue();
    }

    [Test]
    public async Task Cancellation_has_distinct_exit_code()
    {
        using var console = new TestConsole();
        using var tokenSource = new CancellationTokenSource();
        tokenSource.Cancel();
        var application = new ExtractionApplication(console, false);
        await Assert.That(await application.UnpackAsync("unused", "unused", true, false, tokenSource.Token)).IsEqualTo(130);
    }

    [Test]
    public async Task Failed_payload_preserves_existing_file_and_removes_temporary_file()
    {
        using var workspace = new Workspace();
        byte[] bytes = BsaFixture.Create(compressed: true);
        using (var stream = new MemoryStream(bytes))
        {
            var file = new BethesdaArchiveParser.Core.Reader.BsaArchiveReader(stream).ReadBsaArchive().Entries[0].File;
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan((int)file.Offset), 1);
        }
        string source = workspace.AddArchive("bad.bsa", bytes);
        string target = Path.Combine(workspace.Output, "meshes", "first.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await File.WriteAllTextAsync(target, "original");
        await Assert.That(async () => await ArchiveExtractor.ExtractAsync(source, workspace.Output, true, null, default)).Throws<InvalidDataException>();
        await Assert.That(await File.ReadAllTextAsync(target)).IsEqualTo("original");
        await Assert.That(Directory.GetFiles(workspace.Output, "*.tmp", SearchOption.AllDirectories).Length).IsEqualTo(0);
    }

    [Test]
    public async Task Missing_archive_selection_does_not_extract_anything()
    {
        using var workspace = new Workspace();
        workspace.AddArchive("one.bsa");
        using var console = new TestConsole();
        var root = new BsaRootCommand(new ExtractionApplication(console, false));
        await Assert.That(await root.Parse(["unpack-directory", workspace.Input, "-o", workspace.Output,
            "--archive", "one.bsa", "--archive", "missing.bsa"]).InvokeAsync()).IsEqualTo(1);
        await Assert.That(Directory.Exists(workspace.Output)).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Shared_output_collisions_require_overwrite(bool overwrite)
    {
        using var workspace = new Workspace();
        workspace.AddArchive("one.bsa", BsaFixture.Create(content: "one"u8.ToArray()));
        workspace.AddArchive("two.bsa", BsaFixture.Create(content: "two"u8.ToArray()));
        using var console = new TestConsole();
        var root = new BsaRootCommand(new ExtractionApplication(console, false));
        string[] flags = overwrite ? ["--overwrite"] : [];
        await Assert.That(await root.Parse(["unpack-directory", workspace.Input, "-o", workspace.Output,
            "--all", .. flags]).InvokeAsync()).IsEqualTo(overwrite ? 0 : 1);
        await Assert.That(await File.ReadAllTextAsync(Path.Combine(workspace.Output, "meshes", "first.bin")))
            .IsEqualTo(overwrite ? "two" : "one");
    }

    [Test]
    public async Task Explicit_selection_order_controls_overwrite_precedence()
    {
        using var workspace = new Workspace();
        workspace.AddArchive("one.bsa", BsaFixture.Create(content: "one"u8.ToArray()));
        workspace.AddArchive("two.bsa", BsaFixture.Create(content: "two"u8.ToArray()));
        using var console = new TestConsole();
        var root = new BsaRootCommand(new ExtractionApplication(console, false));
        await Assert.That(await root.Parse(["unpack-directory", workspace.Input, "-o", workspace.Output,
            "--archive", "two.bsa", "--archive", "one.bsa", "--overwrite"]).InvokeAsync()).IsEqualTo(0);
        await Assert.That(await File.ReadAllTextAsync(Path.Combine(workspace.Output, "meshes", "first.bin"))).IsEqualTo("one");
    }

    private sealed class Workspace : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"bsa-tests-{Guid.NewGuid():N}");
        public string Input => Path.Combine(_root, "input");
        public string Output => Path.Combine(_root, "output");
        public string AddArchive(string name, byte[]? bytes = null)
        {
            Directory.CreateDirectory(Input);
            string path = Path.Combine(Input, name);
            File.WriteAllBytes(path, bytes ?? BsaFixture.Create());
            return path;
        }
        public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    }
}
