using Content.Shared._WF.Voice;

namespace Content.Client._WF.Voice;

/// <summary>Builds a line of babble from code alone, one short blip per letter.</summary>
// One voice runs through the whole line: an LF glottal pulse and breath noise through a cascade of formant
// resonators (Klatt 1980), with hisses and bursts as noise through their own resonances beside it. Letters only move
// the targets the filters glide to, so nothing restarts between them.
public sealed partial class SpeechVoiceSynth
{
    /// <summary>Output samples per second.</summary>
    public const int SampleRate = 48000;

    /// <summary>The most samples a line can take, eight seconds.</summary>
    public const int MaxSamples = (int) (MaxSeconds * SampleRate);

    private const int MaxBlips = 128;
    private const float MaxSeconds = 8f;
    private const float BaseF0 = 220f;
    private const float BaseFormantScale = 1.35f;

    /// <summary>Samples between retunes of the gliding filters.</summary>
    private const int Retune = 4;
    private const float MaxFrequency = 0.45f * SampleRate;
    private const float TargetPeak = 0.6f;
    private const float RadioDrive = 1.5f;
    private const float PhraseDeclination = 0.08f;

    /// <summary>Scales every voice's letter length, so all of them speed up or slow down together.</summary>
    private const float LetterTime = 0.93f;

    /// <summary>Intonation at which a question's last letter gets its full lift.</summary>
    private const float FullIntonation = 0.5f;

    /// <summary>Loudness every line is levelled to, as an RMS before the peak limit.</summary>
    private const float TargetRms = 0.16f;

    private const float BuzzRate = 125f;

    /// <summary>Loudness of a pop against the voiced part.</summary>
    private const float PopLevel = 0.25f;

    private const int MaxPops = 4;

    // Measured on the Human "ah" through the cascade: the pulse comes out at 1.65 times its own RMS, white noise at
    // 1.48. VoiceLevel puts a full vowel at VowelRms, under the limiter's knee, and the hisses are set against that.
    private const float VoiceLevel = 0.12f;
    private const float VowelRms = 0.2f;

    /// <summary>Scales breath noise into the mouth so, at the same loudness as the voice, it comes out as loud.</summary>
    private const float AspirationUnit = 1.11f;

    /// <summary>Loudness of a whispered voice's noise against the voice it replaces.</summary>
    private const float WhisperLevel = 1f;

    /// <summary>Loudness of the breath on a breathy voice, against the voice.</summary>
    private const float BreathLevel = 0.6f;

    /// <summary>Loudness of a hissing voice's sentence-end hiss, against the vowel.</summary>
    private const float TailHissLevel = 0.7f;

    /// <summary>Silence added after a line for the room echo to ring out.</summary>
    private const float RoomTail = 0.12f;

    /// <summary>Extra weight above 1.5 kHz when levelling, about 4 dB.</summary>
    private const float ShelfBoost = 0.58f;

    /// <summary>Corner of the one-pole that tilts the glottal pulse darker or brighter, in Hz.</summary>
    private const float TiltCorner = 800f;

    /// <summary>Level above which the soft limiter rounds peaks off.</summary>
    private const float LimitKnee = 0.7f;

    /// <summary>How much wider F1 is at the peak of the open phase, in Hz before the mouth scale.</summary>
    private const float OpenWiden = 150f;

    /// <summary>Which parts of the sound to render, for listening to one on its own.</summary>
    [Flags]
    internal enum Parts : byte
    {
        Voice = 1,
        Noise = 2,
        Effects = 4,
        All = Voice | Noise | Effects,
    }

    private NoiseSource _noise;
    private int _seed;

    /// <summary>
    /// Renders a message into <see cref="SpeechVoiceBuffers.Pcm"/>, capped at eight seconds, and returns how many
    /// samples it wrote. Zero when it has no letters or digits.
    /// </summary>
    /// <param name="radio">Plays it as heard through a radio, on top of the voice.</param>
    public int Render(string text, SpeechVoice voice, bool radio, SpeechVoiceBuffers buffers)
    {
        return Render(text, voice, radio, buffers, Parts.All);
    }

