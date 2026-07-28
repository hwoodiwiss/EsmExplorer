using EsmParser.Core.Navigation;

namespace EsmParser.Core;

/// <summary>A record matching the search criteria, with the node giving its location.</summary>
public readonly record struct RecordFound(RecordNode Record);

/// <summary>The search completed cleanly without a match.</summary>
public readonly record struct RecordNotFound;

/// <summary>
/// The outcome of a record search: found, exhaustively not found, or the scan
/// failed because the file is malformed.
/// </summary>
public union SearchResult(RecordFound, RecordNotFound, ParseError);
