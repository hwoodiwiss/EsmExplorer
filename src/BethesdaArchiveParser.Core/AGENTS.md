# Archive library guidance

## User requirements

- Keep the library usable from WASM and NativeAOT consumers.
- Provide synchronous and asynchronous modes. Async paths must perform async I/O;
  do not implement them with synchronous reads, blocking waits, or `Task.Run` wrappers.
- Sync paths must not depend on asynchronously completing operations. The internal
  `AsSync` helper is only appropriate when the selected path completes synchronously.
- Prefer bounded-memory streaming over loading compressed and expanded copies of an
  entry. Keep metadata reading buffered to avoid a stream/JS interop call per byte.
- Extend the public archive API when needed for consumers, with tests and clear stream
  ownership/lifetime semantics.

## Current contracts and format pitfalls

- Layout: format-agnostic `Archive*` types at the root and `Reader/ArchiveReader.cs`;
  per-format code in `Bsa/` and `Ba2/`, each with its own `Reader/`; shared bounded I/O
  and payload copying in `IO/`. Namespaces follow folders. `ArchiveReader` only detects
  and delegates, so keep parsing in the format-specific readers.
- Readers use caller-owned readable, seekable streams and leave them open. Operations
  sharing a source stream must be serialized; payload reads restore source position.
- Keep cancellation flowing through async source/destination operations.
- `BsaArchive.Entries` associates paths with records; path lookup is case-insensitive
  and accepts either separator. Avoid rebuilding paths through hash-only name lookups.
- Distinguish raw record size flags, stored payload length, embedded-name prefixes,
  and expanded length. Bound decompression to the entry and verify expanded size.
- `Stream.CopyTo`'s buffer-size argument is not a byte limit. Use bounded reads to
  prevent an entry from consuming the next entry or the remainder of the archive.
- Parse binary fields with explicit endianness. Preserve 64-bit BSA 105 folder offsets.
- Supported formats are named PC BSA 103/104 (zlib) and 105 (LZ4 frames), and BA2
  v1/v2/v3/v7/v8 `GNRL`/`DX10` (zlib, or raw LZ4 blocks for v3 method 3). Xbox/XMem BSAs,
  `GNMF` BA2s, and unnamed archives are rejected explicitly. Do not imply support for
  another format without implementation and verification.
- BA2: DX10 entries are emitted as DDS (`Ba2/Reader/DdsHeader.cs`, written from the DDS spec).
  Don't validate the 0xBAADF00D sentinel, record flags, the v2/v3 unknown header field, or
  name/directory hashes. Shipped archives contain non-ASCII names whose hashes `Ba2Hash`
  does not reproduce.
- An overstated compressed size can't always be detected, because zlib and LZ4 stop at their
  own end markers. Bounding still guarantees that a read never affects the following entry.
- Preserve the shipped-archive filename-table compatibility behaviour documented in
  `Bsa/Reader/BsaReader.cs` and its regression test. Do not tighten validation solely from
  assumptions about perfectly formed archives.
- Keep malformed-input failures consistent with the existing exception-based API;
  the plugin parser's result-union convention is a separate library contract.

## Verification

Run `tests/BethesdaArchiveParser.Core.Tests`, and the CLI suite when changing public
archive APIs or extraction behaviour. Follow `tests/AGENTS.md` for fixture independence,
sync/async mode checks, and optional real-archive verification.
Review allocations, read granularity, and entry boundaries when changing hot paths;
do not claim performance improvements without describing the concrete change or measurement.
