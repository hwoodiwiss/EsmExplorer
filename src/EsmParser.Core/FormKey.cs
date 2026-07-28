using EsmParser.Core.Format;

namespace EsmParser.Core;

/// <summary>
/// The load-order-independent identity of a form: the plugin that defines it plus its
/// 24-bit object id. Stored form ids are file-relative (their top byte indexes the
/// file's master list), so cross-file navigation goes through this canonical key.
/// </summary>
public sealed record FormKey(string PluginName, uint ObjectId)
{
    public override string ToString() => string.Create(
        System.Globalization.CultureInfo.InvariantCulture,
        $"{PluginName}:{ObjectId:X6}");
}
