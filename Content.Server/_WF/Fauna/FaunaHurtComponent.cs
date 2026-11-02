using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Plays a pain sound or spawns something at the mob when it takes damage, at most once per <see cref="Cooldown"/>.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class FaunaHurtComponent : Component
{
    [DataField]
    public SoundSpecifier? Sound;

    [DataField]
    public EntProtoId? Spawn;

    [DataField]
    public TimeSpan Cooldown = TimeSpan.FromSeconds(2);

    [DataField, AutoPausedField]
    public TimeSpan NextHurt;
}
