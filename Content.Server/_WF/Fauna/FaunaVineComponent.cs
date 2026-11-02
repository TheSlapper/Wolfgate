using Robust.Shared.Prototypes;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Lashes vines onto hostile mobs in sight, knocks them down and drags them a tile closer every interval.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class FaunaVineComponent : Component
{
    [DataField(required: true)]
    public EntProtoId Beam;

    [DataField]
    public int MaxVines = 4;

    [DataField]
    public float Range = 5f;

    [DataField]
    public TimeSpan AttachCooldown = TimeSpan.FromSeconds(4.5);

    [DataField]
    public TimeSpan PullInterval = TimeSpan.FromSeconds(2);

    [DataField]
    public TimeSpan Knockdown = TimeSpan.FromSeconds(1.2);

    [DataField]
    public LocId? Popup;

    /// <summary>
    /// When no new target can be vined, pulls every vined target in at once and retries after half the cooldown.
    /// </summary>
    [DataField]
    public bool YankAll;

    [ViewVariables]
    public List<EntityUid> Targets = new();

    [DataField, AutoPausedField]
    public TimeSpan NextAttach;

    [DataField, AutoPausedField]
    public TimeSpan NextPull;
}
