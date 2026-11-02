using Content.Shared.Damage;
using Content.Shared.Stacks;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Fauna;

/// <summary>
/// The head of a segmented worm. Its body follows the path the head has travelled. A mob its charge hits is
/// swallowed and digested, and what it digests turns into <see cref="DigestStack"/>.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class FaunaWormComponent : Component
{
    [DataField(required: true)]
    public EntProtoId SegmentPrototype;

    [DataField]
    public int SegmentCount = 6;

    /// <summary>
    /// Living segments, from just behind the head to the tail.
    /// </summary>
    [ViewVariables]
    public List<EntityUid> Segments = new();

    /// <summary>
    /// Path from one segment to the next, in tiles.
    /// </summary>
    [DataField]
    public float SegmentSpacing = 1f;

    /// <summary>
    /// Distance the head travels between recorded path points, in tiles.
    /// </summary>
    [DataField]
    public float PointSpacing = 0.25f;

    /// <summary>
    /// Points the head passed, newest first, <see cref="PointSpacing"/> apart.
    /// </summary>
    [ViewVariables]
    public List<EntityCoordinates> Trail = new();

    [ViewVariables]
    public EntityCoordinates? LastHead;

    [DataField]
    public string StomachId = "fauna_worm_stomach";

    [ViewVariables]
    public Container Stomach = default!;

    [DataField]
    public ProtoId<StackPrototype> DigestStack = "Plasma";

    /// <summary>
    /// Sheets made from something that is neither a stack nor an item, such as a mob.
    /// </summary>
    [DataField]
    public int FlatDigestAmount = 5;

    [DataField]
    public float ProcessChance = 0.5f;

    [DataField]
    public float DigestChance = 0.2f;

    /// <summary>
    /// Dealt to a swallowed mob each time it is digested.
    /// </summary>
    [DataField]
    public DamageSpecifier DigestDamage = new();

    [DataField]
    public TimeSpan TickInterval = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Shown around the head when it swallows something.
    /// </summary>
    [DataField]
    public LocId? SwallowPopup;

    /// <summary>
    /// Shown to a swallowed mob each time it is digested.
    /// </summary>
    [DataField]
    public LocId? DigestPopup;

    /// <summary>
    /// Each tick the head swallows loose items within this many tiles. Zero turns it off.
    /// </summary>
    [DataField]
    public float SwallowItemRange;

    /// <summary>
    /// Each tick, every living part evens out its damage with the part ahead of it.
    /// </summary>
    [DataField]
    public bool ShareDamage;

    [DataField, AutoPausedField]
    public TimeSpan NextTick;
}
