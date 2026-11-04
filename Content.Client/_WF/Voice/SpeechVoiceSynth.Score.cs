using System.Numerics;
using Content.Shared._WF.Voice;

namespace Content.Client._WF.Voice;

public sealed partial class SpeechVoiceSynth
{
    /// <summary>Samples per control frame. Targets are set once a millisecond and glided between.</summary>
    private const int FrameSamples = SampleRate / 1000;

    /// <summary>The control tracks a line is rendered from, one value per frame each.</summary>
    private enum Track : byte
    {
        F1,
        F2,
        F3,
        F4,
        B1,
        B2,
        B3,
        B4,

        /// <summary>Voice loudness, 0 to 1.</summary>
        Voice,

        /// <summary>Breath through the mouth's shape, against the vowel.</summary>
        Breath,

        /// <summary>Hiss or burst through the consonant's own resonances, against the vowel.</summary>
        Hiss,
        NoiseF1,
        NoiseF2,
        NoiseF3,
        NoiseB1,
        NoiseB2,
        NoiseB3,
        NoiseS1,
        NoiseS2,
        NoiseS3,
        NoiseFlat,

        /// <summary>How closed the mouth is for a nasal, 0 to 1.</summary>
        Nasal,
        NasalZero,
        F0,

        /// <summary>The letter's loudness from the melody and the text.</summary>
        Gain,
        Rd,

        /// <summary>1 while a glide is sounding, for a trilling voice.</summary>
        Trill,

        /// <summary>1 in the vowel part of a letter, where bubbles ride.</summary>
        Vowel,
        Count,
    }

    /// <summary>What one blip settled on before rendering: its note, loudness and small random colour.</summary>
    private struct Plan
    {
        public float F0;
        public float Gain;
        public float Formants;
        public float WobblePhase;
        public float Bump;
        public float OnsetEnd;
        public float CodaStart;
        public float Rd;
        public bool NewWord;

        /// <summary>A question's rise, reached by the end of its last letter rather than stepped to at its start.</summary>
        public float Rise;
    }

    // Time constants of the glides between frames, in seconds. The mouth moves over about 12 ms, which carries b, d
    // and g (Delattre 1955) and opens a glide in time. Pitch moves over 20 ms, near the fastest a person can change
    // note (Xu and Sun 2002). Noise loudness moves fastest so a burst keeps its shape.
    private const float MouthGlide = 0.012f;
    private const float VoiceGlide = 0.005f;

    /// <summary>The voice falls faster than it rises, so a stop's closure is quiet before its burst.</summary>
    private const float VoiceFall = 0.0025f;
    private const float BreathGlide = 0.003f;
    private const float HissGlide = 0.002f;
    private const float NoiseTuneGlide = 0.004f;
    private const float NasalGlide = 0.005f;
    private const float PitchGlide = 0.02f;
    private const float GainGlide = 0.008f;

    /// <summary>A word fades in and out over these, on a smooth curve, so silence never meets sound at a step.</summary>
    private const float WordAttack = 0.008f;
    private const float WordRelease = 0.012f;

    private const float F4Bandwidth = 250f;