    /// <summary>Renders only some parts of the sound, so one can be heard on its own.</summary>
    internal int Render(string text, SpeechVoice voice, bool radio, SpeechVoiceBuffers buffers, Parts parts)
    {
        var blips = Blips(text);
        if (blips.Count == 0)
            return 0;

        // Seeded from the text, so every listener renders the same line the same way.
        _seed = (int) SpeechVoice.Hash(text);
        _noise = new NoiseSource(_seed);
        var style = Style.For(voice.Type);
        var melody = Melody.For(voice.Melody);

        var wordOnset = WordOnsetFor(voice.Type);
        if (wordOnset.Kind != SoundKind.None)
        {
            var previousWord = -1;
            for (var i = 0; i < blips.Count; i++)
            {
                var blip = blips[i];
                if (blip.Gap || blip.Word == previousWord)
                    continue;

                previousWord = blip.Word;
                if (blip.Onset.Kind != SoundKind.None || blip.ConsonantOnly)
                    continue;

                blip.Onset = wordOnset;
                blips[i] = blip;
            }
        }

        for (var i = blips.Count - 1; i >= 0; i--)
        {
            var blip = blips[i];
            if (style.HissTail >= 0.5f && blip.Last && blip.Ending != ',' && !blip.Gap && blips.Count < MaxBlips)
            {
                var hiss = Syllable(S, Uh);
                hiss.ConsonantOnly = true;
                hiss.Length = 0.8f * style.FricativeStretch;
                hiss.Word = blip.Word;
                hiss.Gain = 0.7f * blip.Gain;
                blips.Insert(i + 1, hiss);
            }
        }

        ApplyMelody(blips, voice.Melody);
        StretchHisses(blips);

        var slot = style.LetterSeconds * LetterTime / voice.Pace * SampleRate;
        var starts = new int[blips.Count + 1];
        for (var i = 0; i < blips.Count; i++)
        {
            var slots = blips[i].Gap ? blips[i].Length * style.GapScale : blips[i].Length;
            starts[i + 1] = starts[i] + (int) (slot * slots);
        }

        var tail = radio ? 0 : (int) (RoomTail * SampleRate);
        var length = Math.Min(starts[^1] + tail, MaxSamples);
        var buffer = buffers.Samples.AsSpan(0, length);
        buffer.Clear();

        // The mouth grows with the pitch about as a child's does against a man's (Peterson and Barney 1952), not
        // one to one, and the Size slider scales it on its own.
        var pitch = voice.Pitch * style.F0Scale;
        var formantScale = BaseFormantScale * MathF.Min(1.5f, MathF.Pow(voice.Pitch, 0.4f)) * style.FormantScale
                           / voice.Size;
        var tracks = BuildTracks(blips, starts, length, voice, style, melody, pitch, formantScale);
        Speak(buffer, blips, starts, tracks, voice, style, melody, formantScale, parts);

        if (style.Hollow > 0f)
        {
            var delay = (int) (style.HollowDelay * SampleRate);
            var feedback = 0.3f * style.Hollow;
            var wet = 0.5f * style.Hollow;
            var ring = new float[delay];
            for (var i = 0; i < length; i++)
            {
                var combed = buffer[i] + (i >= delay ? feedback * ring[i % delay] : 0f);
                ring[i % delay] = combed;
                buffer[i] += (combed - buffer[i]) * wet;
            }
        }

        // A line cut at the cap fades out over 20 ms rather than stopping with a click.
        if (starts[^1] > length)
        {
            var fade = Math.Min(length, SampleRate / 50);
            for (var i = 0; i < fade; i++)
            {
                buffer[length - 1 - i] *= i / (float) fade;
            }
        }

        Settle(buffer);

        if ((parts & Parts.Effects) != 0)
        {
            ApplyEffect(buffer, style.Effect);
            LowPass(buffer, style.LowPass);
            HighPass(buffer, 80f);

            if (radio)
                Radio(buffer);
            else
                Room(buffer);
        }

        ToPcm(buffer, buffers.Pcm);
        return buffer.Length;
    }

