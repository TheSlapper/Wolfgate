using Robust.Shared.Audio;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Fauna;

/// <summary>
/// After a wind-up, spawns an effect on each tile of a line toward the target, one tile at a time.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class FaunaLineAttackComponent : Component
{
    [DataField(required: true)]
    public EntProtoId Effect;

    [DataField]
    public int Range = 3;

    /// <summary>
    /// Fires only along a cardinal direction, and only when the target stands within half a tile of that line.
    /// </summary>
    [DataField]
    public bool Cardinal;

    /// <summary>
    /// Turns each effect to face along the line, so directional beam states show the matching direction.
    /// </summary>
    [DataField]
    public bool RotateEffects;

    [DataField]
    public TimeSpan Windup = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Delay between tiles. Zero spawns the whole line at once.
    /// </summary>
    [DataField]
    public TimeSpan StepDelay = TimeSpan.FromSeconds(0.15);

    /// <summary>
    /// Spawned over the whole line <see cref="StrikeDelay"/> after it is drawn, so the first effects can be a
    /// harmless warning.
    /// </summary>
    [DataField]
    public EntProtoId? Strike;

    [DataField]
    public TimeSpan StrikeDelay = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Replace the first and last effect of the line, for beam caps.
    /// </summary>
    [DataField]
    public EntProtoId? EffectStart;

    [DataField]
    public EntProtoId? EffectEnd;

    /// <summary>
    /// Replace the first and last strike of the line.
    /// </summary>
    [DataField]
    public EntProtoId? StrikeStart;

    [DataField]
    public EntProtoId? StrikeEnd;

    /// <summary>
    /// How long the mob keeps its firing look after the line is done.
    /// </summary>
    [DataField]
    public TimeSpan FiringTime;

    [DataField]
    public SoundSpecifier? StrikeSound;

    [DataField]
    public SoundSpecifier? WindupSound;

    [DataField]
    public SoundSpecifier? FireSound;

    [DataField]
    public LocId? WindupPopup;

    /// <summary>
    /// Shakes the mob this hard during the wind-up. 0 for no shake.
    /// </summary>
    [DataField]
    public float WindupJitter;

    [DataField]
    public EntityUid? Target;

    /// <summary>
    /// When the wind-up ends and the line fires. Null when no wind-up is running.
    /// </summary>
    [DataField, AutoPausedField]
    public TimeSpan? FireAt;

    [ViewVariables]
    public Queue<EntityCoordinates> Tiles = new();

    [ViewVariables]
    public Angle LineAngle;

    [ViewVariables]
    public List<EntityCoordinates> StrikeTiles = new();

    [DataField, AutoPausedField]
    public TimeSpan StrikeAt;

    [DataField, AutoPausedField]
    public TimeSpan IdleAt;

    [ViewVariables]
    public bool FirstTile;

    [DataField, AutoPausedField]
    public TimeSpan NextStep;
}
