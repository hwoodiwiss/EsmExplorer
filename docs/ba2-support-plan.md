# BA2 support plan for `BethesdaArchiveParser.Core`

Status: phases 0 and 2a done (restructure + unified API over BSA). BA2 reading (phases 1, 2b, 3, 4) pending.

Test suites under `tests/BethesdaArchiveParser.Core.Tests/`:

- `ArchiveReaderTests.cs` – format-agnostic API. BSA cases run today; BA2 cases are behind `#if BA2_READER`.
- `Ba2/Ba2ArchiveTests.cs` – BA2-specific reader behaviour (whole file behind `#if BA2_READER`).
- `Ba2/Ba2Fixture.cs` – write-only BA2 fixture builder (compiled today; used by detection tests).
- `Bsa/BsaArchiveTests.cs`, `Bsa/BsaFixture.cs`, `Bsa/ExternalArchiveTests.cs` – existing BSA coverage.
- `TestStreams.cs` – shared `ModeStream`/`AsyncOutput`.

To try the BA2 specs locally before removing the guards:
`dotnet build tests/BethesdaArchiveParser.Core.Tests -p:DefineConstants=BA2_READER`.

## Goals and non-goals

- Read Fallout 4, Fallout 4 next-gen and Starfield `BTDX` archives (`GNRL` and `DX10`)
  with sync and genuinely async APIs meeting the contracts in
  `src/BethesdaArchiveParser.Core/AGENTS.md`.
- One public API for reading both BSA and BA2, in the style of the existing
  `BsaArchiveReader` (caller-owned stream, `ReadX`/`ReadXAsync`, `CopyXTo`/`CopyXToAsync`).
- Bounded memory; WASM and NativeAOT compatible.
- Out of scope: writing archives, `GNMF` (PS4), CLI/Web adoption (follow-up PRs).

## Format summary

Primary code reference: `bsa-rs` (`ba2` crate) `src/fo4/{archive,chunk,file,hashing}.rs`
and `src/hashing.rs`; cross-check with UESP `Fallout4Mod:Archive_File_Format` and BSArch.

| Offset | Field | Notes |
| --- | --- | --- |
| 0 | `char[4]` magic | `BTDX` |
| 4 | `uint32` version | 1 = FO4, 2/3 = Starfield, 7/8 = FO4 next-gen |
| 8 | `char[4]` type | `GNRL`, `DX10` (reject `GNMF` explicitly) |
| 12 | `uint32` file count | |
| 16 | `uint64` name table offset | 0 = unnamed (reject explicitly, as for BSA) |
| 24 | `uint64` unknown | v2/v3 only. No consensus on meaning (tools write `1`); skip, don't validate |
| 32 | `uint32` compression method | v3 only: 0 = zlib, 3 = LZ4. Reject other values |

Header size: 24 (v1/7/8), 32 (v2), 36 (v3).

- GNRL record (36 B): name hash, `char[4]` ext, dir hash, `uint32` flags, `uint64` offset,
  `uint32` packed size (0 = stored), `uint32` unpacked size, `uint32` 0xBAADF00D.
- DX10 record (24 B): name hash, ext, dir hash, `u8` unk, `u8` chunk count,
  `u16` chunk header size (24), `u16` height, `u16` width, `u8` mips, `u8` DXGI format,
  `u8` flags (bit 0 = cubemap), `u8` tile mode; then chunk records (24 B): `uint64` offset,
  `uint32` packed, `uint32` unpacked, `u16` start mip, `u16` end mip, sentinel.
  Layout is identical across versions; only the header size differs.
- Name table: `uint16` length + Windows-1252 bytes per file, in record order.
- Compression: zlib streams (with header) for v1/2/7/8 and v3 method 0. v3 method 3 is a
  **raw LZ4 block** per chunk (no frame magic, no prefix); packed size covers the whole
  block, and the decoded length must equal the unpacked size. Applies to DX10 chunks too.
