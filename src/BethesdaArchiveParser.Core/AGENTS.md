# BSA library guidance

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
- Supported formats are named PC BSA 103/104 (zlib) and 105 (LZ4 frames). Xbox/XMem
  and unnamed archives are currently rejected explicitly. Do not imply support for
  another format without implementation and verification.
- Preserve the shipped-archive filename-table compatibility behaviour documented in
  `Reader/BsaReader.cs` and its regression test. Do not tighten validation solely from
  assumptions about perfectly formed archives.
- Keep malformed-input failures consistent with the existing exception-based API;
  the plugin parser's result-union convention is a separate library contract.

## Verification

Run `tests/BethesdaArchiveParser.Core.Tests`, and the CLI suite when changing public
archive APIs or extraction behaviour. Follow `tests/AGENTS.md` for fixture independence,
sync/async mode checks, and optional real-archive verification.
Review allocations, read granularity, and entry boundaries when changing hot paths;
do not claim performance improvements without describing the concrete change or measurement.
