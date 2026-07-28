using System.Buffers;
using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using EsmParser.Core.Format;
using EsmParser.Core.IO;
using EsmParser.Core.Navigation;

namespace EsmParser.Core;

/// <summary>
/// A parsed Starfield plugin (.esm/.esp/.esl). Opening validates and interprets the
/// TES4 header; everything below it is navigated lazily, so arbitrarily large files
/// stay cheap until content is actually requested.
/// </summary>
public sealed class PluginFile : IAsyncDisposable
{
    /// <summary>Window size for header-scanning reads while enumerating siblings.</summary>
    private const int ChunkSize = 256 * 1024;

    private readonly IDataSource _source;
    private readonly bool _leaveOpen;

    private PluginFile(IDataSource source, bool leaveOpen, RecordNode headerRecord, PluginHeader header)
    {
        _source = source;
        _leaveOpen = leaveOpen;
        HeaderRecord = headerRecord;
        Header = header;
    }

    /// <summary>The source name, typically the plugin file name.</summary>
    public string Name => _source.Name;

    /// <summary>Total size of the plugin data in bytes.</summary>
    public long Length => _source.Length;

    /// <summary>The TES4 record node at offset 0.</summary>
    public RecordNode HeaderRecord { get; }

    /// <summary>The interpreted TES4 header content.</summary>
    public PluginHeader Header { get; }

    /// <summary>
    /// Opens a plugin over <paramref name="source"/>, validating and interpreting its TES4 header.
    /// The source is disposed with the plugin unless <paramref name="leaveOpen"/> is set.
    /// </summary>
    public static async ValueTask<ParseResult<PluginFile>> OpenAsync(
        IDataSource source,
        bool leaveOpen = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (source.Length < RecordHeader.Size)
        {
            return new ParseError(
                ParseErrorKind.UnexpectedEndOfData,
                Invariant($"A plugin file must be at least {RecordHeader.Size} bytes; this source is {source.Length}."),
                0);
        }

        byte[] headerBytes = new byte[RecordHeader.Size];
        if (await source.ReadExactlyAsync(0, headerBytes, cancellationToken) is ParseError readError)
        {
            return readError;
        }

        if (Signature.Read(headerBytes) != KnownSignatures.Tes4)
        {
            return new ParseError(
                ParseErrorKind.InvalidSignature,
                Invariant($"Expected the file to begin with a TES4 record, found '{Signature.Read(headerBytes)}'."),
                0);
        }

        RecordHeader recordHeader = RecordHeader.Read(headerBytes);
        var headerRecord = new RecordNode(0, 0, recordHeader);
        if (headerRecord.EndOffset > source.Length)
        {
            return new ParseError(
                ParseErrorKind.StructureOverrun,
                Invariant($"The TES4 record claims {recordHeader.DataSize} data bytes, which runs past the end of the file."),
                0);
        }

        ParseResult<RecordData> headerData = await ReadRecordDataCoreAsync(source, headerRecord, cancellationToken);
        if (!headerData.TryGet(out RecordData? recordData, out ParseError? dataError))
        {
            return dataError;
        }

        ParseResult<PluginHeader> header = PluginHeader.FromRecordData(recordData, source.Name);
        if (!header.TryGet(out PluginHeader? pluginHeader, out ParseError? headerError))
        {
            return headerError;
        }

        return new Success<PluginFile>(new PluginFile(source, leaveOpen, headerRecord, pluginHeader));
    }

    /// <summary>
    /// Enumerates the nodes following the TES4 record: the plugin's top-level groups
    /// (and, for tolerance, any stray top-level records).
    /// </summary>
    public ValueTask<ParseResult<IReadOnlyList<PluginNode>>> GetTopLevelNodesAsync(CancellationToken cancellationToken = default)
        => ParseSiblingsAsync(_source, HeaderRecord.EndOffset, _source.Length, 0, cancellationToken);

    /// <summary>Enumerates the direct children (records and subgroups) of a group.</summary>
    public ValueTask<ParseResult<IReadOnlyList<PluginNode>>> GetChildrenAsync(GroupNode group, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(group);
        return ParseSiblingsAsync(_source, group.DataOffset, group.EndOffset, group.Depth + 1, cancellationToken);
    }

    /// <summary>
    /// Reads a record's payload, inflating it when compressed, and parses its fields
    /// (including XXXX extended sizes).
    /// </summary>
    public ValueTask<ParseResult<RecordData>> ReadRecordDataAsync(RecordNode record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        return ReadRecordDataCoreAsync(_source, record, cancellationToken);
    }

