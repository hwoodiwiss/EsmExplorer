using EsmParser.Core.Format;
using EsmParser.Core.Navigation;

namespace EsmParser.Core;

/// <summary>How a plugin participates in the load order.</summary>
public enum PluginKind
{
    /// <summary>Full master (.esm or master flag).</summary>
    Master,

    /// <summary>Medium master (Starfield; FD-prefixed runtime form ids).</summary>
    MediumMaster,

    /// <summary>Small/light master (ESL; FE-prefixed runtime form ids).</summary>
    LightMaster,

    /// <summary>Regular plugin (.esp, no master-scale flags).</summary>
    Plugin,
}

/// <summary>A master file dependency declared by a plugin's MAST fields.</summary>
/// <param name="FileName">The master's file name as recorded in the plugin.</param>
/// <param name="Index">
/// Position in the master list; form ids whose <see cref="FormId.MasterIndex"/> equals this
/// index resolve to forms defined in this master.
/// </param>
public sealed record MasterReference(string FileName, int Index);

/// <summary>
/// The interpreted content of the TES4 plugin header record.
/// </summary>
public sealed class PluginHeader
{
    private PluginHeader(
        RecordHeader recordHeader,
        float version,
        uint recordAndGroupCount,
        uint nextFormId,
        string? author,
        string? description,
        IReadOnlyList<MasterReference> masters,
        IReadOnlyList<FormId> overriddenForms,
        int? interiorCellCount,
        PluginKind kind)
    {
        RecordHeader = recordHeader;
        Version = version;
        RecordAndGroupCount = recordAndGroupCount;
        NextFormId = nextFormId;
        Author = author;
        Description = description;
        Masters = masters;
        OverriddenForms = overriddenForms;
        InteriorCellCount = interiorCellCount;
        Kind = kind;
    }

    public RecordHeader RecordHeader { get; }

    /// <summary>HEDR version; 0.96 for the Starfield format revision this parser targets.</summary>
    public float Version { get; }

    /// <summary>HEDR count of records and groups in the file (includes the TES4 record itself).</summary>
    public uint RecordAndGroupCount { get; }

    /// <summary>HEDR next-available object id.</summary>
    public uint NextFormId { get; }

    /// <summary>CNAM author, when present.</summary>
    public string? Author { get; }

    /// <summary>SNAM description, when present.</summary>
    public string? Description { get; }

    /// <summary>Masters declared by MAST fields, in file order (which defines form-id resolution).</summary>
    public IReadOnlyList<MasterReference> Masters { get; }

    /// <summary>Forms overridden by this plugin, from ONAM fields.</summary>
    public IReadOnlyList<FormId> OverriddenForms { get; }

    /// <summary>INCC interior cell count, when present.</summary>
    public int? InteriorCellCount { get; }

    public PluginKind Kind { get; }

    public bool IsMaster => (RecordHeader.Flags & RecordFlags.Master) != 0;

    public bool IsLocalized => (RecordHeader.Flags & RecordFlags.Localized) != 0;

    public bool IsLightMaster => (RecordHeader.Flags & RecordFlags.LightMaster) != 0;

    public bool IsMediumMaster => (RecordHeader.Flags & RecordFlags.MediumMaster) != 0;

    /// <summary>
    /// Resolves the name of the plugin that defines the form with the given id: one of this
    /// plugin's masters, or <paramref name="selfName"/> when the form originates here.
    /// Ids beyond the master list also belong to the plugin itself.
    /// </summary>
    public string ResolveOrigin(FormId formId, string selfName)
        => formId.MasterIndex < Masters.Count ? Masters[formId.MasterIndex].FileName : selfName;

    /// <summary>
    /// Determines the plugin kind from header flags with the file extension as a fallback signal,
    /// mirroring the game's rule that .esm/.esl extensions imply master/light treatment even
    /// without the corresponding flag.
    /// </summary>
    public static PluginKind DetermineKind(RecordFlags flags, string? fileName)
    {
        string? extension = fileName is null ? null : Path.GetExtension(fileName);
        bool hasExtension(string candidate) => string.Equals(extension, candidate, StringComparison.OrdinalIgnoreCase);

        if ((flags & RecordFlags.LightMaster) != 0 || hasExtension(".esl"))
        {
            return PluginKind.LightMaster;
        }

        if ((flags & RecordFlags.MediumMaster) != 0)
        {
            return PluginKind.MediumMaster;
        }

        if ((flags & RecordFlags.Master) != 0 || hasExtension(".esm"))
        {
            return PluginKind.Master;
        }

        return PluginKind.Plugin;
    }

    /// <summary>Interprets the parsed TES4 field set. Fails when required content (HEDR) is missing or malformed.</summary>
    internal static ParseResult<PluginHeader> FromRecordData(RecordData data, string sourceName)
    {
        float version = 0;
        uint recordCount = 0;
        uint nextFormId = 0;
        bool sawHedr = false;
        string? author = null;
        string? description = null;
        var masters = new List<MasterReference>();
        var overridden = new List<FormId>();
        int? interiorCellCount = null;

        foreach (RecordField field in data.Fields)
        {
            if (field.Signature == KnownSignatures.Hedr)
            {
                if (field.Size < 12)
                {
                    return new ParseError(
                        ParseErrorKind.HeaderInvalid,
                        "The HEDR field must be at least 12 bytes (version, record count, next form id).",
                        field.AbsoluteDataOffset);
                }

                ReadOnlySpan<byte> span = field.Data.Span;
                version = System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian(span);
                recordCount = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(span[4..]);
                nextFormId = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(span[8..]);
                sawHedr = true;
            }
            else if (field.Signature == KnownSignatures.Cnam)
            {
                if (!field.ReadZString().TryGet(out author, out ParseError? error))
                {
                    return error;
                }
            }
            else if (field.Signature == KnownSignatures.Snam)
            {
                if (!field.ReadZString().TryGet(out description, out ParseError? error))
                {
                    return error;
                }
            }
            else if (field.Signature == KnownSignatures.Mast)
            {
                if (!field.ReadZString().TryGet(out string? masterName, out ParseError? error))
                {
                    return error;
                }

                masters.Add(new MasterReference(masterName, masters.Count));
            }
            else if (field.Signature == KnownSignatures.Onam)
            {
                if (!field.ReadFormIdArray().TryGet(out IReadOnlyList<FormId>? forms, out ParseError? error))
                {
                    return error;
                }

                overridden.AddRange(forms);
            }
            else if (field.Signature == KnownSignatures.Incc)
            {
                if (!field.ReadInt32().TryGet(out int incc, out ParseError? error))
                {
                    return error;
                }

                interiorCellCount = incc;
            }
        }

        if (!sawHedr)
        {
            return new ParseError(
                ParseErrorKind.HeaderInvalid,
                "The TES4 record contains no HEDR field.",
                data.Record.DataOffset);
        }

        return new Success<PluginHeader>(new PluginHeader(
            data.Record.Header,
            version,
            recordCount,
            nextFormId,
            author,
            description,
            masters,
            overridden,
            interiorCellCount,
            DetermineKind(data.Record.Header.Flags, sourceName)));
    }
}
