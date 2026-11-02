using Robust.Shared.Audio;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Plays a cry and a popup when the mob's NPC picks a new living target.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class FaunaAggroCryComponent : Component
{
    [DataField]
    public SoundSpecifier? Sound;

    [DataField]
    public LocId? Popup;

    [DataField]
    public TimeSpan Cooldown = TimeSpan.FromSeconds(15);

    [ViewVariables]
    public EntityUid? LastTarget;

    [DataField, AutoPausedField]
    public TimeSpan NextCry;
}