    /// <summary>Reads raw bytes for inspection (e.g. hex views). Fails when the range leaves the file.</summary>
    public async ValueTask<ParseResult<byte[]>> ReadBytesAsync(long offset, int count, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        byte[] buffer = new byte[count];
        if (await _source.ReadExactlyAsync(offset, buffer, cancellationToken) is ParseError error)
        {
            return error;
        }

        return new Success<byte[]>(buffer);
    }

    /// <summary>
    /// Scans for the record with the given form id, searching the whole file or a single
    /// subtree. Group labels are not trusted (per the spec), so this walks records exhaustively.
    /// </summary>
    public async ValueTask<SearchResult> FindRecordAsync(FormId formId, GroupNode? root = null, CancellationToken cancellationToken = default)
    {
        RecordNode? found = null;
        ParseResult<Unit> walk = await WalkAsync(
            root,
            node =>
            {
                if (node is RecordNode record && record.Header.FormId == formId)
                {
                    found = record;
                    return false;
                }

                return true;
            },
            cancellationToken);

        if (walk is ParseError error)
        {
            return error;
        }

        return found is null ? new RecordNotFound() : new RecordFound(found);
    }

    /// <summary>Computes structural statistics for the whole file or a single subtree.</summary>
    public async ValueTask<ParseResult<PluginStatistics>> ComputeStatisticsAsync(GroupNode? root = null, CancellationToken cancellationToken = default)
    {
        long groups = 0;
        long records = 0;
        long compressed = 0;
        int maxDepth = 0;
        var bySignature = new Dictionary<Signature, long>();
        var byGroupType = new Dictionary<GroupType, long>();

        ParseResult<Unit> walk = await WalkAsync(
            root,
            node =>
            {
                maxDepth = Math.Max(maxDepth, node.Depth);
                if (node is GroupNode group)
                {
                    groups++;
                    byGroupType[group.Header.GroupType] = byGroupType.GetValueOrDefault(group.Header.GroupType) + 1;
                }
                else if (node is RecordNode record)
                {
                    records++;
                    if (record.IsCompressed)
                    {
                        compressed++;
                    }

                    bySignature[record.Header.Signature] = bySignature.GetValueOrDefault(record.Header.Signature) + 1;
                }

                return true;
            },
            cancellationToken);

        if (walk is ParseError error)
        {
            return error;
        }

        return new Success<PluginStatistics>(new PluginStatistics(groups, records, compressed, maxDepth, bySignature, byGroupType));
    }

    public async ValueTask DisposeAsync()
    {
        if (!_leaveOpen)
        {
            await _source.DisposeAsync();
        }
    }

    /// <summary>
    /// Depth-first structural walk (sibling order within each container; container order
    /// across the stack is unspecified). The visitor returns <see langword="false"/> to stop early.
    /// </summary>
    private async ValueTask<ParseResult<Unit>> WalkAsync(GroupNode? root, Func<PluginNode, bool> visit, CancellationToken cancellationToken)
    {
        var pending = new Stack<(long Start, long End, int Depth)>();
        pending.Push(root is null
            ? (HeaderRecord.EndOffset, _source.Length, 0)
            : (root.DataOffset, root.EndOffset, root.Depth + 1));

        while (pending.Count > 0)
        {
            (long start, long end, int depth) = pending.Pop();
            ParseResult<IReadOnlyList<PluginNode>> siblings = await ParseSiblingsAsync(_source, start, end, depth, cancellationToken);
            if (!siblings.TryGet(out IReadOnlyList<PluginNode>? nodes, out ParseError? error))
            {
                return error;
            }

            foreach (PluginNode node in nodes)
            {
                if (!visit(node))
                {
                    return new Success<Unit>(Unit.Value);
                }

                if (node is GroupNode group && group.DataSize > 0)
                {
                    pending.Push((group.DataOffset, group.EndOffset, group.Depth + 1));
                }
            }
        }

        return new Success<Unit>(Unit.Value);
    }