    /// <summary>Renders the line from its control tracks, one continuous voice from the first sample to the last.</summary>
    private void Speak(Span<float> buffer,
        List<Blip> blips,
        int[] starts,
        float[][] tracks,
        SpeechVoice voice,
        in Style style,
        in Melody melody,
        float formantScale,
        Parts parts)
    {
        var length = buffer.Length;
        var voiceOn = (parts & Parts.Voice) != 0;
        var noiseOn = (parts & Parts.Noise) != 0;
        var intonation = style.Intonation * voice.Intonation * melody.Intonation;

        // Separate noise for breath and hiss, so the two never cancel or add as one (d'Heureuse, KlattSyn).
        var breathNoise = new NoiseSource(_seed ^ 0x3C6EF372);
        var hissNoise = new NoiseSource(_seed ^ 0x1B873593);

        var cascade = new Svf[4];
        var hiss = new Svf[3];
        var nasalPole = new Svf();
        var styleNotch = new Svf();
        var letterNotch = new Svf();
        var whistle = new Svf();
        nasalPole.Tune(250f * formantScale, 100f * formantScale, SampleRate);
        styleNotch.Tune(250f * formantScale * (1f + style.Nasal), 100f * formantScale, SampleRate);
        whistle.Tune(1800f * formantScale, 60f * formantScale, SampleRate);
        var styleNasal = MathF.Min(1f, style.Nasal);
        var darkK = OnePole(900f * formantScale);
        var dark = 0f;
        var tiltK = OnePole(TiltCorner);
        var tiltMix = Math.Clamp(style.Tilt - 1f, -0.5f, 1f);
        var tilted = 0f;
        var driveNorm = style.Drive > 1f ? 1f / MathF.Tanh(style.Drive) : 1f;
        var hissUnit = new float[3];
        var hissNorm = 0f;

        var glottis = new Glottis();
        var rdSet = tracks[(int) Track.Rd][0];
        glottis.Setup(rdSet);
        var phase = 0f;
        var periodRate = 1f;
        var periodAmp = 1f;
        var alternate = false;
        var jitter = 0f;
        var shake = 0f;
        var shakeStep = MathF.Tau * style.VibratoRate / SampleRate;

        // A slow pitch and loudness swing, each cycle 0.33 to 0.47 s long.
        var swingPhase = 0f;
        var swingStep = MathF.Tau / (0.4f * SampleRate);
        var swingDepth = (0.03f + 0.08f * intonation) * melody.Swing;

        var pops = PlanPops(blips, starts, style);
        var popAmp = new float[pops.Length];
        var nextPop = 0;

        var frame = new float[(int) Track.Count];
        var baseF0 = BaseF0 * voice.Pitch * style.F0Scale;
        var open = 0f;

        for (var i = 0; i < length; i++)
        {
            var g = i / FrameSamples;
            var mix = (i - g * FrameSamples) / (float) FrameSamples;
            for (var k = 0; k < frame.Length; k++)
            {
                var track = tracks[k];
                frame[k] = track[g] + (track[g + 1] - track[g]) * mix;
            }

            if (i % Retune == 0)
            {
                // F1 widens while the folds are open in each cycle (Klatt and Klatt 1990), which softens its ring.
                cascade[0].Tune(frame[(int) Track.F1], frame[(int) Track.B1] + OpenWiden * formantScale * open,
                    SampleRate);
                cascade[1].Tune(frame[(int) Track.F2], frame[(int) Track.B2], SampleRate);
                cascade[2].Tune(frame[(int) Track.F3], frame[(int) Track.B3], SampleRate);
                cascade[3].Tune(frame[(int) Track.F4], frame[(int) Track.B4], SampleRate);
                letterNotch.Tune(MathF.Max(100f, frame[(int) Track.NasalZero]), 150f * formantScale, SampleRate);

                var power = frame[(int) Track.NoiseFlat] * frame[(int) Track.NoiseFlat];
                for (var k = 0; k < 3; k++)
                {
                    var width = frame[(int) Track.NoiseB1 + k];
                    var shapeK = frame[(int) Track.NoiseS1 + k];
                    hiss[k].Tune(MathF.Min(frame[(int) Track.NoiseF1 + k], MaxFrequency), width, SampleRate);
                    hissUnit[k] = BandUnit(width);
                    power += shapeK * shapeK;
                }

                hissNorm = power > 0f ? 1f / MathF.Sqrt(power) : 0f;
            }

            // Pitch: the track, a slow swing, vibrato, a growl's wobble and a trill on glides.
            shake += shakeStep;
            swingPhase += swingStep;
            if (swingPhase >= MathF.Tau)
            {
                swingPhase -= MathF.Tau;
                swingStep = MathF.Tau / ((0.4f + 0.07f * _noise.Next()) * SampleRate);
            }

            var swing = swingDepth * MathF.Sin(swingPhase);
            var f0 = frame[(int) Track.F0] * (1f + swing);
            if (style.VibratoDepth > 0f)
                f0 *= 1f + style.VibratoDepth * MathF.Sin(shake);
            var growl = style.Growl > 0f ? MathF.Sin(MathF.Tau * 40f * i / SampleRate) : 0f;
            if (style.Growl > 0f)
                f0 *= 1f + 0.04f * style.Growl * growl;
            if (style.Trill > 0f)
                f0 *= 1f + 0.08f * style.Trill * frame[(int) Track.Trill] * MathF.Sin(MathF.Tau * 25f * i / SampleRate);

            phase += f0 * periodRate / SampleRate;
            if (phase >= 1f)
            {
                phase -= 1f;
                jitter = 0.7f * jitter + 0.3f * _noise.Next();
                periodRate = 1f + style.Jitter * 2f * jitter;
                periodAmp = 1f + style.Shimmer * _noise.Next();
                alternate = !alternate;
                if (alternate)
                    periodAmp *= MathF.Max(0f, 1f - style.Subharmonic);

                var rd = frame[(int) Track.Rd];
                if (MathF.Abs(rd - rdSet) > 0.02f)
                {
                    rdSet = rd;
                    glottis.Setup(rd);
                }
            }

            var av = frame[(int) Track.Voice];
            var pulse = glottis.Sample(phase) * periodAmp;
            tilted += (pulse - tilted) * tiltK;
            pulse += tiltMix * (tilted - pulse);

            // Breath rides on the open phase of each glottal cycle (Klatt and Klatt 1990), and every noise dips in the
            // second half of the period while the voice sounds (Klatt 1980), so it blends in rather than sitting on top.
            open = phase < glottis.Closing ? MathF.Sin(MathF.PI * phase / glottis.Closing) : 0f;
            var noiseMod = 1f - 0.5f * MathF.Min(1f, 2f * av) * (1f - MathF.Max(0f, MathF.Sin(MathF.Tau * phase)));

            // More breath as the pitch rises, which keeps a high note from turning into a bare tone (Klatt 1980).
            var breathAmount = style.Breath * MathF.Min(3f, MathF.Pow(f0 / baseF0, 1.5f));
            if (style.BreathGust > 0f)
            {
                var gust = 0.5f + 0.5f * MathF.Sin(MathF.Tau * 2.5f * i / SampleRate);
                breathAmount += 0.35f * style.BreathGust * gust;
            }

            var breath = breathNoise.Gaussian() * noiseMod * AspirationUnit;
            var source = pulse * av * (1f - breathAmount) * (1f - style.Whisper)
                         + breath * (open * av * breathAmount * BreathLevel + av * style.Whisper * WhisperLevel);
            if (!voiceOn)
                source = 0f;
            if (noiseOn)
                source += breath * frame[(int) Track.Breath];
            source *= VoiceLevel;

            var x = source;
            for (var k = 0; k < cascade.Length; k++)
            {
                cascade[k].Process(x);
                x = cascade[k].Low;
            }

            // A nasal resonance, and the anti-resonance that tells m, n and ng apart. A closed mouth also hums dark.
            var nasal = frame[(int) Track.Nasal];
            var nasalMix = MathF.Max(nasal, styleNasal);
            if (nasalMix > 0.001f)
            {
                nasalPole.Process(x);
                x += (nasalPole.Low - x) * nasalMix;
                if (styleNasal > 0f)
                {
                    styleNotch.Process(x);
                    x += (styleNotch.Low + styleNotch.High - x) * styleNasal;
                }

                if (nasal > 0.001f)
                {
                    letterNotch.Process(x);
                    var notched = letterNotch.Low + letterNotch.High;
                    dark += (notched - dark) * darkK;
                    x += (dark - x) * nasal;
                }
            }

            if (style.Whistle > 0f)
            {
                whistle.Process(x);
                x += whistle.Band * 3f * style.Whistle;
            }

            var hissed = 0f;
            var af = frame[(int) Track.Hiss];
            var grain = hissNoise.Gaussian() * noiseMod;
            hiss[0].Process(grain);
            hiss[1].Process(grain);
            hiss[2].Process(grain);
            if (noiseOn && af > 1e-5f)
            {
                hissed = (frame[(int) Track.NoiseFlat] * grain
                          + frame[(int) Track.NoiseS1] * hissUnit[0] * hiss[0].Band
                          - frame[(int) Track.NoiseS2] * hissUnit[1] * hiss[1].Band
                          + frame[(int) Track.NoiseS3] * hissUnit[2] * hiss[2].Band) * hissNorm * af * VowelRms;
            }

            var sample = (x + hissed) * frame[(int) Track.Gain] * (1f + 0.5f * swing);

            if (style.Bubble > 0f)
            {
                var blub = 0.5f + 0.5f * MathF.Sin(MathF.Tau * 15f * i / SampleRate);
                sample *= 1f - 0.4f * style.Bubble * blub * frame[(int) Track.Vowel];
            }

            if (style.Growl > 0f)
                sample *= 1f - 0.35f * style.Growl * (0.5f + 0.5f * growl);

            // Runs across word gaps, so a short letter still gets whole purr cycles.
            if (style.Purr > 0f)
                sample *= 1f - 0.3f * style.Purr * (0.5f + 0.5f * MathF.Sin(MathF.Tau * 25f * i / SampleRate));

            if (style.Flutter > 0f)
            {
                var beat = 0.5f + 0.5f * MathF.Sin(MathF.Tau * style.FlutterRate * i / SampleRate);
                sample *= 1f - style.Flutter * beat * beat;
            }

            if (style.Buzz > 0f)
                sample *= 1f - style.Buzz * (0.5f + 0.5f * MathF.Sin(MathF.Tau * BuzzRate * i / SampleRate));

            if (style.Drive > 1f)
                sample = MathF.Tanh(style.Drive * sample) * driveNorm;

            while (nextPop < pops.Length && pops[nextPop].End < i)
            {
                nextPop++;
            }

            for (var k = nextPop; k < pops.Length && pops[k].Start <= i; k++)
            {
                var pop = pops[k];
                if (i > pop.End)
                    continue;

                var dt = (i - pop.Start) / (float) SampleRate;
                popAmp[k] = i == pop.Start ? 1f : popAmp[k] * pop.Fade;
                var popPhase = MathF.Tau * pop.Frequency * (dt + 0.05f * dt * dt / pop.Decay);
                sample += PopLevel * style.Pops * pop.Gain * popAmp[k] * MathF.Sin(popPhase);
            }

            buffer[i] = sample;
        }
    }

