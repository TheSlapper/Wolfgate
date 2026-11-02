using Content.Shared.Chat.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Shakes the screens of players near this mob when it shouts. The action carries the sound.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class FaunaShoutComponent : Component
{
    [DataField]
    public float Range = 8f;

    [DataField]
    public int Shakes = 3;

    [DataField]
    public int Strength = 2;

    /// <summary>
    /// Seconds of drunken wobble given to every other mob in range. Zero gives none.
    /// </summary>
    [DataField]
    public float Disorient;

    [DataField]
    public LocId? Popup;

    /// <summary>
    /// Visual effect spawned on the mob when it shouts.
    /// </summary>
    [DataField]
    public EntProtoId? Effect;

    /// <summary>
    /// How long the mob shakes while it shouts. Zero turns it off.
    /// </summary>
    [DataField]
    public TimeSpan Jitter;

    /// <summary>
    /// Animated emote the mob plays when it shouts.
    /// </summary>
    [DataField]
    public ProtoId<EmotePrototype>? Emote;

    /// <summary>
    /// Starts the mob's timed <see cref="FaunaEnrageComponent"/> rage when it shouts.
    /// </summary>
    [DataField]
    public bool Enrage;

    [DataField, AutoPausedField]
    public TimeSpan JitterEnd;
}