    /// <summary>Builds the control tracks for a line, one frame a millisecond, each already glided.</summary>
    private float[][] BuildTracks(List<Blip> blips,
        int[] starts,
        int length,
        SpeechVoice voice,
        in Style style,
        in Melody melody,
        float pitch,
        float formantScale)
    {
        var type = voice.Type;
        var intonation = style.Intonation * voice.Intonation * melody.Intonation;
        var frameCount = length / FrameSamples + 2;
        var tracks = new float[(int) Track.Count][];
        for (var k = 0; k < tracks.Length; k++)
        {
            tracks[k] = new float[frameCount];
        }

        var plans = PlanBlips(blips, starts, voice, style, melody, intonation, pitch);

        var words = 0;
        foreach (var blip in blips)
        {
            words = Math.Max(words, blip.Word + 1);
        }

        var wordFrom = new int[words];
        var wordTo = new int[words];
        for (var k = 0; k < blips.Count; k++)
        {
            if (blips[k].Gap)
                continue;

            var w = blips[k].Word;
            if (wordTo[w] == 0)
                wordFrom[w] = starts[k];
            wordTo[w] = starts[k + 1];
        }

        var baseF0 = BaseF0 * pitch;
        var noiseScale = MathF.Sqrt(formantScale / BaseFormantScale);
        var attack = WordAttack * SampleRate;
        var release = WordRelease * SampleRate;
        var aspirationLevel = DbToGain(-12f);

        var b = 0;
        var frame = new float[(int) Track.Count];
        frame[(int) Track.F0] = baseF0;
        frame[(int) Track.Rd] = style.Rd;
        frame[(int) Track.Gain] = 1f;
        SetMouth(frame, Eh * formantScale, BandwidthOf(Eh), formantScale, style.Bandwidth, baseF0, 0f);
        SetNoise(frame, S, noiseScale, style.HissWidth);

        for (var g = 0; g < frameCount; g++)
        {
            var i = g * FrameSamples;
            while (b < blips.Count && i >= starts[b + 1])
            {
                b++;
            }

            // A gap, or the tail after the last letter: silent, with the mouth and pitch already moving to the next word.
            var silent = b >= blips.Count || blips[b].Gap;
            var target = b;
            if (silent)
            {
                target = b + 1;
                while (target < blips.Count && blips[target].Gap)
                {
                    target++;
                }
            }

            if (target < blips.Count)
            {
                var blip = blips[target];
                var plan = plans[target];
                var slotLength = starts[target + 1] - starts[target];
                var at = silent ? 0 : i - starts[target];
                var t = at / (float) slotLength;
                var seconds = at / (float) SampleRate;
                var slotSeconds = slotLength / (float) SampleRate;
                var wordT = Math.Clamp((i - wordFrom[blip.Word]) / MathF.Max(1f, wordTo[blip.Word] - wordFrom[blip.Word]),
                    0f, 1f);

                Moment moment;
                Consonant sound;
                var inCoda = t >= plan.CodaStart;
                if (inCoda)
                {
                    sound = blip.Coda;
                    var codaSeconds = (1f - plan.CodaStart) * slotSeconds;
                    moment = CodaAt(sound, seconds - plan.CodaStart * slotSeconds, codaSeconds);
                }
                else
                {
                    sound = blip.Onset;
                    moment = blip.ConsonantOnly ? default : OnsetAt(sound, seconds, slotSeconds, style.FricativeStretch);
                }

                Vector3 mouthTarget;
                var bandwidth = blip.VowelBandwidth;
                if (moment.AtLocus)
                {
                    mouthTarget = new Vector3(sound.F1, sound.F2, sound.F3);
                    if (sound.Bandwidth != Vector3.Zero)
                        bandwidth = sound.Bandwidth;
                }
                else
                {
                    var vowelT = Math.Clamp((t - plan.OnsetEnd) / MathF.Max(1e-3f, plan.CodaStart - plan.OnsetEnd), 0f, 1f);
                    mouthTarget = Vector3.Lerp(Vector3.Lerp(blip.VowelStart, blip.VowelEnd, vowelT), style.VowelTarget,
                        style.VowelPull);
                }

                // An open glottis widens F1: about 300 Hz through an h (Klatt 1980).
                var openB1 = sound.Kind == SoundKind.Aspirate ? 300f * moment.Breath : 0f;

                var shape = ShapeFor(type, t, wordT);
                var mouth = Vector3.Lerp(mouthTarget, shape.Formants, shape.Pull) * (formantScale * plan.Formants);
                if (style.Wobble > 0f)
                {
                    var ripple = 1f + 0.12f * style.Wobble * MathF.Sin(MathF.Tau * 9f * i / SampleRate + plan.WobblePhase);
                    mouth.X *= ripple;
                    mouth.Y *= 2f - ripple;
                }

                mouth.X *= shape.F1;
                mouth.Y *= shape.F2;

                var sinceVowel = MathF.Max(0f, t - plan.OnsetEnd) * slotSeconds;
                var f0 = plan.F0 * shape.F0 * (1f + plan.Bump * MathF.Exp(-sinceVowel / 0.03f))
                         * (1f + (plan.Rise - 1f) * t);
                SetMouth(frame, mouth, bandwidth, formantScale, style.Bandwidth, f0, openB1);
                frame[(int) Track.F0] = f0;
                frame[(int) Track.Gain] = plan.Gain;

                // The folds tense as the pitch rises, so a lifted note keeps its upper harmonics.
                frame[(int) Track.Rd] = plan.Rd * MathF.Sqrt(baseF0 / MathF.Max(f0, 1f));

                if (sound.NoiseDb != 0f && sound.Kind != SoundKind.Aspirate)
                    SetNoise(frame, sound, noiseScale, style.HissWidth);

                if (silent)
                {
                    frame[(int) Track.Voice] = 0f;
                    frame[(int) Track.Breath] = 0f;
                    frame[(int) Track.Hiss] = 0f;
                    frame[(int) Track.Nasal] = 0f;
                    frame[(int) Track.Trill] = 0f;
                    frame[(int) Track.Vowel] = 0f;
                }
                else
                {
                    // Letters inside a word keep the voice up as one stream, except where a stop has to go quiet.
                    var streamed = (!plan.NewWord || inCoda) && !moment.Stopped;
                    var voicing = streamed ? MathF.Max(moment.Voice, StreamFloor) : moment.Voice;
                    var rise = Ramp(i - wordFrom[blip.Word], attack);
                    var ease = Ramp(wordTo[blip.Word] - i, release);
                    frame[(int) Track.Voice] = blip.ConsonantOnly ? 0f : voicing * rise * ease;

                    var breathDb = sound.Kind == SoundKind.Aspirate ? DbToGain(sound.NoiseDb) : aspirationLevel;
                    frame[(int) Track.Breath] = moment.Breath * breathDb;

                    if (blip.ConsonantOnly)
                    {
                        // A hissing voice's sentence-end hiss, faded in and out over a third each way.
                        frame[(int) Track.Hiss] = TailHissLevel * plan.Gain * Ramp(t, 0.33f) * Ramp(1f - t, 0.33f);
                    }
                    else
                    {
                        frame[(int) Track.Hiss] = moment.Noise * NoiseLevel(sound);
                    }

                    frame[(int) Track.Nasal] = moment.Nasal ? 1f : 0f;
                    if (moment.Nasal)
                        frame[(int) Track.NasalZero] = sound.NasalZero * formantScale;
                    frame[(int) Track.Trill] = blip.Onset.Kind == SoundKind.Liquid || blip.Coda.Kind == SoundKind.Liquid
                        ? 1f
                        : 0f;
                    frame[(int) Track.Vowel] = !blip.ConsonantOnly && t >= plan.OnsetEnd && !inCoda ? 1f : 0f;
                }
            }
            else
            {
                frame[(int) Track.Voice] = 0f;
                frame[(int) Track.Breath] = 0f;
                frame[(int) Track.Hiss] = 0f;
                frame[(int) Track.Nasal] = 0f;
                frame[(int) Track.Trill] = 0f;
                frame[(int) Track.Vowel] = 0f;
            }

            for (var k = 0; k < frame.Length; k++)
            {
                tracks[k][g] = frame[k];
            }
        }

        for (var k = (int) Track.F1; k <= (int) Track.B4; k++)
        {
            Glide(tracks[k], MouthGlide);
        }

        Glide(tracks[(int) Track.Voice], VoiceGlide, VoiceFall);
        Glide(tracks[(int) Track.Breath], BreathGlide);
        Glide(tracks[(int) Track.Hiss], HissGlide);
        for (var k = (int) Track.NoiseF1; k <= (int) Track.NoiseFlat; k++)
        {
            Glide(tracks[k], NoiseTuneGlide);
        }

        Glide(tracks[(int) Track.Nasal], NasalGlide);
        Glide(tracks[(int) Track.NasalZero], NasalGlide);
        Glide(tracks[(int) Track.F0], PitchGlide);
        Glide(tracks[(int) Track.Gain], GainGlide);
        Glide(tracks[(int) Track.Rd], PitchGlide);
        Glide(tracks[(int) Track.Trill], NasalGlide);
        return tracks;
    }

