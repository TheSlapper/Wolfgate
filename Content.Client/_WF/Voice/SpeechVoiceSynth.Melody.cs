using System.Numerics;
using Content.Shared._WF.Voice;

namespace Content.Client._WF.Voice;

public sealed partial class SpeechVoiceSynth
{
    /// <summary>Scale steps in semitones that a sung note snaps to, a five-note scale.</summary>
    private static readonly int[] SungSteps = { 0, 2, 4, 7, 9, 12 };

    /// <summary>The settings a melody gives the per-letter loop.</summary>
    private struct Melody
    {
        /// <summary>Scales the random note jump between letters.</summary>
        public float Spread;

        /// <summary>Scales the slow pitch and loudness swing.</summary>
        public float Swing;

        /// <summary>Each letter's note snaps to the five-note scale and holds steady.</summary>
        public bool Snap;

        /// <summary>Scales the rise and fall at the ends of sentences.</summary>
        public float Intonation;

        public static Melody For(SpeechVoiceMelody melody)
        {
            return melody switch
            {
                SpeechVoiceMelody.EvenBeat => new Melody { Spread = 0.3f, Swing = 0.3f, Intonation = 1f },
                SpeechVoiceMelody.Tones => new Melody { Spread = 0.15f, Swing = 0f, Intonation = 1f },
                SpeechVoiceMelody.Sung => new Melody { Spread = 3f, Swing = 0f, Snap = true, Intonation = 1f },
                SpeechVoiceMelody.Cascade => new Melody { Spread = 0.1f, Swing = 0f, Intonation = 0f },
                SpeechVoiceMelody.Leaps => new Melody { Spread = 0.3f, Swing = 0f, Intonation = 1f },
                SpeechVoiceMelody.Gallop => new Melody { Spread = 0.2f, Swing = 0f, Intonation = 1f },
                SpeechVoiceMelody.Chant => new Melody { Spread = 0f, Swing = 0f, Intonation = 0f },
                _ => new Melody { Spread = 1f, Swing = 1f, Intonation = 1f },
            };
        }
    }

    /// <summary>Reshapes each word's letters for the melody's rhythm and pitch shape.</summary>
    private static void ApplyMelody(List<Blip> blips, SpeechVoiceMelody melody)
    {
        var from = 0;
        var word = 0;
        var letter = 0;
        var flip = false;
        while (from < blips.Count)
        {
            if (blips[from].Gap)
            {
                from++;
                continue;
            }

            var to = from + 1;
            while (to < blips.Count && !blips[to].Gap && blips[to].Word == blips[from].Word)
            {
                to++;
            }

            var hash = WordHash(blips, from, to);
            if (word == 0)
                flip = (hash & 1) == 1;

            var endsSentence = false;
            for (var i = from; i < to; i++)
            {
                var blip = blips[i];
                endsSentence |= blip.Last;
                var place = new Place(i - from, to - from, word, letter++, flip);
                ShapeLetter(ref blip, melody, place, hash);
                blips[i] = blip;
            }

            word = endsSentence ? 0 : word + 1;
            from = to;
        }
    }

    /// <summary>Where a letter sits: in its word, its word in the sentence, and in the whole line.</summary>
    private readonly struct Place
    {
        public readonly int Index;
        public readonly int Count;
        public readonly int Word;
        public readonly int Letter;

        /// <summary>The sentence starts on the high note, for Leaps.</summary>
        public readonly bool Flip;

        public Place(int index, int count, int word, int letter, bool flip)
        {
            Index = index;
            Count = count;
            Word = word;
            Letter = letter;
            Flip = flip;
        }
    }