    /// <summary>A small bubble pop: a damped sine that rises as it fades (van den Doel 2005).</summary>
    private struct Pop
    {
        public int Start;
        public int End;
        public float Frequency;
        public float Decay;
        public float Fade;
        public float Gain;
    }

    /// <summary>Schedules a bubbly voice's pops on each vowel, in order of their start.</summary>
    private Pop[] PlanPops(List<Blip> blips, int[] starts, in Style style)
    {
        if (style.Pops <= 0f)
            return Array.Empty<Pop>();

        var pops = new List<Pop>();
        for (var b = 0; b < blips.Count; b++)
        {
            var blip = blips[b];
            if (blip.Gap || blip.ConsonantOnly)
                continue;

            var slotLength = starts[b + 1] - starts[b];
            var count = Math.Clamp(3 + (int) MathF.Round(_noise.Next()), 2, MaxPops);
            for (var k = 0; k < count; k++)
            {
                var decay = 0.014f + 0.006f * _noise.Next();
                var start = starts[b] + (int) (slotLength * (0.25f + 0.25f * (_noise.Next() + 1f)));
                pops.Add(new Pop
                {
                    Start = start,
                    End = start + (int) (4.6f * decay * SampleRate),
                    Frequency = 1150f + 650f * _noise.Next(),
                    Decay = decay,
                    Fade = MathF.Exp(-1f / (decay * SampleRate)),
                    Gain = blip.Gain,
                });
            }
        }

        pops.Sort((a, b) => a.Start.CompareTo(b.Start));
        return pops.ToArray();
    }