    private static async ValueTask<ParseResult<IReadOnlyList<PluginNode>>> ParseSiblingsAsync(
        IDataSource source,
        long start,
        long end,
        int depth,
        CancellationToken cancellationToken)
    {
        var nodes = new List<PluginNode>();
        if (start == end)
        {
            return new Success<IReadOnlyList<PluginNode>>(nodes);
        }

        byte[] buffer = ArrayPool<byte>.Shared.Rent(ChunkSize);
        try
        {
            long windowStart = 0;
            int windowLength = 0;
            long position = start;
            while (position < end)
            {
                if (end - position < PluginNode.HeaderSize)
                {
                    return new ParseError(
                        ParseErrorKind.UnexpectedEndOfData,
                        Invariant($"A node header needs {PluginNode.HeaderSize} bytes, but only {end - position} remain in the container."),
                        position);
                }

                if (position < windowStart || position + PluginNode.HeaderSize > windowStart + windowLength)
                {
                    windowLength = (int)Math.Min(ChunkSize, end - position);
                    if (await source.ReadExactlyAsync(position, buffer.AsMemory(0, windowLength), cancellationToken) is ParseError readError)
                    {
                        return readError;
                    }

                    windowStart = position;
                }

                ParseResult<PluginNode> parsed = ParseNodeHeader(buffer, (int)(position - windowStart), position, depth, end);
                if (!parsed.TryGet(out PluginNode? node, out ParseError? nodeError))
                {
                    return nodeError;
                }

                nodes.Add(node);
                position += node.TotalSize;
            }

            return new Success<IReadOnlyList<PluginNode>>(nodes);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static ParseResult<PluginNode> ParseNodeHeader(byte[] buffer, int bufferOffset, long position, int depth, long containerEnd)
    {
        ReadOnlySpan<byte> span = buffer.AsSpan(bufferOffset, PluginNode.HeaderSize);
        Signature signature = Signature.Read(span);

        if (signature == KnownSignatures.Grup)
        {
            GroupHeader header = GroupHeader.Read(span);
            if (header.TotalSize < GroupHeader.Size)
            {
                return new ParseError(
                    ParseErrorKind.StructureInvalid,
                    Invariant($"Group '{header.GetLabelDescription()}' declares total size {header.TotalSize}, smaller than its own {GroupHeader.Size}-byte header."),
                    position);
            }

            if (position + header.TotalSize > containerEnd)
            {
                return new ParseError(
                    ParseErrorKind.StructureOverrun,
                    Invariant($"Group '{header.GetLabelDescription()}' ({header.TotalSize} bytes) runs past the end of its container at 0x{containerEnd:X}."),
                    position);
            }

            return new Success<PluginNode>(new GroupNode(position, depth, header));
        }

        RecordHeader recordHeader = RecordHeader.Read(span);
        if (position + RecordHeader.Size + (long)recordHeader.DataSize > containerEnd)
        {
            return new ParseError(
                ParseErrorKind.StructureOverrun,
                Invariant($"Record {recordHeader.Signature} {recordHeader.FormId} ({recordHeader.DataSize} data bytes) runs past the end of its container at 0x{containerEnd:X}."),
                position);
        }

        return new Success<PluginNode>(new RecordNode(position, depth, recordHeader));
    }

    private static async ValueTask<ParseResult<RecordData>> ReadRecordDataCoreAsync(
        IDataSource source,
        RecordNode record,
        CancellationToken cancellationToken)
    {
        if (record.Header.DataSize > int.MaxValue)
        {
            return new ParseError(
                ParseErrorKind.StructureInvalid,
                Invariant($"Record {record} declares an unsupported data size of {record.Header.DataSize} bytes."),
                record.Offset);
        }

        byte[] raw = new byte[(int)record.Header.DataSize];
        if (await source.ReadExactlyAsync(record.DataOffset, raw, cancellationToken) is ParseError readError)
        {
            return readError;
        }

        byte[] fieldBytes;
        long? absoluteBase;
        if (record.IsCompressed)
        {
            ParseResult<byte[]> inflated = Inflate(raw, record);
            if (!inflated.TryGet(out fieldBytes!, out ParseError? inflateError))
            {
                return inflateError;
            }

            absoluteBase = null;
        }
        else
        {
            fieldBytes = raw;
            absoluteBase = record.DataOffset;
        }

        ParseResult<IReadOnlyList<RecordField>> fields = ParseFields(fieldBytes, absoluteBase, record);
        if (!fields.TryGet(out IReadOnlyList<RecordField>? fieldList, out ParseError? fieldError))
        {
            return fieldError;
        }

        return new Success<RecordData>(new RecordData(record, record.IsCompressed, fieldBytes, fieldList));
    }

    private static ParseResult<byte[]> Inflate(byte[] raw, RecordNode record)
    {
        if (raw.Length < 4)
        {
            return new ParseError(
                ParseErrorKind.DecompressionFailed,
                Invariant($"Compressed record {record} has only {raw.Length} data bytes; at least 4 are needed for the decompressed size."),
                record.DataOffset);
        }

        uint declaredSize = BinaryPrimitives.ReadUInt32LittleEndian(raw);
        if (declaredSize > int.MaxValue)
        {
            return new ParseError(
                ParseErrorKind.DecompressionFailed,
                Invariant($"Compressed record {record} declares an unsupported decompressed size of {declaredSize} bytes."),
                record.DataOffset);
        }

        byte[] inflated = new byte[(int)declaredSize];
        try
        {
            using var stream = new ZLibStream(new MemoryStream(raw, 4, raw.Length - 4, writable: false), CompressionMode.Decompress);
            int filled = 0;
            while (filled < inflated.Length)
            {
                int read = stream.Read(inflated, filled, inflated.Length - filled);
                if (read == 0)
                {
                    break;
                }

                filled += read;
            }

            if (filled != inflated.Length || stream.ReadByte() != -1)
            {
                return new ParseError(
                    ParseErrorKind.DecompressionFailed,
                    Invariant($"Compressed record {record} inflated to a different size than the declared {declaredSize} bytes."),
                    record.DataOffset);
            }
        }
        catch (InvalidDataException exception)
        {
            return new ParseError(
                ParseErrorKind.DecompressionFailed,
                Invariant($"Compressed record {record} does not contain valid zlib data: {exception.Message}"),
                record.DataOffset);
        }

        return new Success<byte[]>(inflated);
    }

    private static ParseResult<IReadOnlyList<RecordField>> ParseFields(byte[] data, long? absoluteBase, RecordNode record)
    {
        var fields = new List<RecordField>();
        int position = 0;
        while (position < data.Length)
        {
            long errorOffset = absoluteBase + position ?? record.Offset;
            if (data.Length - position < RecordField.HeaderSize)
            {
                return new ParseError(
                    ParseErrorKind.FieldInvalid,
                    Invariant($"Record {record}: a field header needs {RecordField.HeaderSize} bytes, but only {data.Length - position} remain."),
                    errorOffset);
            }

            Signature signature = Signature.Read(data.AsSpan(position));
            int declaredSize = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(position + 4));
            int? extendedHeaderOffset = null;
            int actualSize;

            if (signature == KnownSignatures.Xxxx)
            {
                if (declaredSize != 4)
                {
                    return new ParseError(
                        ParseErrorKind.ExtendedSizeInvalid,
                        Invariant($"Record {record}: XXXX field declares size {declaredSize}; only 4-byte extended sizes are supported."),
                        errorOffset);
                }

                if (data.Length - position < RecordField.HeaderSize + 4 + RecordField.HeaderSize)
                {
                    return new ParseError(
                        ParseErrorKind.ExtendedSizeInvalid,
                        Invariant($"Record {record}: XXXX field is not followed by a complete field."),
                        errorOffset);
                }

                uint extendedSize = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(position + RecordField.HeaderSize));
                if (extendedSize > int.MaxValue)
                {
                    return new ParseError(
                        ParseErrorKind.ExtendedSizeInvalid,
                        Invariant($"Record {record}: XXXX field declares an unsupported extended size of {extendedSize} bytes."),
                        errorOffset);
                }

                extendedHeaderOffset = position;
                position += RecordField.HeaderSize + 4;
                signature = Signature.Read(data.AsSpan(position));
                actualSize = (int)extendedSize;
            }
            else
            {
                actualSize = declaredSize;
            }

            int dataStart = position + RecordField.HeaderSize;
            if ((long)dataStart + actualSize > data.Length)
            {
                return new ParseError(
                    ParseErrorKind.FieldInvalid,
                    Invariant($"Record {record}: field {signature} claims {actualSize} bytes, running past the end of the record data."),
                    errorOffset);
            }

            fields.Add(new RecordField(
                signature,
                dataStart,
                absoluteBase + dataStart,
                extendedHeaderOffset,
                data.AsMemory(dataStart, actualSize)));
            position = dataStart + actualSize;
        }

        return new Success<IReadOnlyList<RecordField>>(fields);
    }

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
