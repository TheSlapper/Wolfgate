using Robust.Shared.Serialization;

namespace Content.Shared._WF.Voice;

/// <summary>A base voice with its own sound and way of speaking, which the sliders then adjust.</summary>
[Serializable, NetSerializable]
public enum SpeechVoiceType : byte
{
    Human,
    Canid,
    Felid,
    Avian,
    Saurian,
    Rodent,
    Gelatinous,
    Sylvan,
    Arthropod,
    Lepidopteran,
    Synthetic,
}

/// <summary>The tune of the speech: how notes move between letters and across a sentence.</summary>
[Serializable, NetSerializable]
public enum SpeechVoiceMelody : byte
{
    Natural,
    Flat,
    Lively,
    SingSong,
    Questioning,
    Dramatic,
}

/// <summary>A speaking habit layered over the voice.</summary>
[Serializable, NetSerializable]
public enum SpeechVoiceQuirk : byte
{
    None,
    TrailingMumble,
    Giggle,
    Sigh,
}

/// <summary>A character's voice, picked in the creator and played when they talk.</summary>
[DataDefinition, Serializable, NetSerializable]
public sealed partial class SpeechVoice : IEquatable<SpeechVoice>
{
    public const float MinPitch = 0.5f;
    public const float MaxPitch = 2f;
    public const float MinPace = 0.7f;
    public const float MaxPace = 1.5f;

    /// <summary>The top of the multiplier sliders, as a multiple of the voice's own value.</summary>
    public const float MaxScale = 2f;

    public const float MaxPitchVariance = 4f;
    public const float MinSize = 0.6f;
    public const float MaxSize = 1.6f;

    /// <summary>How far a spoken voice carries. Matches ChatSystem.VoiceRange.</summary>
    public const float VoiceRange = 10f;

    /// <summary>How far a whispered voice carries. Matches ChatSystem.WhisperMuffledRange.</summary>
    public const float WhisperRange = 5f;

    /// <summary>How far a whisper stays clear, then it is muffled. Matches ChatSystem.WhisperClearRange.</summary>
    public const float WhisperClearRange = 2f;

    /// <summary>Whether the character speaks with this voice. Off, they use the message sound instead.</summary>
    [DataField]
    public bool Enabled = true;

    /// <summary>The base voice.</summary>
    [DataField]
    public SpeechVoiceType Type = SpeechVoiceType.Human;

    /// <summary>The tune of the speech. Pitch variance and intonation scale it.</summary>
    [DataField]
    public SpeechVoiceMelody Melody = SpeechVoiceMelody.Natural;

    /// <summary>The speaking habit, if any.</summary>
    [DataField]
    public SpeechVoiceQuirk Quirk = SpeechVoiceQuirk.None;

    /// <summary>Pitch as a multiple of the voice's own pitch. Formants rise with it.</summary>
    [DataField]
    public float Pitch = 1f;

    /// <summary>Random pitch change per letter, as a multiple of the voice's own amount.</summary>
    [DataField]
    public float PitchVariance = 1f;

    /// <summary>Speaking rate as a multiple of the voice's own, so higher is faster.</summary>
    [DataField]
    public float Pace = 1f;

    /// <summary>How far questions rise and statements fall, as a multiple of the voice's own amount.</summary>
    [DataField]
    public float Intonation = 1f;

    /// <summary>Throat size as a multiple of the voice's own. Bigger lowers the resonances, not the pitch.</summary>
    [DataField]
    public float Size = 1f;

    /// <summary>Breath in the voice as a multiple of the voice's own amount.</summary>
    [DataField]
    public float Breathiness = 1f;

    /// <summary>A copy of this voice.</summary>
    public SpeechVoice Clone()
    {
        return (SpeechVoice) MemberwiseClone();
    }

    /// <summary>An FNV-1a hash of the text, the same on every machine and every run.</summary>
    public static uint Hash(string text)
    {
        var hash = 2166136261u;
        foreach (var c in text)
        {
            hash = (hash ^ c) * 16777619u;
        }

        return hash;
    }

    /// <summary>A plain voice made from a name, the same every time for the same name.</summary>
    public static SpeechVoice FromName(string name)
    {
        var hash = Hash(name);

        float Pick(float min, float max, int shift)
        {
            return min + (max - min) * ((hash >> shift) & 0xFF) / 255f;
        }

        return new SpeechVoice
        {
            Type = SpeechVoiceType.Human,
            Pitch = Pick(0.8f, 1.25f, 1),
            PitchVariance = Pick(0.7f, 1.3f, 8),
            Pace = Pick(0.85f, 1.15f, 16),
            Intonation = Pick(0.7f, 1.3f, 24),
        };
    }

    /// <summary>A copy with every value inside its slider's range and an unknown type set to Human.</summary>
    public SpeechVoice Clamped()
    {
        var voice = Clone();
        voice.Pitch = Clamp(Pitch, MinPitch, MaxPitch, 1f);
        voice.PitchVariance = Clamp(PitchVariance, 0f, MaxPitchVariance, 1f);
        voice.Pace = Clamp(Pace, MinPace, MaxPace, 1f);
        voice.Intonation = Clamp(Intonation, 0f, MaxScale, 1f);
        voice.Size = Clamp(Size, MinSize, MaxSize, 1f);
        voice.Breathiness = Clamp(Breathiness, 0f, MaxScale, 1f);

        if (!Enum.IsDefined(Type))
            voice.Type = SpeechVoiceType.Human;

        if (!Enum.IsDefined(Melody))
            voice.Melody = SpeechVoiceMelody.Natural;

        if (!Enum.IsDefined(Quirk))
            voice.Quirk = SpeechVoiceQuirk.None;

        return voice;
    }

    /// <summary>Clamps a value, or gives the fallback for NaN and infinity, which a modified client can send.</summary>
    private static float Clamp(float value, float min, float max, float fallback)
    {
        return float.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
    }

    /// <summary>Whether every setting matches, with a small tolerance on the floats.</summary>
    public bool Equals(SpeechVoice? other)
    {
        if (other is null)
            return false;

        return Enabled == other.Enabled
               && Type == other.Type
               && Melody == other.Melody
               && Quirk == other.Quirk
               && MathHelper.CloseTo(Pitch, other.Pitch)
               && MathHelper.CloseTo(PitchVariance, other.PitchVariance)
               && MathHelper.CloseTo(Pace, other.Pace)
               && MathHelper.CloseTo(Intonation, other.Intonation)
               && MathHelper.CloseTo(Size, other.Size)
               && MathHelper.CloseTo(Breathiness, other.Breathiness);
    }

    public override bool Equals(object? obj)
    {
        return obj is SpeechVoice other && Equals(other);
    }

    /// <summary>Only the type, since the floats compare with a tolerance.</summary>
    public override int GetHashCode()
    {
        return Type.GetHashCode();
    }
}
