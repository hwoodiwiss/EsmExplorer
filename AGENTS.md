# Repository guidance

## Scope and layout

EsmParser contains Bethesda plugin/archive parsers, a Blazor WebAssembly explorer,
and a BSA extraction CLI. See `README.md` for architecture, usage, and fixture setup.
Read the more specific `AGENTS.md` when working in the CLI, archive library, or tests.

- `src/EsmParser.Core`: plugin parsing, navigation, and interpretation.
- `src/BethesdaArchiveParser.Core`: BSA metadata and payload reading.
- `src/EsmParser.Web`: browser UI and browser-backed file access.
- `src/BsaParser.Cli`: command parsing, console UI, and filesystem extraction.
- `tests`: TUnit suites and generated fixtures.

## Requirements

- Follow `.editorconfig` and `Directory.Build.props`. Always use braces for control
  statements, including single-line `if`, `else`, and loop bodies.
- Fix reported compiler/analyzer diagnostics. Do not disable rules or lower their
  severity merely to make a build pass; explain any genuinely necessary exception.
- Use the SDK pinned in `global.json` and the existing language/framework settings.
- Declare dependency versions centrally in `Directory.Packages.props`; project
  references to packages should not specify their own versions.
- Keep parsing in the core libraries and UI/filesystem orchestration in consumers.
  Preserve bounded-memory handling of large inputs and browser compatibility.
- Add meaningful tests for new behaviour and non-trivial bug fixes, following TUnit
  conventions. Formatting-only changes do not need new tests.
- Write repository utility and fixture-preparation scripts in PowerShell 7+, including
  the fixture manifest generator. Avoid introducing Node/npm tooling for these tasks.
- Write browser smoke tests in C# with TUnit and the .NET Microsoft.Playwright bindings.
  Keep JavaScript limited to browser interop/shims where needed, not smoke-test orchestration.
- Preserve existing user work. Commit or push only when requested. A request for a
  safety commit applies to that task, not every subsequent edit.
- Do not commit proprietary game archives, extracted assets, secrets, or build outputs.

## Verification

Run the affected project's build/tests during development. For shared settings,
cross-project API changes, or final integration verification, use:

```shell
dotnet build EsmParser.slnx
dotnet test --solution EsmParser.slnx
```

Tests use Microsoft.Testing.Platform, selected by `global.json`. Target a suite with
`dotnet test --project tests/<project>/<project>.csproj`.
Use the existing external-fixture switches rather than adding machine-specific paths
to default tests. Report skipped or unavailable verification accurately.
