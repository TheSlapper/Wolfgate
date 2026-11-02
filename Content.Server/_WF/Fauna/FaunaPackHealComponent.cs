using Content.Shared.Damage;
using Content.Shared.NPC.Prototypes;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Licks the wounds of hurt packmates nearby, once per interval. Packmates are other mobs with this component, or the
/// members of <see cref="Faction"/> when it is set.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class FaunaPackHealComponent : Component
{
    [DataField(required: true)]
    public DamageSpecifier Heal = new();

    [DataField]
    public float Radius = 1.5f;

    [DataField]
    public ProtoId<NpcFactionPrototype>? Faction;

    [DataField]
    public TimeSpan Interval = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Played on each packmate it heals.
    /// </summary>
    [DataField]
    public SoundSpecifier? Sound;

    [DataField, AutoPausedField]
    public TimeSpan NextTick;
}
