using System.Numerics;
using Content.Shared._WF.Voice;

namespace Content.Client._WF.Voice;

public sealed partial class SpeechVoiceSynth
{
    /// <summary>A post effect laid over the whole line.</summary>
    private enum VoiceEffect : byte
    {
        None,
        Robot,
        Insect,
    }

    /// <summary>A base voice: its sound and its way of speaking. The sliders scale it.</summary>
    private struct Style
    {
        public float F0Scale;
        public float FormantScale;
        public float Rd;
        public float Tilt;
        public float Breath;
        public float Jitter;
        public float Shimmer;
        public float Growl;
        public float Wobble;
        public float FricationBoost;

        /// <summary>Seconds per letter at pace 1.</summary>
        public float LetterSeconds;

        /// <summary>Scales the pauses between words and phrases.</summary>
        public float GapScale;

        /// <summary>Random pitch change per letter, as a fraction of the pitch.</summary>
        public float PitchVariance;

        /// <summary>How far questions rise and statements fall.</summary>
        public float Intonation;

        /// <summary>Built-in pitch shake depth, as a fraction of the pitch.</summary>
        public float VibratoDepth;

        public float VibratoRate;

        /// <summary>Corner of the gentle low-pass over the line, in Hz.</summary>
        public float LowPass;

        public VoiceEffect Effect;

        /// <summary>Alternates glottal periods strong and weak, putting a rough tone an octave down.</summary>
        public float Subharmonic;

        /// <summary>Scales the pitch step between letters.</summary>
        public float PitchSpread;

        /// <summary>How far every vowel is pulled toward <see cref="VowelTarget"/>.</summary>
        public float VowelPull;

        public Vector3 VowelTarget;

        /// <summary>Lengthens s, sh, z and th.</summary>
        public float FricativeStretch;

        /// <summary>A fast pitch flutter on l and r.</summary>
        public float Trill;

        /// <summary>Depth of a 15 Hz loudness ripple on vowels.</summary>
        public float Bubble;

        /// <summary>Strength of a short feedback comb.</summary>
        public float Hollow;

        /// <summary>A 25 Hz flutter in loudness over the whole line.</summary>
        public float Purr;

        /// <summary>Widens the noise band of s, z and sh into a bright hiss.</summary>
        public float HissWidth;

        /// <summary>At half or more, each phrase ends on a long s.</summary>
        public float HissTail;

        /// <summary>Short damped sine pops on each vowel.</summary>
        public float Pops;

        /// <summary>Extra breath that rises and falls at 2.5 Hz.</summary>
        public float BreathGust;

        /// <summary>The hollow comb's delay in seconds, which sets its body note.</summary>
        public float HollowDelay;

        /// <summary>Depth of a wing-beat loudness flutter at <see cref="FlutterRate"/>.</summary>
        public float Flutter;

        public float FlutterRate;

        /// <summary>Depth of a 125 Hz loudness buzz.</summary>
        public float Buzz;

        /// <summary>Multiplies the formant bandwidths. Narrow rings and sounds metallic, wide sounds damp.</summary>
        public float Bandwidth;

        /// <summary>How nasal the voice is, 0 to 1.</summary>
        public float Nasal;

        /// <summary>Soft saturation drive. At 1 or below the sound stays clean.</summary>
        public float Drive;

        /// <summary>Share of the voiced tone replaced by whispered noise through the formants.</summary>
        public float Whisper;

        /// <summary>Boosts the harmonic nearest 1.8 kHz, so one overtone sings over the voice.</summary>
        public float Whistle;

        public static readonly Style Neutral = new()
        {
            F0Scale = 1f,
            FormantScale = 1f,
            Rd = 1f,
            Tilt = 1f,
            Breath = 0.05f,
            Jitter = 0.005f,
            Shimmer = 0.03f,
            FricationBoost = 1f,
            LetterSeconds = 0.05f,
            GapScale = 1f,
            PitchVariance = 0.1f,
            Intonation = 0.2f,
            VibratoRate = 5f,
            LowPass = 7500f,
            PitchSpread = 1f,
            FricativeStretch = 1f,
            HissWidth = 1f,
            HollowDelay = 0.0035f,
            FlutterRate = 26f,
            Bandwidth = 1f,
        };

