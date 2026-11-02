using Content.Shared.Chat.Prototypes;
using Content.Shared.Damage;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Fauna;

/// <summary>
/// After a wind-up, damages and knocks down every mob in a cone in front of this one.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class FaunaSlamComponent : Component
{
    [DataField(required: true)]
    public DamageSpecifier Damage = new();

    /// <summary>
    /// Damage dealt to anchored structures in the cone, such as walls and tables.
    /// </summary>
    [DataField]
    public DamageSpecifier? StructureDamage;

    [DataField]
    public float Range = 3f;

    /// <summary>
    /// Full width of the cone, in degrees.
    /// </summary>
    [DataField]
    public float Arc = 90f;

    [DataField]
    public TimeSpan Delay = TimeSpan.FromSeconds(2);

    [DataField]
    public TimeSpan Knockdown = TimeSpan.FromSeconds(3);

    [DataField]
    public SoundSpecifier? HitSound;

    /// <summary>
    /// Tiles each hit mob is thrown away from the slammer. Zero throws nothing.
    /// </summary>
    [DataField]
    public float Throw;

    /// <summary>
    /// Marker spawned on every tile of the cone during the wind-up.
    /// </summary>
    [DataField]
    public EntProtoId? Telegraph;

    /// <summary>
    /// Visual effect spawned on every tile of the cone when the slam lands.
    /// </summary>
    [DataField]
    public EntProtoId? Effect;

    /// <summary>
    /// Animated emote the mob plays when it starts the wind-up.
    /// </summary>
    [DataField]
    public ProtoId<EmotePrototype>? Emote;

    [DataField, AutoPausedField]
    public TimeSpan? SlamAt;

    [ViewVariables]
    public Angle Direction;
}
