using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Puffs the mob up when it finds a target: it bites, smashes walls and shrugs off most damage for a while, then
/// shrinks back and rests before it can inflate again.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class FaunaInflateComponent : Component
{
    [DataField]
    public TimeSpan Duration = TimeSpan.FromSeconds(10);

    [DataField]
    public TimeSpan Cooldown = TimeSpan.FromSeconds(8);

    [DataField(required: true)]
    public DamageSpecifier Damage = new();

    [DataField]
    public ProtoId<DamageModifierSetPrototype>? ModifierSet;

    [DataField]
    public float Speed = 2.2f;

    [DataField]
    public LocId? Popup;

    [ViewVariables]
    public bool Inflated;

    [DataField, AutoPausedField]
    public TimeSpan NextChange;

    /// <summary>
    /// Damage modifier set to restore when the mob deflates.
    /// </summary>
    [ViewVariables]
    public string? NormalModifierSet;
}