    /// <summary>Each blip's note, loudness and timing, settled once so every frame of it agrees.</summary>
    private Plan[] PlanBlips(List<Blip> blips,
        int[] starts,
        SpeechVoice voice,
        in Style style,
        in Melody melody,
        float intonation,
        float pitch)
    {
        var plans = new Plan[blips.Count];
        var letterPitch = 1f;
        var spread = 0.7f * style.PitchVariance * voice.PitchVariance * style.PitchSpread * melody.Spread;
        var step = spread > 0f ? 0.02f * style.PitchSpread * MathF.Min(1f, voice.PitchVariance) : 0f;

        for (var b = 0; b < blips.Count; b++)
        {
            var blip = blips[b];
            if (blip.Gap)
                continue;

            // Each letter gets its own note, kept a step away from the last one.
            var note = 1f + spread * _noise.Next();
            if (MathF.Abs(note - letterPitch) < step)
                note = letterPitch + (note >= letterPitch ? step : -step);
            letterPitch = note;

            var gain = blip.Gain;
            var lastLift = 1f;
            if (blip.Last && blip.Ending == '?')
                lastLift = 1.13f + 0.17f * MathF.Min(1f, intonation / FullIntonation);
            if (blip.Last && blip.Ending == '!')
                gain *= 1.25f;

            // A sung note snaps to the scale with its rise included. Spoken, the rise glides up through the letter.
            var tune = letterPitch * Contour(blip, intonation) * blip.Lift * (melody.Snap ? lastLift : 1f);
            if (melody.Snap)
                tune = SnapToScale(tune);

            var slotSeconds = (starts[b + 1] - starts[b]) / (float) SampleRate;
            var consonantEnd = blip.ConsonantOnly
                ? 0.8f * slotSeconds
                : ConsonantSeconds(blip.Onset, slotSeconds, style.FricativeStretch);
            var hasCoda = blip.Coda.Kind != SoundKind.None;

            plans[b] = new Plan
            {
                F0 = BaseF0 * pitch * tune,
                Rise = melody.Snap ? 1f : lastLift,
                Gain = gain,
                Formants = 1f + 0.03f * _noise.Next(),
                WobblePhase = MathF.Tau * (0.5f + 0.5f * _noise.Next()),
                // Micro-prosody: pitch starts a little high after a voiceless consonant, a little low after a voiced one.
                Bump = blip.Onset.Kind == SoundKind.None || melody.Snap ? 0f : blip.Onset.Voiced ? -0.03f : 0.05f,
                OnsetEnd = consonantEnd / slotSeconds,
                CodaStart = hasCoda ? 1f - MathF.Min(0.4f, 0.02f / slotSeconds) : 1f,
                Rd = style.Rd * (blip.Shout ? 0.7f : 1f),
                NewWord = b == 0 || blips[b - 1].Gap,
            };
        }

        return plans;
    }

