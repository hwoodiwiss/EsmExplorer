namespace BsaParser.Cli;

internal static class TerminalMode
{
    public static bool Detect() => IsInteractive(Console.IsInputRedirected, Console.IsOutputRedirected,
        Environment.GetEnvironmentVariable("CI"), Environment.GetEnvironmentVariable("TERM"));

    internal static bool IsInteractive(bool inputRedirected, bool outputRedirected, string? ci, string? term) =>
        !inputRedirected && !outputRedirected
        && (string.IsNullOrEmpty(ci) || ci.Equals("false", StringComparison.OrdinalIgnoreCase) || ci == "0")
        && !string.Equals(term, "dumb", StringComparison.OrdinalIgnoreCase);
}
