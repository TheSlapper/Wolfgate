using Content.Shared.Damage;
using Robust.Shared.Audio;

namespace Content.Server._WF.Fauna;

/// <summary>
/// After a delay, hurts every living mob on this tile except blast casters.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class FaunaBlastTileComponent : Component
{
    [DataField]
    public TimeSpan Delay = TimeSpan.FromSeconds(0.6);

    [DataField]
    public DamageSpecifier? Damage;

    [DataField]
    public SoundSpecifier? HitSound;

    [DataField, AutoPausedField]
    public TimeSpan StrikeAt;

    [ViewVariables]
    public bool Struck;
}
