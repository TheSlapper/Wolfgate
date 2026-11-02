using Content.Shared.Damage;
using Robust.Shared.Audio;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Anyone who grabs or shoves the mob gets its melee hit and is thrown away.
/// </summary>
[RegisterComponent]
public sealed partial class FaunaGrabRetortComponent : Component
{
    [DataField]
    public float MinThrow = 3f;

    [DataField]
    public float MaxThrow = 6f;

    [DataField]
    public LocId? Popup;

    [DataField]
    public SoundSpecifier? Sound;

    /// <summary>
    /// Dealt instead of the mob's melee damage.
    /// </summary>
    [DataField]
    public DamageSpecifier? Damage;

    [DataField]
    public TimeSpan? Knockdown;
}
