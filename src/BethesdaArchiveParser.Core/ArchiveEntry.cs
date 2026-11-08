using BethesdaArchiveParser.Core.Bsa;

namespace BethesdaArchiveParser.Core;

/// <summary>A format-specific file record. Pattern-match to access format details.</summary>
public readonly union ArchiveFileRecord(BsaFileRecord);

/// <param name="Path">Archive-relative path using <c>\</c> separators.</param>
/// <param name="File">The format-specific record, accepted by the matching format reader.</param>
/// <param name="IsCompressed">Whether the stored payload is compressed.</param>
/// <param name="ExpandedLength">Extracted length when known without reading the payload; otherwise <see langword="null"/>.</param>
public sealed record ArchiveEntry(string Path, ArchiveFileRecord File, bool IsCompressed, long? ExpandedLength);
