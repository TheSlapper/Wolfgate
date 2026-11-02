using Content.Shared.Damage;
using Robust.Shared.Audio;

namespace Content.Server._WF.Fauna;

/// <summary>
/// When the mob steps onto a new tile, it hurts and throws every mob near it, then waits out a cooldown.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class FaunaStepSlamComponent : Component
{
    [DataField]
    public float Range = 1.5f;

    /// <summary>
    /// Range while the mob is enraged.
    /// </summary>
    [DataField]
    public float EnragedRange = 2.5f;

    [DataField]
    public DamageSpecifier? Damage;

    [DataField]
    public float Throw = 8f;

    [DataField]
    public TimeSpan Cooldown = TimeSpan.FromSeconds(10);

    [DataField]
    public SoundSpecifier? Sound;

    [DataField]
    public LocId? Popup;

    [DataField, AutoPausedField]
    public TimeSpan NextSlam;

    [ViewVariables]
    public Vector2i? LastTile;
}
