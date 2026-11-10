# Test guidance

## Conventions

- Use TUnit and the existing Microsoft.Testing.Platform configuration. Follow nearby
  test naming and assertion patterns; central package management also applies here.
- Write browser smoke tests in C# using TUnit and the .NET Microsoft.Playwright
  bindings. Keep real-game browser checks opt-in; see `EsmParser.Web.Tests/README.md`.
- Write fixture preparation utilities in PowerShell 7+ and test them from C#/TUnit.
  Use `scripts/Create-BsaFixtureManifest.ps1` for original-file hash manifests; do not
  introduce a Node test runner for fixture utilities or browser smoke tests.
- Test externally observable behaviour and meaningful failure cases. Avoid tests that
  merely reproduce the implementation. Brace/formatting-only edits need no new tests.
- Keep default suites portable: generate small inputs, use isolated temporary output
  directories, and clean up only resources owned by that test.
- Build fixture bytes independently of production parsing logic. Do not derive
  expected results by invoking the same production code under test.

## BSA coverage

- Cover supported versions, compression defaults and per-file overrides, embedded
  names, exact payload boundaries, malformed/truncated data, and cancellation.
- Verify sync and async modes separately. Async tests should include genuinely
  suspending, short-reading streams that reject sync I/O; sync tests should reject
  async I/O. Include destination behaviour when testing streaming extraction.
- For CLI changes, cover parsing, non-interactive detection, interactive selection via
  Spectre's test console, shared output paths, overwrite precedence, and failure handling.
- Keep generated fixtures write-only and small; `BethesdaArchiveParser.Core.Tests/Bsa/BsaFixture.cs`
  is shared with CLI tests through a linked source file.

## BA2 and unified API coverage

- `Ba2/Ba2Fixture.cs` builds GNRL/DX10 archives with its own CRC and compression calls.
  Keep it independent of production code.
- Cover every BA2 version, zlib and LZ4-block chunks, stored chunks, and texture/DDS output.
  Write DDS expectations from the DDS spec, not from `DdsHeader`.
- `ArchiveReaderTests` must run each format through the unified API in both I/O modes, and
  check that its output matches the format-specific reader.
- Use `TestStreams.cs` (`ModeStream`, `AsyncOutput`) for mode checks; don't add private copies.

## External fixtures

- No default test may depend on a particular Steam installation or local drive path.
- Use `BSA_VERIFY_DIRECTORY` for optional local BSA smoke checks. Successful parsing
  and decompression establish structural consistency, not independent content correctness.
- Use `BSA_TEST_ARCHIVE` and `BSA_TEST_MANIFEST` for supplied BSA regression data.
  The manifest contains every expected path and its SHA-256 hash; obtain hashes from
  original files or an independent trusted extractor, never this parser.
- The user-supplied independent BSA fixture is still pending as of this guidance's
  creation. Update that status when it is provided; do not treat local game archives
  as its replacement. See `README.md` and `Bsa/ExternalArchiveTests.cs` for the workflow.
- Follow the existing optional-resource convention for plugin integration tests too.
  Keep proprietary fixtures/extracted assets out of commits and report skipped tests.
- BA2 equivalents: `BA2_VERIFY_DIRECTORY` runs a local smoke check that parses, checks
  ASCII-path hashes, and streams every entry. `BA2_TEST_ARCHIVE` and `BA2_TEST_MANIFEST`
  run a manifest check; take its hashes from Archive2/BSArch extractions. See
  `Ba2/Ba2ExternalArchiveTests.cs`. The full Fallout 4 Data folder takes about 10 minutes.

## Commands (from repository root)

```shell
dotnet test --project tests/BethesdaArchiveParser.Core.Tests/BethesdaArchiveParser.Core.Tests.csproj
dotnet test --project tests/BsaParser.Cli.Tests/BsaParser.Cli.Tests.csproj
dotnet test --project tests/EsmParser.Core.Tests/EsmParser.Core.Tests.csproj
dotnet test --solution EsmParser.slnx
```
