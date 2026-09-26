using System.CommandLine;

namespace BsaParser.Cli;

internal sealed class InputFileArgument : Argument<string>
{
    public InputFileArgument() : base("input") => Description = "BSA file to unpack.";
}

internal sealed class InputDirectoryArgument : Argument<string>
{
    public InputDirectoryArgument() : base("input-directory") => Description = "Directory containing BSA files (top level only).";
}

internal sealed class OutputDirectoryOption : Option<string>
{
    public OutputDirectoryOption() : base("--output", "-o")
    {
        Description = "Destination directory.";
        Required = true;
    }
}

internal sealed class NonInteractiveOption : Option<bool>
{
    public NonInteractiveOption() : base("--non-interactive") => Description = "Disable prompts and live progress (also automatic for redirected input/output and CI).";
}

internal sealed class OverwriteOption : Option<bool>
{
    public OverwriteOption() : base("--overwrite") => Description = "Replace existing files after each entry has extracted successfully.";
}

internal sealed class AllArchivesOption : Option<bool>
{
    public AllArchivesOption() : base("--all") => Description = "Unpack every BSA in the input directory.";
}

internal sealed class ArchiveNamesOption : Option<string[]>
{
    public ArchiveNamesOption() : base("--archive", "-a")
    {
        Description = "Select a BSA by filename. Repeat to select multiple archives.";
        Arity = ArgumentArity.OneOrMore;
        AllowMultipleArgumentsPerToken = false;
    }
}
