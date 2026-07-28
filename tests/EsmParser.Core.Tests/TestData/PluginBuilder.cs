using System.Buffers;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using EsmParser.Core.Format;
using EsmParser.Core.IO;

namespace EsmParser.Core.Tests.TestData;

/// <summary>
/// Builds syntactically valid (or deliberately corrupted) plugin bytes for tests,
/// independently of the production parser: the builder only writes, the parser only reads.
/// </summary>
internal sealed class PluginBuilder
{
    private readonly List<(Signature Signature, byte[] Data)> _headerFields = [];
    private readonly NodeListBuilder _body = new();
    private RecordFlags _headerFlags = RecordFlags.Master;
    private ushort _headerFormVersion = 581;
    private bool _hedrAdded;

    public PluginBuilder WithHeaderFlags(RecordFlags flags)
    {
        _headerFlags = flags;
        return this;
    }

    public PluginBuilder WithHeaderFormVersion(ushort formVersion)
    {
        _headerFormVersion = formVersion;
        return this;
    }

    public PluginBuilder WithHedr(float version = 0.96f, uint recordCount = 0, uint nextFormId = 0x800)
    {
        byte[] data = new byte[12];
        BinaryPrimitives.WriteSingleLittleEndian(data, version);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), recordCount);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8), nextFormId);
        _headerFields.Add((KnownSignatures.Hedr, data));
        _hedrAdded = true;
        return this;
    }

    public PluginBuilder WithAuthor(string author)
    {
        _headerFields.Add((KnownSignatures.Cnam, ZString(author)));
        return this;
    }

    public PluginBuilder WithDescription(string description)
    {
        _headerFields.Add((KnownSignatures.Snam, ZString(description)));
        return this;
    }

    public PluginBuilder WithMaster(string fileName)
    {
        _headerFields.Add((KnownSignatures.Mast, ZString(fileName)));
        return this;
    }

    public PluginBuilder WithOverriddenForms(params uint[] formIds)
    {
        byte[] data = new byte[formIds.Length * 4];
        for (int i = 0; i < formIds.Length; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(i * 4), formIds[i]);
        }

        _headerFields.Add((KnownSignatures.Onam, data));
        return this;
    }

    public PluginBuilder WithHeaderField(string signature, byte[] data)
    {
        _headerFields.Add((Signature.FromString(signature), data));
        return this;
    }

    public PluginBuilder AddRecord(
        string signature,
        uint formId,
        Action<RecordFieldsBuilder>? fields = null,
        RecordFlags flags = RecordFlags.None,
        ushort timestamp = 0,
        ushort formVersion = 581)
    {
        _body.AddRecord(signature, formId, fields, flags, timestamp, formVersion);
        return this;
    }

    public PluginBuilder AddRecordWithRawData(string signature, uint formId, byte[] rawData, RecordFlags flags = RecordFlags.None)
    {
        _body.AddRecordWithRawData(signature, formId, rawData, flags);
        return this;
    }

    public PluginBuilder AddGroup(GroupType type, uint rawLabel, Action<NodeListBuilder>? children = null)
    {
        _body.AddGroup(type, rawLabel, children);
        return this;
    }

    public PluginBuilder AddTopGroup(string recordType, Action<NodeListBuilder>? children = null)
    {
        _body.AddTopGroup(recordType, children);
        return this;
    }

    public PluginBuilder AddRawBytes(params byte[] bytes)
    {
        _body.AddRawBytes(bytes);
        return this;
    }

    public byte[] Build()
    {
        if (!_hedrAdded)
        {
            WithHedr();
            // Move the implicit HEDR to the front so it precedes any explicitly-added fields.
            (Signature, byte[]) hedr = _headerFields[^1];
            _headerFields.RemoveAt(_headerFields.Count - 1);
            _headerFields.Insert(0, hedr);
        }

        byte[] headerData = NodeListBuilder.BuildFieldBytes(_headerFields);
        var file = new ArrayBufferWriter<byte>();
        var recordHeader = new RecordHeader(
            KnownSignatures.Tes4,
            (uint)headerData.Length,
            _headerFlags,
            new FormId(0),
            Timestamp: 0,
            VersionControlInfo: 0,
            FormVersion: _headerFormVersion,
            Unknown: 0);
        Span<byte> header = stackalloc byte[RecordHeader.Size];
        recordHeader.WriteTo(header);
        file.Write(header);
        file.Write(headerData);
        file.Write(_body.WrittenBytes);
        return file.WrittenSpan.ToArray();
    }

    public MemoryDataSource BuildSource(string name = "Test.esm") => new(Build(), name);

    internal static byte[] ZString(string value)
    {
        byte[] text = Encoding.UTF8.GetBytes(value);
        byte[] data = new byte[text.Length + 1];
        text.CopyTo(data, 0);
        return data;
    }
}

/// <summary>Builds a sequence of sibling nodes (records, groups, raw bytes).</summary>
internal sealed class NodeListBuilder
{
    private readonly ArrayBufferWriter<byte> _content = new();

