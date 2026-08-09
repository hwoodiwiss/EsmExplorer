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

In the explorer, open one or more `.esm`/`.esp`/`.esl` files (load a mod together with
its masters to follow references between them), then browse the tree: groups expand
lazily, records show a decoded editor-style form view alongside their header metadata,
field table (signature, size, location, preview), and a paged hex dump of the on-disk
bytes. Form references are links — following one jumps to the defining record, even in
another loaded file, expanding the tree to it. A "Go to form id" box does the same for
arbitrary ids. The first jump into a plugin builds its form-id index (a one-off full
read, with progress); later jumps are instant.

## Model viewer

Records whose `MODL` field (or any text field ending in `.nif`) names a model get a
**View** link that opens the built-in NIF model viewer (`/model`, also in the nav).
The viewer needs read access to your game `Data` folder (File System Access API —
Chromium-based browsers): click **Choose data root…** and pick it once per session.
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
