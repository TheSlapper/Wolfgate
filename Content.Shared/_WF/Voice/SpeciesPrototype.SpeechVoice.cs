using Content.Shared._WF.Voice;

namespace Content.Shared.Humanoid.Prototypes;

public sealed partial class SpeciesPrototype
{
    /// <summary>The voice type a character of this species starts on when the species is picked.</summary>
    [DataField]
    public SpeechVoiceType SpeechVoice { get; private set; } = SpeechVoiceType.Human;
}