        public static Style For(SpeechVoiceType type)
        {
            var style = Neutral;

            switch (type)
            {
                case SpeechVoiceType.Human:
                    style.F0Scale = 0.8f;
                    style.FormantScale = 0.88f;
                    style.Rd = 1.2f;
                    style.Breath = 0.08f;
                    style.Intonation = 0.25f;
                    break;
                case SpeechVoiceType.Canid:
                    style.F0Scale = 0.7f;
                    style.FormantScale = 0.75f;
                    style.Rd = 0.7f;
                    style.Breath = 0.3f;
                    style.Jitter = 0.012f;
                    style.Shimmer = 0.05f;
                    style.Growl = 0.25f;
                    style.Subharmonic = 0.35f;
                    style.Drive = 1.5f;
                    style.LetterSeconds = 0.045f;
                    style.VowelPull = 0.45f;
                    style.VowelTarget = Uh;
                    break;
                case SpeechVoiceType.Felid:
                    style.F0Scale = 1.3f;
                    style.FormantScale = 1.15f;
                    style.Rd = 1.2f;
                    style.Breath = 0.1f;
                    style.Purr = 1f;
                    style.Nasal = 0.6f;
                    style.PitchVariance = 0.12f;
                    break;
                case SpeechVoiceType.Avian:
                    style.F0Scale = 2.3f;
                    style.FormantScale = 1.6f;
                    style.Rd = 3f;
                    style.Tilt = 2.2f;
                    style.Breath = 0.02f;
                    style.Jitter = 0.002f;
                    style.LetterSeconds = 0.042f;
                    style.PitchSpread = 2f;
                    style.Intonation = 0.3f;
                    style.VowelPull = 0.4f;
                    style.VowelTarget = Ee;
                    style.Trill = 0.6f;
                    style.Whistle = 0.5f;
                    style.LowPass = 9000f;
                    break;
                case SpeechVoiceType.Saurian:
                    style.F0Scale = 0.8f;
                    style.FormantScale = 0.9f;
                    style.Rd = 1.3f;
                    style.Breath = 0.12f;
                    style.FricationBoost = 1.8f;
                    style.FricativeStretch = 1.8f;
                    style.HissWidth = 2.2f;
                    style.HissTail = 1f;
                    style.Growl = 0.15f;
                    style.Drive = 1.3f;
                    style.PitchSpread = 0.3f;
                    style.LetterSeconds = 0.058f;
                    style.LowPass = 9000f;
                    break;
                case SpeechVoiceType.Rodent:
                    style.F0Scale = 2.2f;
                    style.FormantScale = 1.6f;
                    style.Tilt = 1.2f;
                    style.Breath = 0.02f;
                    style.LetterSeconds = 0.039f;
                    style.Nasal = 0.5f;
                    style.LowPass = 9000f;
                    break;
                case SpeechVoiceType.Gelatinous:
                    style.F0Scale = 0.9f;
                    style.FormantScale = 0.9f;
                    style.Rd = 2.2f;
                    style.Tilt = 1.6f;
                    style.Wobble = 1.6f;
                    style.Bubble = 1f;
                    style.Pops = 1f;
                    style.Bandwidth = 1.8f;
                    break;
                case SpeechVoiceType.Sylvan:
                    style.F0Scale = 0.95f;
                    style.FormantScale = 0.95f;
                    style.Rd = 1.8f;
                    style.Tilt = 1.2f;
                    style.Breath = 0.12f;
                    style.BreathGust = 1f;
                    style.LetterSeconds = 0.06f;
                    style.GapScale = 1.3f;
                    style.VowelPull = 0.35f;
                    style.VowelTarget = Oo;
                    style.Hollow = 1f;
                    style.HollowDelay = 0.005f;
                    style.Bandwidth = 1.5f;
                    style.Whisper = 0.25f;
                    break;
                case SpeechVoiceType.Arthropod:
                    style.F0Scale = 1.1f;
                    style.FormantScale = 1.1f;
                    style.LetterSeconds = 0.045f;
                    style.Effect = VoiceEffect.Insect;
                    style.Bandwidth = 0.6f;
                    break;
                // Measured from the moth emotes: a buzz near 450 to 650 Hz heard mostly in its upper harmonics,
                // a rattle about 26 times a second and a pitch that falls through each call.
                case SpeechVoiceType.Lepidopteran:
                    style.F0Scale = 2.1f;
                    style.FormantScale = 1.25f;
                    style.Rd = 1.2f;
                    style.Tilt = 0.8f;
                    style.Breath = 0.15f;
                    style.Jitter = 0.004f;
                    style.FricationBoost = 1.3f;
                    style.LetterSeconds = 0.045f;
                    style.Intonation = 0.25f;
                    style.Flutter = 0.3f;
                    style.Buzz = 0.15f;
                    style.Whisper = 0.35f;
                    style.LowPass = 10000f;
                    break;
                case SpeechVoiceType.Synthetic:
                    style.Breath = 0f;
                    style.Jitter = 0f;
                    style.Shimmer = 0f;
                    style.PitchVariance = 0.04f;
                    style.Intonation = 0.1f;
                    style.Effect = VoiceEffect.Robot;
                    style.Bandwidth = 0.5f;
                    break;
            }

            return style;
        }
    }

