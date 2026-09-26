# BSA CLI guidance

## User requirements

- Use System.CommandLine for parsing and Spectre.Console for rich presentation.
- Define commands, arguments, options, and similar application-defined command-line
  objects as derived types. Instantiate those types rather than the generic/base
  System.CommandLine types directly; see `BsaRootCommand.cs` and `Inputs.cs`.
- Preserve NativeAOT compatibility. Avoid reflection-based discovery, dynamic code
  generation, and dependencies that require them.
- Support both single-archive extraction and selection of multiple archives from an
  input directory.
- Batch extraction writes directly into **one shared output directory**, preserving
  paths inside each archive. Do not introduce archive-named output subdirectories.
- Keep `--non-interactive` and automatic non-interactive detection. Redirected
  stdin/stdout, CI, and dumb terminals must not trigger prompts or live progress.

## Current behaviour to preserve

- `TerminalMode` handles environment detection; `ExtractionApplication` coordinates
  selection/reporting; `ArchiveExtractor` performs filesystem extraction.
- Non-interactive batch mode requires `--all` or repeated `--archive` selections.
- Archive selection is top-level and extension matching is case-insensitive.
  `--all` uses case-insensitive filename order; explicit selections use supplied order.
  Do not infer game load order from this ordering.
- Existing paths require `--overwrite`, including collisions between archives. With
  overwrite enabled, later archives win. Keep processing order deterministic.
- Validate output paths and stream each entry to a temporary file beside its target
  before moving it into place. Failed/cancelled entries preserve existing targets;
  completed entries remain if a later entry fails.
- Continue after individual archive failures and return a failing exit code. Preserve
  cancellation propagation and the documented exit codes in `README.md`.
- Treat filenames and exception messages as literal text; escape them when rendering
  Spectre markup. Keep console interaction injectable for testing.

## Verification

Update `tests/BsaParser.Cli.Tests` for changed command parsing, selection, extraction,
collision, cancellation, and failure behaviour. Use Spectre's test console for prompts.

```shell
dotnet test --project tests/BsaParser.Cli.Tests/BsaParser.Cli.Tests.csproj
dotnet publish src/BsaParser.Cli/BsaParser.Cli.csproj -c Release -r win-x64
```

Run NativeAOT publish when changing dependencies, startup/binding, or AOT-sensitive
code. It requires the target platform's toolchain; report limitations if unavailable.
