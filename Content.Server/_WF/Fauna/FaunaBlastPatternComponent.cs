using Robust.Shared.Prototypes;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Spawns <see cref="Effect"/> on a pattern of tiles around this entity, then deletes itself.
/// </summary>
[RegisterComponent]
public sealed partial class FaunaBlastPatternComponent : Component
{
    [DataField(required: true)]
    public EntProtoId Effect;

    [DataField]
    public FaunaBlastShape Shape = FaunaBlastShape.Square;

    /// <summary>
    /// Square and ring radius, or the length of each cross arm.
    /// </summary>
    [DataField]
    public int Radius = 1;

    /// <summary>
    /// Delay between each ring of a square, so it grows outward. Zero spawns it all at once.
    /// </summary>
    [DataField]
    public TimeSpan RingDelay = TimeSpan.Zero;

    [DataField]
    public FaunaBlastCross Cross = FaunaBlastCross.Cardinal;

    /// <summary>
    /// Chance of a diagonal cross when <see cref="Cross"/> is random.
    /// </summary>
    [DataField]
    public float DiagonalChance = 0.4f;

    /// <summary>
    /// Effect spawned at the centre when the pattern is laid out. A cross uses it when it has both arms.
    /// </summary>
    [DataField]
    public EntProtoId? Telegraph;

    [DataField]
    public EntProtoId? TelegraphCardinal;

    [DataField]
    public EntProtoId? TelegraphDiagonal;

    [ViewVariables]
    public List<(TimeSpan At, Vector2i Tile)> Pending = new();
}

public enum FaunaBlastShape : byte
{
    Square,
    Cross,
    Ring,
}

public enum FaunaBlastCross : byte
{
    Cardinal,
    Diagonal,
    All,
    Random,
}
