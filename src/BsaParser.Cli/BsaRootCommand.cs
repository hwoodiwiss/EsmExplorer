using System.CommandLine;

namespace BsaParser.Cli;

internal sealed class BsaRootCommand : RootCommand
{
    public BsaRootCommand(ExtractionApplication application) : base("Unpack Bethesda BSA archives (versions 103, 104 and 105).")
    {
        Subcommands.Add(new UnpackCommand(application));
        Subcommands.Add(new UnpackDirectoryCommand(application));
    }
}

internal sealed class UnpackCommand : Command
{
    public UnpackCommand(ExtractionApplication application) : base("unpack", "Unpack one BSA, preserving its directory structure.")
    {
        var input = new InputFileArgument();
        var output = new OutputDirectoryOption();
        var nonInteractive = new NonInteractiveOption();
        var overwrite = new OverwriteOption();
        Arguments.Add(input);
        Options.Add(output);
        Options.Add(nonInteractive);
        Options.Add(overwrite);
        SetAction((result, token) => application.UnpackAsync(result.GetRequiredValue(input), result.GetRequiredValue(output),
            result.GetValue(nonInteractive), result.GetValue(overwrite), token));
    }
}

internal sealed class UnpackDirectoryCommand : Command
{
    public UnpackDirectoryCommand(ExtractionApplication application) : base("unpack-directory", "Select BSAs and unpack into a shared output directory, preserving archive paths.")
    {
        var input = new InputDirectoryArgument();
        var output = new OutputDirectoryOption();
        var nonInteractive = new NonInteractiveOption();
        var overwrite = new OverwriteOption();
        var all = new AllArchivesOption();
        var archives = new ArchiveNamesOption();
        Arguments.Add(input);
        Options.Add(output);
        Options.Add(nonInteractive);
        Options.Add(overwrite);
        Options.Add(all);
        Options.Add(archives);
        Validators.Add(result =>
        {
            if (result.GetValue(all) && result.GetValue(archives) is { Length: > 0 })
                result.AddError("--all and --archive cannot be combined.");
        });
        SetAction((result, token) => application.UnpackDirectoryAsync(result.GetRequiredValue(input), result.GetRequiredValue(output),
            result.GetValue(archives) ?? [], result.GetValue(all), result.GetValue(nonInteractive), result.GetValue(overwrite), token));
    }
}
