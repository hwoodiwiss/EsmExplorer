# EsmParser

A Starfield plugin file (`.esm` / `.esp` / `.esl`) parser and browser-based explorer.
The core library parses the binary mod file format — records, groups, fields, and
compression — while tracking exactly where every structure lives in the file data.
The Blazor WebAssembly front end navigates plugins of any size (including the
1.4 GB `Starfield.esm`) directly in the browser: rather than loading the whole file,
it reads small slices on demand through a bounded page cache (~16 MiB), so memory
use stays flat regardless of file size.

## Solution layout

| Project | Purpose |
|---|---|
| `src/EsmParser.Core` | All parsing, navigation, and interpretation logic. No UI or platform dependencies. |
| `src/EsmParser.Web` | Blazor WebAssembly explorer. Display logic only — file access via JS blob slicing, tree navigation, record/field details, hex views, dark mode. |
| `src/BethesdaArchiveParser.Core` | BSA 103/104/105 metadata and bounded-memory payload extraction, with synchronous and asynchronous stream APIs. |
| `src/BsaParser.Cli` | NativeAOT-compatible System.CommandLine CLI with Spectre.Console selection and progress. |
| `tests/BethesdaArchiveParser.Core.Tests` | Generated BSA fixtures, sync/async I/O checks, and opt-in external fixture verification. |
| `tests/BsaParser.Cli.Tests` | Command parsing, interactive selection, extraction, overwrite, and error handling tests. |
| `tests/EsmParser.Web.Tests` | Browser file-stream and BSA resolver tests with fake JS interop, plus JavaScript directory-handle tests. |
| `tests/EsmParser.Core.Tests` | TUnit test suite: unit tests over synthetic plugins plus integration tests against the real `Starfield.esm`. |

## The file format