    /// <summary>The consonant a voice puts before words that start with a vowel.</summary>
    private static Consonant WordOnsetFor(SpeechVoiceType type)
    {
        return type switch
        {
            SpeechVoiceType.Canid => R,
            SpeechVoiceType.Felid => M,
            _ => default,
        };
    }

    /// <summary>How a voice bends one point of a word: pitch multiplier and a pull on the formants.</summary>
    private struct Shape
    {
        public float F0;

        /// <summary>Multiplies the first formant.</summary>
        public float F1;

        /// <summary>Multiplies the second formant.</summary>
        public float F2;

        public float Pull;
        public Vector3 Formants;
    }

    /// <summary>A voice's pitch and formant bend at one point of a word.</summary>
    /// <param name="t">Position in the letter, 0 to 1.</param>
    /// <param name="wordT">Position in the word, 0 to 1.</param>
    private static Shape ShapeFor(SpeechVoiceType type, float t, float wordT)
    {
        var shape = new Shape { F0 = 1f, F1 = 1f, F2 = 1f };

        switch (type)
        {
            case SpeechVoiceType.Canid:
                shape.F0 = 1.05f - 0.25f * t;
                break;
            case SpeechVoiceType.Felid:
                shape.F0 = 0.85f + 0.45f * MathF.Sin(MathF.PI * wordT);
                shape.Pull = 0.7f;
                shape.Formants = wordT < 0.4f
                    ? Vector3.Lerp(new Vector3(300, 2300, 3000), new Vector3(800, 1300, 2600), wordT / 0.4f)
                    : Vector3.Lerp(new Vector3(800, 1300, 2600), new Vector3(450, 900, 2300), (wordT - 0.4f) / 0.6f);
                break;
            case SpeechVoiceType.Avian:
                shape.F0 = 1f + 0.35f * t;
                break;
            case SpeechVoiceType.Rodent:
                shape.F0 = 0.75f + 0.3f * (1f - MathF.Exp(-t / 0.12f)) + 0.15f * t;
                break;
            case SpeechVoiceType.Gelatinous:
                shape.F0 = 1f + 0.5f * t;
                shape.F1 = t < 0.33f ? 0.55f + 0.45f * t / 0.33f : 1f;
                shape.F2 = shape.F1;
                break;
            case SpeechVoiceType.Sylvan:
                shape.F0 = 1f - 0.12f * MathF.Sin(MathF.PI * wordT) + 0.03f * MathF.Sin(MathF.Tau * 2f * wordT);
                break;
            case SpeechVoiceType.Lepidopteran:
                shape.F0 = 1.12f - 0.3f * wordT;
                break;
        }

        return shape;
    }
}