    /// <summary>Takes any offset out of the line and rounds off peaks above the knee, so no edge reaches the output.</summary>
    private static void Settle(Span<float> buffer)
    {
        const float pole = 0.995f;
        var x1 = 0f;
        var y1 = 0f;
        for (var i = 0; i < buffer.Length; i++)
        {
            var x = buffer[i];
            var y = x - x1 + pole * y1;
            x1 = x;
            y1 = y;

            var level = MathF.Abs(y);
            if (level > LimitKnee)
                y = MathF.Sign(y) * (LimitKnee + (1f - LimitKnee) * MathF.Tanh((level - LimitKnee) / (1f - LimitKnee)));
            buffer[i] = y;
        }
    }

    /// <summary>Scales a peak-gain-1 band-pass of this bandwidth so white noise through it comes out at unit RMS.</summary>
    private static float BandUnit(float bandwidth)
    {
        return bandwidth > 0f ? MathF.Sqrt(0.5f * SampleRate / (MathF.PI / 2f * bandwidth)) : 0f;
    }

    /// <summary>Makes letters that open on s, sh, f or th longer, so their hiss lasts long enough to be heard.</summary>
    // s and sh need about 30 ms of noise, f and th about 50 ms (Jongman 1989).
    private static void StretchHisses(List<Blip> blips)
    {
        for (var i = 0; i < blips.Count; i++)
        {
            var blip = blips[i];
            if (blip.Gap || blip.Onset.Kind != SoundKind.Fricative || blip.Onset.Voiced)
                continue;

            blip.Length *= 1.4f;
            blip.Onset.Hold *= 1.6f;
            blips[i] = blip;
        }
    }