Starfield plugins use a modified version of the
[Skyrim mod file format](https://en.uesp.net/wiki/Skyrim_Mod:Mod_File_Format):
a `TES4` header record followed by nested `GRUP` containers of records, where each
record is a sequence of fields. The Starfield-specific deltas this parser implements
were verified empirically against `Starfield.esm` before implementation:

| Aspect | Starfield value |
|---|---|
| Record/group header size | 24 bytes (Skyrim SE layout) |
| Form version | 581 |
| HEDR version | 0.96 |
| Group types | 0–9 as Skyrim, plus **10 = Quest Children** |
| Light (ESL) header flag | `0x100` (Skyrim SE used `0x200`) |
| Medium master flag | `0x400` (new in Starfield) |
| Record compression | zlib with a `uint32` decompressed-size prefix (flag `0x40000`) |
| Large fields | `XXXX` field carries a 32-bit size for the following field |

## Design

### Results as union types

Parsing never throws for malformed data. Every operation returns a
[.NET 11 union type](https://learn.microsoft.com/dotnet/csharp/language-reference/):

```csharp
public union ParseResult<T>(Success<T>, ParseError);
public union SearchResult(RecordFound, RecordNotFound, ParseError);
```

Consumers pattern-match exhaustively — the compiler knows the full set of cases:

```csharp
switch (await PluginFile.OpenAsync(source))
{
    case Success<PluginFile>(var plugin):
        // navigate
        break;
    case ParseError error:
        Console.WriteLine(error); // kind, message, and file offset
        break;
}
```

`ParseError` carries a `ParseErrorKind` (invalid signature, structure overrun,
decompression failure, …) and the absolute file offset where the problem was found.
Contract violations (null arguments, negative offsets) still throw, and cancellation
uses `OperationCanceledException` — results model *expected* data states only.

### Lazy navigation with exact locations

Opening a plugin reads and validates only the `TES4` header. Everything below is
enumerated on demand (`GetTopLevelNodesAsync`, `GetChildrenAsync`,
`ReadRecordDataAsync`), so navigation cost is proportional to what you actually
look at. Every node and field exposes its `Offset`, `DataOffset`, `DataSize`, and
`EndOffset`; fields inside compressed records report offsets within the inflated
buffer instead of file positions.

### Pluggable data sources

`IDataSource` is an async random-access read abstraction with implementations for
memory (`MemoryDataSource`), files (`FileDataSource`), an LRU page cache decorator
(`CachingDataSource`), and — in the web project — browser files via JS `Blob.slice`
(`BlobDataSource`). In the browser, only the pages currently cached (plus any record
data being viewed) are resident in WASM memory; the rest of the file never crosses
the JS boundary.

### Cross-file references

A `PluginWorkspace` holds multiple plugins at once. Stored form ids are file-relative
(their top byte indexes the file's master list), so references are canonicalized to a
`FormKey` — defining plugin plus 24-bit object id — and followed into whichever loaded
file defines the form. Resolution outcomes are another union
(`ResolvedRecord | MissingMaster | RecordNotFound | ParseError`). Lookups go through a
per-plugin `FormIdIndex`: built lazily with one structural walk on the first resolution
into a file, then held as sorted arrays (a few tens of MB even for the full master).

### Editor-style form view

`RecordDecoder` decodes fields into named, typed values (`DecodedValue`, also a union:
text, localized-string index, numbers, references, structs) using a deliberately
conservative schema registry — only format-stable fields are labelled (editor ids,
names, bounds, keywords, placed-reference base/position, game-setting values typed by
their EDID prefix, the TES4 header, …). Anything unknown or misshapen degrades to a
raw view rather than risking a mislabel.

## Getting started

The repository pins its SDK in `global.json` (.NET 11 preview). Then:

```shell
dotnet build            # whole solution
dotnet test             # full test suite
dotnet run --project src/EsmParser.Web   # explorer at http://localhost:5246
```

Code-style analysis runs during builds (`EnforceCodeStyleInBuild`) and reported
warnings fail the build (`TreatWarningsAsErrors`). `.editorconfig` explicitly
requires braces, including single-line control statements, with `IDE0011` set to
error in the editor and build. Existing suggestion/disabled rule overrides retain
their severity. XML documentation is generated to enable build-time unnecessary
`using` analysis; missing XML comments (`CS1591`) are not enforced.

In the explorer, open one or more `.esm`/`.esp`/`.esl` files (load a mod together with
its masters to follow references between them), then browse the tree: groups expand
lazily, records show a decoded editor-style form view alongside their header metadata,
field table (signature, size, location, preview), and a paged hex dump of the on-disk
bytes. Form references are links — following one jumps to the defining record, even in
another loaded file, expanding the tree to it. A "Go to form id" box does the same for
arbitrary ids. The first jump into a plugin builds its form-id index (a one-off full
read, with progress); later jumps are instant.

## BSA unpacker

```powershell
# One archive: writes directly into output, preserving archive paths
dotnet run --project src/BsaParser.Cli -- unpack "D:\Game\Data\Meshes.bsa" -o "D:\Extracted"

# Interactive multi-selection (Space to select, Enter to unpack)
dotnet run --project src/BsaParser.Cli -- unpack-directory "D:\Game\Data" -o "D:\Extracted"

# Scripts/CI: select every archive, or repeat --archive for specific filenames
dotnet run --project src/BsaParser.Cli -- unpack-directory "D:\Game\Data" -o "D:\Extracted" --all --non-interactive
dotnet run --project src/BsaParser.Cli -- unpack-directory "D:\Game\Data" -o "D:\Extracted" --archive "Meshes.bsa" --archive "Textures.bsa" --non-interactive

# Native executable (requires the platform's NativeAOT toolchain)
dotnet publish src/BsaParser.Cli -c Release -r win-x64
```

Batch mode searches the input directory's top level, case-insensitively matching `.bsa`.
All archives write directly into the shared output directory, preserving their internal
paths (for example, `Extracted/meshes/…` and `Extracted/textures/…`).
`--all` and interactive selection process archives in case-insensitive filename order;
repeated `--archive` options use the supplied order. With `--overwrite`, later archives
replace earlier files at the same path; this order does not infer the game's load order.
`--all` and `--archive` are mutually exclusive. Prompts and live progress
are disabled by `--non-interactive`, redirected stdin/stdout, `TERM=dumb`, or a nonempty
`CI` value other than `false`/`0`. Without an interactive terminal, batch mode requires
explicit selection. The two commands also support `--help`.

Existing files, including paths already extracted from another BSA, cause an error
unless `--overwrite` is supplied. Each entry is first
streamed to a temporary file in its destination directory, then moved into place;
failed/cancelled entries leave existing files intact. Completed entries remain if a
later entry fails. Batch mode continues after archive failures and reports a summary.
Archive traversal paths, device names, and existing symbolic links/reparse points in
output paths are rejected. Use an output directory that is not concurrently modified.
Exit codes: **0** success, **1** extraction/argument error, **2** missing non-interactive
selection, **130** cancellation. Ctrl+C cancels the active command.

### Library API and portability

```csharp
using var stream = File.OpenRead("example.bsa");
var reader = new BsaArchiveReader(stream);
var archive = await reader.ReadBsaArchiveAsync(cancellationToken);
foreach (var entry in archive.Entries)
{
    // entry.Path is an archive-relative path; entry.File is its metadata record.
    await reader.CopyBsaFileToAsync(entry.File, destination, cancellationToken);
}
```

`ReadBsaArchive`, `ReadBsaFile`, and `CopyBsaFileTo` are the synchronous equivalents.
Async methods perform asynchronous source/destination I/O, without blocking on tasks
or using `Task.Run`, so seekable browser-backed streams can be used from WASM.
Streams are caller-owned and remain open; operations on the same source stream must
be serialized. Payload reads restore the source position. `ReadBsaFile[Async]` returns
a byte array; prefer `CopyBsaFileTo[Async]` for large entries. Payload copying uses a
pooled buffer, bounds compressed input to the entry, and verifies the expanded length.
Archive metadata is held in memory with buffered reads and a case-insensitive path index.
Treat the legacy mutable metadata lists as read-only after construction.

Supported: named PC BSA versions 103/104 (zlib) and 105 (LZ4 frames), including
per-file compression overrides and embedded names. Xbox/XMem archives and archives
without directory/file names are explicitly rejected. Version 105 folder offsets now
use `ulong` (`BsaFolderRecord.Offset`/`FileBlockOffset`) rather than truncating to 32 bits.
`BinaryBsaHeader.Version` uses the `uint`-backed `BsaVersion` enum: `Oblivion` (103),
`Fallout3AndSkyrim` (104, including New Vegas), and `SkyrimSpecialEdition` (105).
These mappings follow the [UESP format documentation](https://en.uesp.net/wiki/Skyrim_Mod:Archive_File_Format).
Unknown versions are rejected as unsupported rather than interpreted using an assumed layout.
Malformed archives throw `InvalidDataException`/`EndOfStreamException`.

### BSA regression fixtures

The default BSA/CLI suites use generated archives and do not require game installations.
For a supplied BSA, create an independently verified UTF-8 manifest containing every
entry as `SHA256<TAB>archive-relative-path` (one entry per line, optional `#` comments).
Use hashes from original files or extraction by a trusted independent tool, rather
than generating the expected values with this parser.

```powershell
$env:BSA_TEST_ARCHIVE = 'D:\Fixtures\regression.bsa'
$env:BSA_TEST_MANIFEST = 'D:\Fixtures\regression.sha256.tsv'
dotnet test --project tests/BethesdaArchiveParser.Core.Tests

# Optional local smoke check: parse every BSA and stream every entry to Stream.Null
$env:BSA_VERIFY_DIRECTORY = 'D:\SteamLibrary\steamapps\common\Fallout New Vegas\Data'
dotnet test --project tests/BethesdaArchiveParser.Core.Tests
```

External tests skip unless their environment variables are set. The manifest test
checks the full entry set and hashes through both sync and async streaming APIs.
The local smoke check verifies structural/decompression consistency, not independent
content correctness. No game archives are included in the repository.

## Model viewer

Records whose `MODL` field (or any text field ending in `.nif`) names a model get a
**View** link that opens the built-in NIF model viewer (`/model`, also in the nav).
The viewer needs read access to your game `Data` folder (File System Access API —
Chromium-based browsers): click **Choose data root…** and pick it once per session.
For BSA-based games such as Fallout: New Vegas, select the installed `Data` directory
directly; no extraction is required. The viewer indexes top-level BSA metadata, then
reads requested NIFs and dependencies using seekable, asynchronous file slices capped
at 64 KiB per interop call. Whole archives are never copied into browser memory.
Individual assets are materialized as byte arrays for the viewer's resolver API.
Loose files take precedence; duplicate archived paths use the last archive in
case-insensitive filename order (this does not reproduce plugin load order).
Changing the root rebuilds the index; cancelling the picker preserves the current root.
Unusable archives are listed in the UI while other archives remain available.

Use **Browse archived models** to filter NIF paths, or enter a data-relative path and
click **Load path**. The browser list shows up to 100 matches at a time. Record **View**
links continue to work with the same resolver. Starfield BA2 files still require the
extraction workflow below.

Model paths are resolved case-insensitively, trying `meshes/<path>` then `<path>`,
and external dependencies (`geometries/*.mesh`, `materials/*.mat`, `textures/*.dds`)
are streamed from the same folder on demand. Arbitrary `.nif` files can also be
loaded directly via **Open .nif…** (dependencies still resolve from the data root
when one is granted; without it, external meshes/textures are reported missing).
A control bar under the canvas adjusts camera speed, light colour/direction,
intensity, ambient, and auto-orbit. A collapsible panel shows the parsed
NIF block structure. Rendering uses the `NifViewer.Blazor` package, consumed from
the local feed at `.packages/` (a `LocalPackages` source with a package-source
mapping in `nuget.config`), so `dotnet restore` works offline.

### Preparing Starfield resources

The repository includes `scripts/Unpack-StarfieldResources.ps1`. It uses
Archive2 to extract every `Starfield - *.ba2` archive from the Starfield
installation into `StarfieldResources`; duplicate paths are overwritten by
later archives.

```powershell
.\scripts\Unpack-StarfieldResources.ps1
# Optional custom installation/output locations:
.\scripts\Unpack-StarfieldResources.ps1 `
    -StarfieldInstallDir 'D:\Games\Starfield' `
    -OutputDirectory 'C:\Users\me\Documents\StarfieldResources'
```

Archive2 is normally at
`<Starfield Install Dir>\Tools\Archive2\Archive2.exe`. The default script
assumes the Steam installation at
`C:\Program Files (x86)\Steam\steamapps\common\Starfield`.

For accurate Starfield material definitions, install the Creation Kit and
extract `<Starfield Install Dir>\Tools\ContentResources.zip`. The material
files are under its `Materials` directory. Copy or extract that directory into
the matching `materials` directory under the data root selected in the viewer.
The viewer also supports heuristic texture matching when loose `.mat` files
are unavailable, but the Creation Kit material files provide the most accurate
texture assignments.

## Testing

Web resolver tests run as part of the solution suite, linking the production service
and stream sources into a host-runtime test project. Run the JavaScript directory
handle tests with Node 20 or newer:

```shell
node --test tests/EsmParser.Web.Tests/dataRoot.test.mjs
```

The local `NifViewer.Blazor` package is `0.1.0-preview.5`, built from the sibling
`nif-viewer` repository with NiTriStrips/NiTriStripsData support, invalid-reference
checks, and degenerate-strip handling. Rebuild with
`dotnet pack nif-viewer-blazor/NifViewer.Blazor -c Release -o artifacts -m:1`
from that repository, then copy the package into `.packages` and update the central
version. Serial packing avoids concurrent multi-target builds writing the same WASM
output directory. Use a new package version to avoid stale NuGet-cache assets.

```shell
dotnet test
```

- **Unit tests** build synthetic plugins with a write-only `PluginBuilder`
  (independent of the production parser) and cover headers, navigation, compression,
  `XXXX` fields, error taxonomy, data sources, and field decoding.
- **Integration tests** run against the real master file at `Resources/Starfield.esm`
  and skip automatically when it is absent (it is gitignored — ~1.4 GB). To enable
  them, copy it from your game install
  (`…\steamapps\common\Starfield\Data\Starfield.esm`). The suite verifies the parser
  against an independently-scanned structural census: 101,341 groups, 3,829,246
  records, 91,149 of them compressed.

## Deployment

`.github/workflows/deploy-pages.yml` publishes the explorer to GitHub Pages on every
push to `main`: it runs the test suite, does a Release publish (WASM AOT), rewrites
the `<base href>` for project-page hosting, adds an SPA `404.html` fallback and
`.nojekyll`, and deploys via `actions/deploy-pages`. Enable **Settings → Pages →
Source: GitHub Actions** in the repository for the first deployment.