    /// <summary>One letter's length, loudness, pitch and vowel for its place in the word.</summary>
    private static void ShapeLetter(ref Blip blip, SpeechVoiceMelody melody, in Place place, uint hash)
    {
        var index = place.Index;
        var count = place.Count;
        var vowel = blip.Onset.Kind == SoundKind.None && !blip.ConsonantOnly;
        var x = count > 1 ? index / (float) (count - 1) : 0.5f;

        switch (melody)
        {
            case SpeechVoiceMelody.StressBeat:
                var period = 2 + (int) (hash % 2);
                if (index % period == 0)
                {
                    blip.Length *= 1.5f;
                    blip.Gain *= 1.15f;
                    blip.Lift *= 1.08f;
                }
                else
                {
                    blip.Length *= 0.7f;
                    blip.Gain *= 0.8f;
                    if (!blip.ConsonantOnly)
                    {
                        blip.VowelStart = Vector3.Lerp(blip.VowelStart, Uh, 0.6f);
                        blip.VowelEnd = Vector3.Lerp(blip.VowelEnd, Uh, 0.6f);
                    }
                }

                break;
            case SpeechVoiceMelody.EvenBeat:
                blip.Length *= vowel ? 1.5f : 0.8f;
                if (count > 1)
                {
                    var drop = 1 + (int) (hash % (uint) (count - 1));
                    blip.Lift *= index < drop ? 1.06f : 0.88f;
                }

                break;
            case SpeechVoiceMelody.Tones:
                blip.Length *= 1.2f;
                blip.Lift *= (hash % 4) switch
                {
                    0 => 1.12f,
                    1 => 0.8f + 0.4f * x,
                    2 => 1.2f - 0.4f * x,
                    _ => 1f - 0.25f * MathF.Sin(MathF.PI * x),
                };
                break;
            case SpeechVoiceMelody.Sung:
                if (vowel)
                    blip.Length *= 1.3f;
                if (blip.Last)
                    blip.Length *= 2.5f;
                break;
            case SpeechVoiceMelody.Cascade:
                blip.Lift *= 1.2f * MathF.Max(0.6f, MathF.Pow(0.88f, place.Word));
                break;
            case SpeechVoiceMelody.Leaps:
                blip.Lift *= (place.Word % 2 == 0) == place.Flip ? 1.35f : 0.85f;
                break;
            case SpeechVoiceMelody.Gallop:
                if (place.Letter % 3 == 0)
                {
                    blip.Length *= 2f;
                    blip.Gain *= 1.4f;
                    blip.Lift *= 1.15f;
                }
                else
                {
                    blip.Length *= 0.55f;
                    blip.Gain *= 0.65f;
                    blip.Lift *= 0.95f;
                }

                break;
            case SpeechVoiceMelody.Chant:
                if (blip.Last)
                {
                    blip.Lift *= 0.75f;
                    blip.Length *= 2f;
                }

                break;
        }
    }

    /// <summary>A hash of a word's sounds, so the same word gets the same shape in every line.</summary>
    private static uint WordHash(List<Blip> blips, int from, int to)
    {
        var hash = 2166136261u;
        for (var i = from; i < to; i++)
        {
            var blip = blips[i];
            hash = (hash ^ (uint) blip.Onset.Kind) * 16777619u;
            hash = (hash ^ (uint) blip.Onset.F2) * 16777619u;
            hash = (hash ^ (uint) blip.VowelStart.Y) * 16777619u;
            hash = (hash ^ (uint) blip.Coda.F2) * 16777619u;
        }

        return hash ^ (hash >> 16);
    }

    /// <summary>A pitch ratio moved to the nearest note of the five-note scale.</summary>
    private static float SnapToScale(float ratio)
    {
        var semitones = 12f * MathF.Log2(MathF.Max(ratio, 0.01f));
        var octave = MathF.Floor(semitones / 12f);
        var within = semitones - 12f * octave;
        var best = 0;
        foreach (var step in SungSteps)
        {
            if (MathF.Abs(within - step) < MathF.Abs(within - best))
                best = step;
        }

        return MathF.Pow(2f, (12f * octave + best) / 12f);
    }
}