    /// <summary>Writes the four formants and their bandwidths for this mouth shape at this pitch.</summary>
    // F1 never sits under the pitch, which is what singers do as the voice rises (Joliveau, Smith and Wolfe 2004),
    // and bandwidths widen with the pitch of the moment (Hawks and Miller 1995).
    private static void SetMouth(float[] frame,
        Vector3 mouth,
        Vector3 bandwidth,
        float formantScale,
        float bandwidthScale,
        float f0,
        float openB1)
    {
        var widen = Math.Clamp(1f + 0.25f * (f0 - 132f) / 88f, 0.9f, 2.2f);
        var f1 = MathF.Max(mouth.X, 1.05f * f0);
        var scale = formantScale * widen * bandwidthScale;
        frame[(int) Track.F1] = f1;
        frame[(int) Track.F2] = mouth.Y;
        frame[(int) Track.F3] = mouth.Z;
        frame[(int) Track.F4] = mouth.Z + 1000f * formantScale;
        frame[(int) Track.B1] = (bandwidth.X + openB1) * scale;
        frame[(int) Track.B2] = bandwidth.Y * scale;
        frame[(int) Track.B3] = bandwidth.Z * scale;
        frame[(int) Track.B4] = F4Bandwidth * scale;
    }

    /// <summary>Writes a consonant's noise colour: three resonances and a flat share.</summary>
    private static void SetNoise(float[] frame, in Consonant sound, float noiseScale, float hissWidth)
    {
        frame[(int) Track.NoiseF1] = sound.NoiseFrequency.X * noiseScale;
        frame[(int) Track.NoiseF2] = sound.NoiseFrequency.Y * noiseScale;
        frame[(int) Track.NoiseF3] = sound.NoiseFrequency.Z * noiseScale;
        frame[(int) Track.NoiseB1] = sound.NoiseBandwidth.X * hissWidth;
        frame[(int) Track.NoiseB2] = sound.NoiseBandwidth.Y * hissWidth;
        frame[(int) Track.NoiseB3] = sound.NoiseBandwidth.Z * hissWidth;
        frame[(int) Track.NoiseS1] = sound.NoiseShape.X;
        frame[(int) Track.NoiseS2] = sound.NoiseShape.Y;
        frame[(int) Track.NoiseS3] = sound.NoiseShape.Z;
        frame[(int) Track.NoiseFlat] = sound.NoiseFlat;
    }

