using Robust.Shared.Map;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Teleports the mob back to where it spawned once it has had no target for a while, healing part of its damage.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class FaunaLeashComponent : Component
{
    [DataField]
    public TimeSpan Timeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Only leashes when farther than this from home.
    /// </summary>
    [DataField]
    public float Range;

    /// <summary>
    /// Fraction of the damage taken that is healed on the way home.
    /// </summary>
    [DataField]
    public float HealFraction;

    [DataField]
    public float MinHeal;

    [DataField]
    public LocId? Popup;

    [ViewVariables]
    public EntityCoordinates? Home;

    [DataField, AutoPausedField]
    public TimeSpan LastTarget;

    [DataField, AutoPausedField]
    public TimeSpan NextCheck;
}
