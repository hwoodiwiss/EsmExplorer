using EsmParser.Core.Format;

namespace EsmParser.Core.Navigation;

/// <summary>
/// A node in a plugin's structural tree — either a <see cref="RecordNode"/> or a
/// <see cref="GroupNode"/> — with its exact location in the underlying file data.
/// </summary>
public abstract class PluginNode
{
    private protected PluginNode(long offset, int depth)
    {
        Offset = offset;
        Depth = depth;
    }

    /// <summary>Both records and groups carry a fixed 24-byte header.</summary>
    public const int HeaderSize = 24;

    /// <summary>Absolute file offset of the node's header.</summary>
    public long Offset { get; }

    /// <summary>Nesting depth: 0 for top-level nodes.</summary>
    public int Depth { get; }

    /// <summary>Absolute file offset of the node's data (records) or contents (groups).</summary>
    public long DataOffset => Offset + HeaderSize;

    /// <summary>Size in bytes of the node's data, excluding the header.</summary>
    public abstract long DataSize { get; }

    /// <summary>Total on-disk footprint, header included.</summary>
    public long TotalSize => HeaderSize + DataSize;

    /// <summary>Absolute file offset of the first byte after this node.</summary>
    public long EndOffset => Offset + TotalSize;
}

/// <summary>A record occurrence: a 24-byte header plus a data payload of fields.</summary>
public sealed class RecordNode(long offset, int depth, RecordHeader header) : PluginNode(offset, depth)
{
    public RecordHeader Header { get; } = header;

    public override long DataSize => Header.DataSize;

    public bool IsCompressed => Header.IsCompressed;

    /// <summary>Friendly name of the record type, when known.</summary>
    public string? TypeName => KnownRecordTypes.GetNameOrDefault(Header.Signature);

    public override string ToString() => $"{Header.Signature} {Header.FormId}";
}

/// <summary>A GRUP container holding records and/or nested groups.</summary>
public sealed class GroupNode(long offset, int depth, GroupHeader header) : PluginNode(offset, depth)
{
    public GroupHeader Header { get; } = header;

    public override long DataSize => Header.ContentSize;

    public override string ToString() => $"GRUP {Header.GetLabelDescription()}";
}
