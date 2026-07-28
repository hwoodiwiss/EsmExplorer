using EsmParser.Core.Format;

namespace EsmParser.Core;

/// <summary>Aggregate structural counts for a plugin (or a subtree of one).</summary>
public sealed class PluginStatistics
{
    internal PluginStatistics(
        long groupCount,
        long recordCount,
        long compressedRecordCount,
        int maxDepth,
        IReadOnlyDictionary<Signature, long> recordCountsBySignature,
        IReadOnlyDictionary<GroupType, long> groupCountsByType)
    {
        GroupCount = groupCount;
        RecordCount = recordCount;
        CompressedRecordCount = compressedRecordCount;
        MaxDepth = maxDepth;
        RecordCountsBySignature = recordCountsBySignature;
        GroupCountsByType = groupCountsByType;
    }

    public long GroupCount { get; }

    /// <summary>Number of records, excluding the TES4 header record.</summary>
    public long RecordCount { get; }

    public long CompressedRecordCount { get; }

    /// <summary>Deepest node nesting encountered (top-level nodes are depth 0).</summary>
    public int MaxDepth { get; }

    public IReadOnlyDictionary<Signature, long> RecordCountsBySignature { get; }

    public IReadOnlyDictionary<GroupType, long> GroupCountsByType { get; }

    public long TotalNodeCount => GroupCount + RecordCount;
}
