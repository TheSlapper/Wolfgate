using Robust.Shared.Prototypes;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Sucks power out of the batteries on the cable network under the mob while it has no target, and evolves once it has
/// drained enough.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class FaunaPowerDrainComponent : Component
{
    /// <summary>
    /// Joules taken from each battery on the network per interval.
    /// </summary>
    [DataField]
    public float Rate = 75000f;

    [DataField]
    public TimeSpan Interval = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Mob it turns into once <see cref="Drained"/> reaches <see cref="EvolveAt"/>, unless an earlier evolved one of
    /// that mob is still alive.
    /// </summary>
    [DataField]
    public EntProtoId? EvolveInto;

    [DataField]
    public float EvolveAt = 32000000f;

    [DataField]
    public LocId? EvolvePopup;

    /// <summary>
    /// Shown when the mob starts draining.
    /// </summary>
    [DataField]
    public LocId? DrainPopup;

    /// <summary>
    /// Anchors the mob while it drains. A hit or a target lets it go.
    /// </summary>
    [DataField]
    public bool Anchor;

    [ViewVariables]
    public bool Draining;

    [ViewVariables]
    public float Drained;

    [DataField, AutoPausedField]
    public TimeSpan NextTick;
}

/// <summary>
/// Marks an evolved power-drain form. Only one may be alive at a time.
/// </summary>
[RegisterComponent]
public sealed partial class FaunaPowerDrainEvolvedComponent : Component;
