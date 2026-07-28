namespace EsmParser.Core.Format;

/// <summary>
/// The kind of a GRUP container, which determines how its label bytes are interpreted.
/// Types 0-9 match Skyrim; type 10 (<see cref="QuestChildren"/>) is a Starfield addition.
/// </summary>
public enum GroupType
{
    /// <summary>Top-level group; label is the contained record type signature.</summary>
    Top = 0,

    /// <summary>Label is the parent WRLD form id.</summary>
    WorldChildren = 1,

    /// <summary>Label is the interior cell block number.</summary>
    InteriorCellBlock = 2,

    /// <summary>Label is the interior cell sub-block number.</summary>
    InteriorCellSubBlock = 3,

    /// <summary>Label is the grid (Y, X) coordinate pair of the exterior block.</summary>
    ExteriorCellBlock = 4,

    /// <summary>Label is the grid (Y, X) coordinate pair of the exterior sub-block.</summary>
    ExteriorCellSubBlock = 5,

    /// <summary>Label is the parent CELL form id.</summary>
    CellChildren = 6,

    /// <summary>Label is the parent DIAL form id.</summary>
    TopicChildren = 7,

    /// <summary>Label is the parent CELL form id.</summary>
    CellPersistentChildren = 8,

    /// <summary>Label is the parent CELL form id.</summary>
    CellTemporaryChildren = 9,

    /// <summary>Label is the parent QUST form id (Starfield addition).</summary>
    QuestChildren = 10,
}
