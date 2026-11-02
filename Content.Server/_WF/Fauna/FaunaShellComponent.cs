using Content.Shared.Damage.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Fauna;

/// <summary>
/// A hard shell that an explosion flips over. While flipped the mob is stunned and takes damage through
/// <see cref="FlippedModifierSet"/>, until it rights itself. A retractable shell opens to
/// <see cref="OpenModifierSet"/> for a while each time the mob attacks.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class FaunaShellComponent : Component
{
    [DataField(required: true)]
    public ProtoId<DamageModifierSetPrototype> ShellModifierSet;

    [DataField]
    public ProtoId<DamageModifierSetPrototype>? FlippedModifierSet;

    [DataField]
    public TimeSpan Duration = TimeSpan.FromSeconds(20);

    [DataField]
    public bool FlipOnExplode = true;

    /// <summary>
    /// Used while the shell is open after an attack. Null means it never opens.
    /// </summary>
    [DataField]
    public ProtoId<DamageModifierSetPrototype>? OpenModifierSet;

    [DataField]
    public TimeSpan OpenDuration = TimeSpan.FromSeconds(3);

    [DataField]
    public bool Open;

    [DataField, AutoPausedField]
    public TimeSpan CloseAt;

    [DataField]
    public bool Flipped;

    [DataField, AutoPausedField]
    public TimeSpan RightAt;
}
