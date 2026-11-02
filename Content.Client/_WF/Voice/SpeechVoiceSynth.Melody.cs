using Content.Shared._WF.Voice;

namespace Content.Client._WF.Voice;

public sealed partial class SpeechVoiceSynth
{
    /// <summary>How a melody moves the pitch from one word to the next.</summary>
    private enum WordPattern : byte
    {
        None,
        Random,
        Alternate,
        PhraseStart,
    }

    /// <summary>A speaking melody: how far notes jump, how words step and how the sentence rises and falls.</summary>
    private struct Melody
    {
        /// <summary>Scales the random note jump between letters.</summary>
        public float Spread;

        /// <summary>Scales the rise and fall at the ends of sentences.</summary>
        public float Intonation;

        /// <summary>Scales the slow pitch and loudness swing.</summary>
        public float Swing;

        /// <summary>Speaking rate on top of the Speed slider.</summary>
        public float Pace;

        /// <summary>How each word's pitch is picked.</summary>
        public WordPattern Words;

        /// <summary>How far a word's pitch moves, as a fraction of the voice's pitch.</summary>
        public float WordStep;

        /// <summary>Statements end rising, as if every sentence were a question.</summary>
        public bool Uptalk;

        public static Melody For(SpeechVoiceMelody melody)
        {
            var result = new Melody { Spread = 1f, Intonation = 1f, Swing = 1f, Pace = 1f };

            switch (melody)
            {
                case SpeechVoiceMelody.Flat:
                    result.Spread = 0f;
                    result.Intonation = 0.15f;
                    result.Swing = 0f;
                    break;
                case SpeechVoiceMelody.Lively:
                    result.Spread = 2.5f;
                    result.Intonation = 1.6f;
                    result.Swing = 2.5f;
                    result.Pace = 1.15f;
                    result.Words = WordPattern.Random;
                    result.WordStep = 0.12f;
                    break;
                case SpeechVoiceMelody.SingSong:
                    result.Spread = 0.3f;
                    result.Swing = 0.5f;
                    result.Pace = 0.95f;
                    result.Words = WordPattern.Alternate;
                    result.WordStep = 0.15f;
                    break;
                case SpeechVoiceMelody.Questioning:
                    result.Intonation = 1.5f;
                    result.Uptalk = true;
                    break;
                case SpeechVoiceMelody.Dramatic:
                    result.Spread = 1.2f;
                    result.Intonation = 2.5f;
                    result.Swing = 2f;
                    result.Pace = 0.85f;
                    result.Words = WordPattern.PhraseStart;
                    result.WordStep = 0.15f;
                    break;
            }

            return result;
        }

        /// <summary>The pitch of a new word, given whether the last word was high.</summary>
        /// <param name="random">A random value from -1 to 1.</param>
        public float WordPitch(in Blip blip, ref bool up, float random)
        {
            switch (Words)
            {
                case WordPattern.Random:
                    return 1f + WordStep * random;
                case WordPattern.Alternate:
                    up = !up;
                    return up ? 1f + WordStep : 1f - 0.67f * WordStep;
                case WordPattern.PhraseStart:
                    return blip.PhrasePosition <= 0f ? 1f + WordStep : 1f;
                default:
                    return 1f;
            }
        }
    }
}
