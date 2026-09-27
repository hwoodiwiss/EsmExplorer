using BsaParser.Cli;
using Spectre.Console;

return await new BsaRootCommand(new ExtractionApplication(AnsiConsole.Console, TerminalMode.Detect())).Parse(args).InvokeAsync();