    internal ReadOnlySpan<byte> WrittenBytes => _content.WrittenSpan;

    public NodeListBuilder AddRecord(
        string signature,
        uint formId,
        Action<RecordFieldsBuilder>? fields = null,
        RecordFlags flags = RecordFlags.None,
        ushort timestamp = 0,
        ushort formVersion = 581)
    {
        var fieldsBuilder = new RecordFieldsBuilder();
        fields?.Invoke(fieldsBuilder);
        byte[] data = fieldsBuilder.Build();

        if ((flags & RecordFlags.Compressed) != 0)
        {
            data = Compress(data);
        }

        return AddRecordWithRawData(signature, formId, data, flags, timestamp, formVersion);
    }

    public NodeListBuilder AddRecordWithRawData(
        string signature,
        uint formId,
        byte[] rawData,
        RecordFlags flags = RecordFlags.None,
        ushort timestamp = 0,
        ushort formVersion = 581)
    {
        var header = new RecordHeader(
            Signature.FromString(signature),
            (uint)rawData.Length,
            flags,
            new FormId(formId),
            timestamp,
            VersionControlInfo: 0,
            formVersion,
            Unknown: 0);
        Span<byte> headerBytes = stackalloc byte[RecordHeader.Size];
        header.WriteTo(headerBytes);
        _content.Write(headerBytes);
        _content.Write(rawData);
        return this;
    }

    public NodeListBuilder AddGroup(GroupType type, uint rawLabel, Action<NodeListBuilder>? children = null)
    {
        var childBuilder = new NodeListBuilder();
        children?.Invoke(childBuilder);
        ReadOnlySpan<byte> content = childBuilder.WrittenBytes;

        var header = new GroupHeader(
            (uint)(GroupHeader.Size + content.Length),
            rawLabel,
            type,
            Timestamp: 0,
            VersionControlInfo: 0,
            Unknown: 0);
        Span<byte> headerBytes = stackalloc byte[GroupHeader.Size];
        header.WriteTo(headerBytes);
        _content.Write(headerBytes);
        _content.Write(content);
        return this;
    }

    public NodeListBuilder AddTopGroup(string recordType, Action<NodeListBuilder>? children = null)
        => AddGroup(GroupType.Top, Signature.FromString(recordType).Value, children);

    public NodeListBuilder AddRawBytes(params byte[] bytes)
    {
        _content.Write(bytes);
        return this;
    }

    internal static byte[] BuildFieldBytes(IReadOnlyList<(Signature Signature, byte[] Data)> fields)
    {
        var writer = new ArrayBufferWriter<byte>();
        Span<byte> header = stackalloc byte[6];
        foreach ((Signature signature, byte[] data) in fields)
        {
            signature.WriteTo(header);
            BinaryPrimitives.WriteUInt16LittleEndian(header[4..], checked((ushort)data.Length));
            writer.Write(header);
            writer.Write(data);
        }

        return writer.WrittenSpan.ToArray();
    }

    private static byte[] Compress(byte[] data)
    {
        using var output = new MemoryStream();
        Span<byte> sizePrefix = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(sizePrefix, (uint)data.Length);
        output.Write(sizePrefix);
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(data);
        }

        return output.ToArray();
    }
}

/// <summary>Builds a record's field payload, with optional forced XXXX extended-size encoding.</summary>
internal sealed class RecordFieldsBuilder
{
    private readonly ArrayBufferWriter<byte> _content = new();

    public RecordFieldsBuilder AddField(string signature, params byte[] data)
    {
        Span<byte> header = stackalloc byte[6];
        Signature.FromString(signature).WriteTo(header);
        BinaryPrimitives.WriteUInt16LittleEndian(header[4..], checked((ushort)data.Length));
        _content.Write(header);
        _content.Write(data);
        return this;
    }

    public RecordFieldsBuilder AddZString(string signature, string value)
        => AddField(signature, PluginBuilder.ZString(value));

    public RecordFieldsBuilder AddUInt32(string signature, uint value)
    {
        byte[] data = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(data, value);
        return AddField(signature, data);
    }

    /// <summary>Emits an XXXX size-carrier followed by the field with a zero declared size, per the spec.</summary>
    public RecordFieldsBuilder AddFieldWithXxxx(string signature, byte[] data)
    {
        Span<byte> xxxx = stackalloc byte[10];
        KnownSignatures.Xxxx.WriteTo(xxxx);
        BinaryPrimitives.WriteUInt16LittleEndian(xxxx[4..], 4);
        BinaryPrimitives.WriteUInt32LittleEndian(xxxx[6..], (uint)data.Length);
        _content.Write(xxxx);

        Span<byte> header = stackalloc byte[6];
        Signature.FromString(signature).WriteTo(header);
        BinaryPrimitives.WriteUInt16LittleEndian(header[4..], 0);
        _content.Write(header);
        _content.Write(data);
        return this;
    }

    public RecordFieldsBuilder AddRawBytes(params byte[] bytes)
    {
        _content.Write(bytes);
        return this;
    }

    public byte[] Build() => _content.WrittenSpan.ToArray();
}
