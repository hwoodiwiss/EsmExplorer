namespace EsmParser.Core.Format;

/// <summary>
/// A 32-bit form identifier. The top byte indexes into the plugin's master list
/// (with the value one past the last master meaning the plugin itself), and the
/// low 24 bits are the object id within that plugin.
/// </summary>
public readonly record struct FormId(uint Value)
{
    /// <summary>Index into the owning plugin's master list (load-order byte within the file).</summary>
    public byte MasterIndex => (byte)(Value >> 24);

    /// <summary>The object id portion (low 24 bits).</summary>
    public uint ObjectId => Value & 0x00FF_FFFF;

    public bool IsNull => Value == 0;

    /// <summary>Renders the form id in the conventional 8-digit hex form, e.g. <c>0004F2B1</c>.</summary>
    public override string ToString() => Value.ToString("X8", System.Globalization.CultureInfo.InvariantCulture);
}
