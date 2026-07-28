using EsmParser.Core.Format;
using EsmParser.Core.Navigation;

namespace EsmParser.Core;

/// <summary>
/// A lookup from stored form id to record offset for one plugin, built with a single
/// structural walk and kept as sorted parallel arrays so that even multi-million-record
/// masters index in tens of megabytes.
/// </summary>
public sealed class FormIdIndex
{
    private readonly uint[] _formIds;
    private readonly long[] _offsets;

    private FormIdIndex(uint[] formIds, long[] offsets)
    {
        _formIds = formIds;
        _offsets = offsets;
    }

    /// <summary>Number of indexed records.</summary>
    public int Count => _formIds.Length;

    /// <summary>
    /// Walks the plugin's full structure and indexes every record. Progress (0..1,
    /// approximate, by file position) is reported for long builds over slow sources.
    /// </summary>
    public static async ValueTask<ParseResult<FormIdIndex>> BuildAsync(
        PluginFile plugin,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plugin);

        var entries = new List<(uint FormId, long Offset)>();
        long length = plugin.Length;
        long reportedPosition = 0;

        ParseResult<Unit> walk = await plugin.WalkAsync(
            root: null,
            node =>
            {
                if (node is RecordNode record)
                {
                    entries.Add((record.Header.FormId.Value, record.Offset));
                }

                if (progress is not null && node.Offset > reportedPosition)
                {
                    reportedPosition = node.Offset;
                    progress.Report((double)reportedPosition / length);
                }

                return true;
            },
            cancellationToken);

        if (walk is ParseError error)
        {
            return error;
        }

        // Sort by form id (offset as a deterministic tie-break; duplicate ids should
        // not occur, but if they do the first occurrence in the file wins).
        entries.Sort(static (left, right) =>
        {
            int byId = left.FormId.CompareTo(right.FormId);
            return byId != 0 ? byId : left.Offset.CompareTo(right.Offset);
        });

        uint[] formIds = new uint[entries.Count];
        long[] offsets = new long[entries.Count];
        int count = 0;
        foreach ((uint formId, long offset) in entries)
        {
            if (count > 0 && formIds[count - 1] == formId)
            {
                continue;
            }

            formIds[count] = formId;
            offsets[count] = offset;
            count++;
        }

        progress?.Report(1.0);
        return new Success<FormIdIndex>(new FormIdIndex(formIds[..count], offsets[..count]));
    }

    /// <summary>Returns the file offset of the record with the given stored form id, or <see langword="null"/>.</summary>
    public long? FindOffset(FormId formId)
    {
        int index = Array.BinarySearch(_formIds, formId.Value);
        return index >= 0 ? _offsets[index] : null;
    }
}