- Hashes (resolved): CRC-32, reflected polynomial `0xEDB88320`, **init 0, no final XOR**
  (not zlib's `crc32`). Normalise path (lowercase ASCII, `/`→`\`, trim leading/trailing `\`);
  name hash = stem (no extension), ext field = first 4 extension bytes, dir hash = parent
  path. Known vector: `Interface\Pipboy_StatsPage.swf` → `0x2F26E4D0`, `0x00667773`,
  `0xD2FDF873` (verified against `Ba2Fixture`'s independent CRC).
  - Note: this is *not* the 64-bit BSA hash (`BsaHash`). The two share only path
    normalisation. Expose a separate `Ba2Hash`. Don't validate hashes on read; they are
    for lookup and diagnostics.


## Public API

### Format-agnostic layer (implemented for BSA)

```csharp
namespace BethesdaArchiveParser.Core;
public enum ArchiveFormat { Unknown, Bsa, Ba2 }
public readonly union ArchiveFileRecord(BsaFileRecord);          // phase 2b: add Ba2FileRecord
public readonly union ArchiveDetails(BsaArchive);                 // phase 2b: add Ba2Archive
public sealed record ArchiveEntry(string Path, ArchiveFileRecord File, bool IsCompressed, long? ExpandedLength);
public sealed class Archive                                       // phase 2b: add Archive(Ba2Archive) constructor
{
    ArchiveFormat Format { get; }
    ArchiveDetails Details { get; }
    IReadOnlyList<ArchiveEntry> Entries { get; }
    ArchiveEntry? GetEntry(string path);          // case-insensitive, either separator
}

namespace BethesdaArchiveParser.Core.Reader;
/// Caller-owned, readable, seekable stream; left open; operations must not overlap.
public sealed class ArchiveReader(Stream stream)
{
    static ArchiveFormat DetectFormat(Stream);    static ValueTask<ArchiveFormat> DetectFormatAsync(Stream, CancellationToken = default);
    Archive ReadArchive();                        Task<Archive> ReadArchiveAsync(CancellationToken = default);
    byte[] ReadFile(ArchiveEntry);                Task<byte[]> ReadFileAsync(ArchiveEntry, CancellationToken = default);
    long CopyFileTo(ArchiveEntry, Stream);        ValueTask<long> CopyFileToAsync(ArchiveEntry, Stream, CancellationToken = default);
}
```

Current behaviour (`src/BethesdaArchiveParser.Core/Reader/ArchiveReader.cs`):

- `ArchiveReader` reads the 4-byte signature at offset 0, restores the stream position,
  and delegates to `BsaArchiveReader`. It adds no parsing of its own.
- `DetectFormat` returns `Unknown` for short or unrecognised input, and `ReadArchive`
  throws `InvalidDataException` for it. `BTDX` is detected as `Ba2`, but `ReadArchive`
  throws `NotSupportedException` until phase 2b. This is the same explicit rejection used
  for Xbox BSAs.
- `ExpandedLength` is `null` for BSA entries that are compressed or have embedded names,
  because those sizes live in the payload. It is set for plain entries. For BA2 it will
  always be known; for DX10 it will include the 148-byte DDS header.
- Entries are dispatched by pattern matching on `ArchiveEntry.File`. An entry of the wrong
  format throws `ArgumentException`.
- `BsaReader` reports "this is a BA2 archive" when it sees `BTDX`.
- The CLI (`ArchiveExtractor`) and Web (`GameDataService`) now use `ArchiveReader`.

### Format-specific layer (kept public)

- BSA: `BethesdaArchiveParser.Core.Bsa` (models) and `BethesdaArchiveParser.Core.Bsa.Reader.BsaArchiveReader`.
  This is a breaking namespace change from `…Core` / `…Core.Reader`, applied in this refactor.
- BA2 (to add): `BethesdaArchiveParser.Core.Ba2` with `Ba2Archive`, `Ba2Header`, `Ba2Version`,
  `Ba2ArchiveType`, `Ba2CompressionMethod`, `Ba2FileRecord` (Chunks, Texture, IsCompressed,
  ExpandedLength), `Ba2Chunk`, `Ba2TextureInfo`, `Ba2Entry`, and `Ba2Hash` (`Compute(path)` →
  `(Name, Extension, Directory)`). `BethesdaArchiveParser.Core.Ba2.Reader.Ba2ArchiveReader` has
  `ReadBa2Archive[Async]`, `ReadBa2File[Async]`, and `CopyBa2FileTo[Async]`.

Why keep both layers: the specific readers expose format details (BSA folder records,
BA2 chunks and texture metadata) without lossy abstraction. The unified layer stays thin
and can't drift, because it delegates.

## Directory structure (applied)

Namespaces follow folders.

```
src/BethesdaArchiveParser.Core/
  Archive.cs, ArchiveEntry.cs, ArchiveFormat.cs          …Core
  Reader/ArchiveReader.cs                                …Core.Reader
  Bsa/                                                   …Core.Bsa         (models, BsaHash, BsaFileData)
    Reader/                                              …Core.Bsa.Reader  (BsaArchiveReader + internals)
  Ba2/ (to add)                                          …Core.Ba2
    Reader/ (to add)                                     …Core.Ba2.Reader  (Ba2ArchiveReader, index, payload, DdsHeaderWriter)
  IO/                                                    …Core.IO          (BoundedReadStream, ScopedOffset,
                                                                            ValueTaskExtensions, BoundedPayload)
tests/BethesdaArchiveParser.Core.Tests/                  (single test namespace …Core.Tests)
  ArchiveReaderTests.cs, TestStreams.cs, FixtureManifestTests.cs
  Bsa/  BsaArchiveTests.cs, BsaFixture.cs, ExternalArchiveTests.cs
  Ba2/  Ba2ArchiveTests.cs, Ba2Fixture.cs, (to add) Ba2ExternalArchiveTests.cs
```

- `IO/BoundedPayload.Copy` is the format-neutral bounded copy: range check, position restore,
  optional decoder factory, exact expanded-size check, and trailing-data check.
  `BsaPayload` now only selects the decoder. BA2 zlib and stored chunks should reuse it.
  An `IO/Compression/` folder is only worth adding if LZ4-block handling needs shared helpers.
- `Bsa/BsaFixture.cs` is linked into `BsaParser.Cli.Tests` and `EsmParser.Web.Tests`.

## Phases

Each phase ends with `dotnet test --solution EsmParser.slnx` green.

### Phase 0 – restructure and groundwork ✅
- Moved files and namespaces as above. Updated the CLI, Web, test `using`s and the linked fixture paths.
- Extracted `IO/BoundedPayload` from `BsaPayload`.
- `BsaArchiveTests` uses the shared `TestStreams.cs`.

### Phase 2a – unified API over BSA ✅ (done ahead of BA2 so consumers migrate once)
- Added `ArchiveFormat`, `ArchiveFileRecord`, `ArchiveDetails`, `ArchiveEntry`, `Archive` and `ArchiveReader`.
- `BsaReader` now names BA2 on a `BTDX` signature.
- The CLI and Web use `ArchiveReader`.
- `ArchiveReaderTests` covers BSA 103/104/105 in both I/O modes, detection, explicit BA2
  rejection, `ExpandedLength`, pass-through of format-specific records, and cancellation.

### Phase 1 – BA2 header, GNRL and hashing (remove `#if BA2_READER` from `Ba2ArchiveTests`)
1. Add the BA2 public types, `Ba2Hash`, and the `Ba2ArchiveReader` skeleton so the suite compiles.
2. Header: magic, version map, type, v2/v3 extras. Throw `InvalidDataException` for an
   unknown version, type or compression, and for a zero or out-of-range name table offset.
3. Index: one buffered read of `count * recordSize` (checked, bounded by stream length
   before allocating), then a buffered name-table read. Reject duplicate paths.
4. Payloads: stored and zlib chunks go through `BoundedPayload.Copy` (zlib uses a
   `ZLibStream` decoder factory). LZ4: `LZ4Codec.Decode` per chunk. A block means the whole
   chunk is in memory, bounded by chunk size; document this. Restore the source position.
5. Passing: the `Ba2ArchiveTests` phase 1 group.

### Phase 2b – plug BA2 into the unified API (remove the remaining `#if BA2_READER` blocks)
1. Extend the unions to `ArchiveFileRecord(BsaFileRecord, Ba2FileRecord)` and
   `ArchiveDetails(BsaArchive, Ba2Archive)`. Add `Archive(Ba2Archive)`.
2. `ArchiveReader`: replace the `NotSupportedException` with delegation to `Ba2ArchiveReader`.
   Dispatch `ReadFile`/`CopyFileTo` on the record type.
3. Delete `Rejects_ba2_archives_explicitly_until_supported`. The `#if` swaps in
   `Rejects_entries_from_a_different_format`.
4. Update the `ArchiveFormat.Ba2` doc comment, README "Supported" text, and library `AGENTS.md`.

### Phase 3 – DX10 textures
1. Parse texture records and chunks: chunk header size = 24, chunk count > 0, mip ranges
   ascending and within `MipCount`.
2. Write `DDS_HEADER` + `DDS_HEADER_DXT10` from the DDS spec, then stream chunks in order.
3. Passing: the `Ba2ArchiveTests` phase 3 group, and the BA2 half of `Unified_reads_match_format_specific_readers`.

### Phase 4 – external fixtures and docs
1. Add a `BA2_VERIFY_DIRECTORY` smoke test that parses and streams every entry in both
   I/O modes, ideally through `ArchiveReader`.
2. Add `BA2_TEST_ARCHIVE`/`BA2_TEST_MANIFEST`, with hashes from an independent extractor
   (Archive2 or BSArch, never this parser). Include at least one Starfield v3 LZ4 archive.
   Optionally check that every record's hashes match `Ba2Hash`.
3. Update `README.md` and both `AGENTS.md` files (supported formats, BA2 coverage, env vars).

### Follow-ups (separate PRs)
- CLI: accept `.ba2` in archive discovery. Extraction already goes through `ArchiveReader`.
  Update the root command description, and add CLI tests per `tests/AGENTS.md`.
- Web explorer: index `.ba2` files in `GameDataService`, which already uses `ArchiveReader`.

## Risks and open questions
- LZ4 blocks can't be streamed. Memory is bounded by the largest chunk; reject chunks above
  a sane limit, or above `Array.MaxLength`.
- Texture totals may exceed `int`. Use `long` with `checked`.
- The v2/v3 extra header field is unexplained. Don't interpret or validate it.
- Only `0` and `3` are known v3 compression values; other values are rejected until seen in real data.
- The LZ4 and hash findings come from one implementation (`bsa-rs`). Confirm them with
  BSArch and real Starfield archives in phase 4.
- `ArchiveReader.ReadArchive` reads the signature and then the header, which costs one extra
  4-byte read per archive. That is negligible, but note it for browser interop.
