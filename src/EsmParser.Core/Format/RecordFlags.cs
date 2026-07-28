namespace EsmParser.Core.Format;

/// <summary>
/// Record header flags for Starfield plugins. Named values cover the general and
/// TES4-header flags; many other bits are record-type specific.
/// </summary>
/// <remarks>
/// The plugin-scale flags (<see cref="Master"/>, <see cref="Localized"/>, <see cref="LightMaster"/>,
/// <see cref="Overlay"/>, <see cref="MediumMaster"/>) only carry that meaning on the TES4 header
/// record. Note that Starfield's light-master bit (0x100) differs from Skyrim SE's (0x200).
/// </remarks>
[Flags]
#pragma warning disable CA1028 // Flags cover the full 32-bit range on disk (0x80000000), so the backing type is uint.
public enum RecordFlags : uint
#pragma warning restore CA1028
{
    None = 0,

    /// <summary>(TES4) Plugin is a master file.</summary>
    Master = 0x0000_0001,

    DeletedGroup = 0x0000_0010,

    Deleted = 0x0000_0020,

    Constant = 0x0000_0040,

    /// <summary>(TES4) Strings are indices into .STRINGS/.DLSTRINGS/.ILSTRINGS files.</summary>
    Localized = 0x0000_0080,

    /// <summary>(TES4) Small/light master (ESL); Starfield-specific bit position.</summary>
    LightMaster = 0x0000_0100,

    /// <summary>(TES4) Overlay plugin (Starfield; deprecated by later game updates).</summary>
    Overlay = 0x0000_0200,

    /// <summary>(TES4) Medium master (Starfield).</summary>
    MediumMaster = 0x0000_0400,

    InitiallyDisabled = 0x0000_0800,

    Ignored = 0x0000_1000,

    VisibleWhenDistant = 0x0000_8000,

    Dangerous = 0x0002_0000,

    /// <summary>Record data is zlib-compressed.</summary>
    Compressed = 0x0004_0000,

    CantWait = 0x0008_0000,

    IsMarker = 0x0080_0000,

    Obstacle = 0x0200_0000,

    NavMeshGenFilter = 0x0400_0000,

    NavMeshGenBoundingBox = 0x0800_0000,

    NavMeshGenGround = 0x4000_0000,

    MultiBound = 0x8000_0000,
}

public static class RecordFlagsExtensions
{
    /// <summary>
    /// Decomposes the flag value into human-readable names, one per set bit.
    /// Bits without a known name are rendered as <c>0x00004000</c>-style hex values.
    /// </summary>
    public static IReadOnlyList<string> GetFlagNames(this RecordFlags flags)
    {
        if (flags == RecordFlags.None)
        {
            return [];
        }

        var names = new List<string>();
        uint remaining = (uint)flags;
        for (int bit = 0; bit < 32; bit++)
        {
            uint mask = 1u << bit;
            if ((remaining & mask) == 0)
            {
                continue;
            }

            var single = (RecordFlags)mask;
            names.Add(Enum.IsDefined(single)
                ? single.ToString()
                : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"0x{mask:X8}"));
        }

        return names;
    }
}