    /// <summary>Runs a one-pole glide along a track, so each frame eases toward its target instead of stepping.</summary>
    private static void Glide(float[] track, float seconds)
    {
        Glide(track, seconds, seconds);
    }

    /// <summary>A glide with its own time constant for falling.</summary>
    private static void Glide(float[] track, float rise, float fall)
    {
        var up = 1f - MathF.Exp(-0.001f / rise);
        var down = 1f - MathF.Exp(-0.001f / fall);
        var value = track[0];
        for (var g = 1; g < track.Length; g++)
        {
            value += (track[g] - value) * (track[g] > value ? up : down);
            track[g] = value;
        }
    }

    /// <summary>A consonant's hiss or burst loudness, 0 for one without noise.</summary>
    private static float NoiseLevel(in Consonant sound)
    {
        if (sound.NoiseDb == 0f || sound.Kind == SoundKind.Aspirate)
            return 0f;

        return DbToGain(sound.NoiseDb);
    }

    /// <summary>Pitch through a phrase: a gentle fall, then a tune from the punctuation that ends it.</summary>
    private static float Contour(in Blip blip, float intonation)
    {
        var t = blip.PhrasePosition;
        var ending = Math.Max(0f, (t - 0.6f) / 0.4f);
        var tune = blip.Ending switch
        {
            '?' => 1f + intonation * 0.5f * ending,
            '!' => 1f + intonation * 0.6f * (1f - t),
            ',' => 1f + intonation * 0.2f * ending,
            _ => 1f - intonation * 0.5f * ending,
        };

        return (1f - PhraseDeclination * t) * tune;
    }

    private static float DbToGain(float db)
    {
        return MathF.Pow(10f, db / 20f);
    }
}
