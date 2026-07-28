using EsmParser.Core.Format;
using EsmParser.Core.IO;
using EsmParser.Core.Navigation;
using EsmParser.Core.Tests.TestData;

namespace EsmParser.Core.Tests;

public sealed class FormIdIndexTests
{
    [Test]
    public async Task Indexes_Records_At_All_Depths()
    {
        MemoryDataSource source = new PluginBuilder()
            .AddTopGroup("GMST", g => g.AddRecord("GMST", 0x100))
            .AddTopGroup("CELL", g => g
                .AddRecord("CELL", 0x200)
                .AddGroup(GroupType.CellChildren, 0x200, cell => cell
                    .AddGroup(GroupType.CellTemporaryChildren, 0x200, temp => temp
                        .AddRecord("REFR", 0x201))))
            .BuildSource();

        await using PluginFile plugin = (await PluginFile.OpenAsync(source)).ShouldSucceed();
        FormIdIndex index = (await FormIdIndex.BuildAsync(plugin)).ShouldSucceed();

        await Assert.That(index.Count).IsEqualTo(3);

        if (await plugin.FindRecordAsync(new FormId(0x201)) is not RecordFound found)
        {
            throw new InvalidOperationException("Expected the record to be found.");
        }

        await Assert.That(index.FindOffset(new FormId(0x201))).IsEqualTo(found.Record.Offset);
        await Assert.That(index.FindOffset(new FormId(0x100))).IsNotNull();
        await Assert.That(index.FindOffset(new FormId(0xDEAD))).IsNull();
    }

    [Test]
    public async Task Reports_Monotonic_Progress()
    {
        MemoryDataSource source = new PluginBuilder()
            .AddTopGroup("GMST", g => g.AddRecord("GMST", 0x100).AddRecord("GMST", 0x101))
            .BuildSource();

        await using PluginFile plugin = (await PluginFile.OpenAsync(source)).ShouldSucceed();
        var reports = new List<double>();
        var progress = new SynchronousProgress(reports.Add);
        (await FormIdIndex.BuildAsync(plugin, progress)).ShouldSucceed();

        await Assert.That(reports.Count).IsGreaterThan(0);
        await Assert.That(reports[^1]).IsEqualTo(1.0);
    }

    private sealed class SynchronousProgress(Action<double> handler) : IProgress<double>
    {
        public void Report(double value) => handler(value);
    }
}

public sealed class GetPathToOffsetTests
{
    [Test]
    public async Task Returns_The_Ancestor_Chain_For_A_Nested_Record()
    {
        MemoryDataSource source = new PluginBuilder()
            .AddTopGroup("GMST", g => g.AddRecord("GMST", 0x100))
            .AddTopGroup("CELL", g => g
                .AddRecord("CELL", 0x200)
                .AddGroup(GroupType.CellChildren, 0x200, cell => cell
                    .AddGroup(GroupType.CellTemporaryChildren, 0x200, temp => temp
                        .AddRecord("REFR", 0x201)
                        .AddRecord("REFR", 0x202))))
            .BuildSource();

        await using PluginFile plugin = (await PluginFile.OpenAsync(source)).ShouldSucceed();
        if (await plugin.FindRecordAsync(new FormId(0x202)) is not RecordFound found)
        {
            throw new InvalidOperationException("Expected the record to be found.");
        }

        IReadOnlyList<PluginNode> path = (await plugin.GetPathToOffsetAsync(found.Record.Offset)).ShouldSucceed();

        await Assert.That(path.Count).IsEqualTo(4);
        await Assert.That(((GroupNode)path[0]).Header.LabelAsSignature).IsEqualTo(Signature.FromString("CELL"));
        await Assert.That(((GroupNode)path[1]).Header.GroupType).IsEqualTo(GroupType.CellChildren);
        await Assert.That(((GroupNode)path[2]).Header.GroupType).IsEqualTo(GroupType.CellTemporaryChildren);
        await Assert.That(((RecordNode)path[3]).Header.FormId).IsEqualTo(new FormId(0x202));
        await Assert.That(path[3].Offset).IsEqualTo(found.Record.Offset);
        await Assert.That(path[3].Depth).IsEqualTo(3);
    }

    [Test]
    public async Task Offset_Zero_Is_The_Header_Record()
    {
        MemoryDataSource source = new PluginBuilder().AddRecord("MISC", 0x10).BuildSource();
        await using PluginFile plugin = (await PluginFile.OpenAsync(source)).ShouldSucceed();

        IReadOnlyList<PluginNode> path = (await plugin.GetPathToOffsetAsync(0)).ShouldSucceed();
        await Assert.That(path.Count).IsEqualTo(1);
        await Assert.That(path[0]).IsEqualTo(plugin.HeaderRecord);
    }

    [Test]
    public async Task A_Misaligned_Offset_Is_Invalid()
    {
        MemoryDataSource source = new PluginBuilder()
            .AddRecord("MISC", 0x10, f => f.AddZString("EDID", "Misc"))
            .BuildSource();

        await using PluginFile plugin = (await PluginFile.OpenAsync(source)).ShouldSucceed();
        var record = (RecordNode)(await plugin.GetTopLevelNodesAsync()).ShouldSucceed()[0];

        // Points inside the record's data, not at a node header.
        ParseError error = (await plugin.GetPathToOffsetAsync(record.DataOffset)).ShouldFail();
        await Assert.That(error.Kind).IsEqualTo(ParseErrorKind.StructureInvalid);
    }
}

public sealed class PluginWorkspaceTests
{
    private static MemoryDataSource BuildMaster() => new PluginBuilder()
        .WithHeaderFlags(RecordFlags.Master)
        .AddTopGroup("KYWD", g => g
            .AddRecord("KYWD", 0x000ABC, f => f.AddZString("EDID", "MasterKeyword")))
        .BuildSource("Master.esm");