    /// <summary>Per-sample step of a one-pole glide with this time constant.</summary>
    private static float Smoothing(float seconds)
    {
        return 1f - MathF.Exp(-1f / (seconds * SampleRate));
    }

    /// <summary>16-bit samples levelled by loudness, so one loud burst doesn't make the whole line quiet.</summary>
    // RMS is taken over the sounding part only, then the gain is capped so the peak stays under TargetPeak.
    private static void ToPcm(ReadOnlySpan<float> buffer, short[] pcm)
    {
        var peak = 0f;
        var power = 0.0;
        var counted = 0;

        // Loudness is weighted toward the treble, as ears hear it, so bright voices aren't levelled louder.
        var shelf = OnePole(1500f);
        var low = 0f;
        foreach (var sample in buffer)
        {
            low += (sample - low) * shelf;
            var level = Math.Abs(sample);
            peak = Math.Max(peak, level);
            if (level <= 1e-4f)
                continue;

            var weighted = sample + ShelfBoost * (sample - low);
            power += weighted * weighted;
            counted++;
        }

        var rms = counted > 0 ? (float) Math.Sqrt(power / counted) : 0f;
        var gain = peak > 0f ? MathF.Min(TargetRms / rms, TargetPeak / peak) : 0f;

        for (var i = 0; i < buffer.Length; i++)
        {
            pcm[i] = (short) Math.Clamp(buffer[i] * gain * short.MaxValue, short.MinValue, short.MaxValue);
        }
    }
}