    private static MemoryDataSource BuildMod() => new PluginBuilder()
        .WithMaster("Master.esm")
        .AddTopGroup("WEAP", g => g
            .AddRecord("WEAP", 0x01000123, f => f
                .AddZString("EDID", "ModWeapon")
                .AddUInt32("KWDA", 0x000ABC))) // references the master's keyword
        .BuildSource("Mod.esp");

    [Test]
    public async Task Adding_The_Same_File_Name_Twice_Returns_The_Existing_Plugin()
    {
        await using var workspace = new PluginWorkspace();
        PluginFile first = (await workspace.AddAsync(BuildMaster())).ShouldSucceed();
        PluginFile second = (await workspace.AddAsync(BuildMaster())).ShouldSucceed();

        await Assert.That(ReferenceEquals(first, second)).IsTrue();
        await Assert.That(workspace.Plugins.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Resolves_A_Reference_Into_A_Loaded_Master()
    {
        await using var workspace = new PluginWorkspace();
        PluginFile master = (await workspace.AddAsync(BuildMaster())).ShouldSucceed();
        PluginFile mod = (await workspace.AddAsync(BuildMod())).ShouldSucceed();

        // As seen from the mod, 0x0000_0ABC has master index 0 => Master.esm.
        ResolveResult result = await workspace.ResolveAsync(mod, new FormId(0x000ABC));
        if (result is not ResolvedRecord resolved)
        {
            throw new InvalidOperationException($"Expected a resolved record, got {result}");
        }

        await Assert.That(ReferenceEquals(resolved.Plugin, master)).IsTrue();
        await Assert.That(resolved.Record.Header.Signature).IsEqualTo(Signature.FromString("KYWD"));
        await Assert.That(resolved.Record.Header.FormId).IsEqualTo(new FormId(0x000ABC));
        await Assert.That(resolved.Path.Count).IsEqualTo(2); // top group -> record
    }

    [Test]
    public async Task Resolves_A_Self_Reference_Within_The_Mod()
    {
        await using var workspace = new PluginWorkspace();
        (await workspace.AddAsync(BuildMaster())).ShouldSucceed();
        PluginFile mod = (await workspace.AddAsync(BuildMod())).ShouldSucceed();

        // Top byte 1 == mod.Masters.Count => defined by the mod itself.
        ResolveResult result = await workspace.ResolveAsync(mod, new FormId(0x01000123));
        if (result is not ResolvedRecord resolved)
        {
            throw new InvalidOperationException($"Expected a resolved record, got {result}");
        }

        await Assert.That(ReferenceEquals(resolved.Plugin, mod)).IsTrue();
        await Assert.That(resolved.Record.Header.FormId).IsEqualTo(new FormId(0x01000123));
    }

    [Test]
    public async Task Reports_A_Missing_Master()
    {
        await using var workspace = new PluginWorkspace();
        PluginFile mod = (await workspace.AddAsync(BuildMod())).ShouldSucceed();

        ResolveResult result = await workspace.ResolveAsync(mod, new FormId(0x000ABC));
        if (result is not MissingMaster missing)
        {
            throw new InvalidOperationException($"Expected a missing master, got {result}");
        }

        await Assert.That(missing.PluginName).IsEqualTo("Master.esm");
        await Assert.That(workspace.GetMissingMasters(mod)).Contains("Master.esm");
    }

    [Test]
    public async Task Reports_A_Clean_Miss()
    {
        await using var workspace = new PluginWorkspace();
        PluginFile master = (await workspace.AddAsync(BuildMaster())).ShouldSucceed();

        ResolveResult result = await workspace.ResolveAsync(master, new FormId(0x000FFF));
        await Assert.That(result is RecordNotFound).IsTrue();

        ResolveResult nullResult = await workspace.ResolveAsync(master, new FormId(0));
        await Assert.That(nullResult is RecordNotFound).IsTrue();
    }

    [Test]
    public async Task Caches_The_Index_Per_Plugin()
    {
        await using var workspace = new PluginWorkspace();
        PluginFile master = (await workspace.AddAsync(BuildMaster())).ShouldSucceed();

        await Assert.That(workspace.HasIndex(master)).IsFalse();
        FormIdIndex first = (await workspace.GetIndexAsync(master)).ShouldSucceed();
        FormIdIndex second = (await workspace.GetIndexAsync(master)).ShouldSucceed();
        await Assert.That(ReferenceEquals(first, second)).IsTrue();
        await Assert.That(workspace.HasIndex(master)).IsTrue();
    }

    [Test]
    public async Task FormKeys_Canonicalize_Across_Files()
    {
        await using var workspace = new PluginWorkspace();
        PluginFile master = (await workspace.AddAsync(BuildMaster())).ShouldSucceed();
        PluginFile mod = (await workspace.AddAsync(BuildMod())).ShouldSucceed();

        // The same form seen from either side yields the same key.
        FormKey fromMod = PluginWorkspace.GetFormKey(mod, new FormId(0x000ABC));
        FormKey fromMaster = PluginWorkspace.GetFormKey(master, new FormId(0x000ABC));
        await Assert.That(fromMod).IsEqualTo(new FormKey("Master.esm", 0xABC));
        await Assert.That(fromMaster).IsEqualTo(new FormKey("Master.esm", 0xABC));

        FormKey selfKey = PluginWorkspace.GetFormKey(mod, new FormId(0x01000123));
        await Assert.That(selfKey).IsEqualTo(new FormKey("Mod.esp", 0x123));
    }
}
