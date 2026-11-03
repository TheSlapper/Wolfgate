using System.Numerics;
using Content.Shared._WF.Voice;

namespace Content.Client._WF.Voice;

/// <summary>Builds a line of babble from code alone, one short blip per letter.</summary>
// Voiced sound is a sum of harmonics shaped by formant curves over an LF glottal spectrum. Hiss and bursts are
// filtered noise.
public sealed partial class SpeechVoiceSynth
{
    /// <summary>Output samples per second.</summary>
    public const int SampleRate = 32000;

    /// <summary>The most samples a line can take, four seconds.</summary>
    public const int MaxSamples = (int) (MaxSeconds * SampleRate);

    private const int MaxBlips = 64;
    private const float MaxSeconds = 4f;
    private const float BaseF0 = 220f;
    private const float BaseFormantScale = 1.35f;
    private const int Block = 32;
    private const int MaxHarmonics = 64;
    private const float MaxFrequency = 0.45f * SampleRate;
    private const float TargetPeak = 0.6f;
    private const float RadioDrive = 1.5f;
    private const float WordFadeSeconds = 0.012f;
    private const float PhraseDeclination = 0.08f;
    private const float Coarticulation = 0.4f;

    /// <summary>Intonation at which a question's last letter gets its full lift.</summary>
    private const float FullIntonation = 0.5f;

    /// <summary>Loudness every line is levelled to, as an RMS before the peak limit.</summary>
    private const float TargetRms = 0.16f;

    private const float BuzzRate = 125f;

    /// <summary>Loudness of a pop against the voiced part.</summary>
    private const float PopLevel = 0.25f;

    private const int MaxPops = 4;

    // Levels of the noise parts against the voiced part, which is normalised to an RMS of about 0.7.
    private const float FricationLevel = 1.2f;
    private const float AspirationLevel = 0.5f;
    private const float BreathLevel = 0.6f;
    private const float WhisperLevel = 9f;

    /// <summary>Silence added after a line for the room echo to ring out.</summary>
    private const float RoomTail = 0.12f;

    /// <summary>Extra weight above 1.5 kHz when levelling, about 4 dB.</summary>
    private const float ShelfBoost = 0.58f;

    private NoiseSource _noise;

    /// <summary>
    /// Renders a message into <see cref="SpeechVoiceBuffers.Pcm"/>, capped at four seconds, and returns how many
    /// samples it wrote. Zero when it has no letters or digits.
    /// </summary>
    /// <param name="radio">Plays it as heard through a radio, on top of the voice.</param>
    public int Render(string text, SpeechVoice voice, bool radio, SpeechVoiceBuffers buffers)
    {
        var blips = Blips(text);
        if (blips.Count == 0)
            return 0;

        // Seeded from the text, so every listener renders the same line the same way.
        _noise = new NoiseSource((int) SpeechVoice.Hash(text));
        var style = Style.For(voice.Type);

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

        var slot = style.LetterSeconds / voice.Pace * SampleRate;
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

        Speak(buffer, length, blips, starts, voice, style);

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

        ApplyEffect(buffer, style.Effect);
        LowPass(buffer, style.LowPass);
        HighPass(buffer, 80f);

        if (radio)
            Radio(buffer);
        else
            Room(buffer);

        ToPcm(buffer, buffers.Pcm);
        return buffer.Length;
    }

    /// <summary>Renders every letter into the buffer, each its own blip.</summary>
    private void Speak(Span<float> buffer,
        int length,
        List<Blip> blips,
        int[] starts,
        SpeechVoice voice,
        Style style)
    {
        var type = voice.Type;
        var melody = Melody.For(voice.Melody);
        var intonation = style.Intonation * voice.Intonation * melody.Intonation;
        var pitch = voice.Pitch * style.F0Scale;
        var formantScale = BaseFormantScale * voice.Pitch * style.FormantScale / voice.Size;
        var rd = style.Rd;
        var whisperAmount = style.Whisper;

        var tilt = new float[MaxHarmonics + 1];
        var shoutTilt = new float[MaxHarmonics + 1];
        for (var k = 1; k <= MaxHarmonics; k++)
        {
            tilt[k] = LfHarmonic(k, rd) * MathF.Pow(k, 1f - style.Tilt);
            shoutTilt[k] = LfHarmonic(k, rd * 0.7f) * MathF.Pow(k, 1f - style.Tilt);
        }

        var ampFrom = new float[MaxHarmonics + 1];
        var ampTo = new float[MaxHarmonics + 1];
        var harmonics = 0;

        // Pitch glides over about 20 ms, near the fastest a person can move between notes (Xu and Sun 2002).
        var f0Glide = Smoothing(0.02f);
        var gainGlide = Smoothing(0.0015f);
        var formantGlide = 1f - MathF.Exp(-Block / (0.012f * SampleRate));
        var attack = 0.005f * SampleRate;
        var fade = 0.012f * SampleRate;

        var formants = Eh * formantScale;
        var f0 = BaseF0 * pitch;
        var f0Target = f0;
        var frication = new NoiseBand();
        var breath = new NoiseBand();
        var whisper1 = new NoiseBand();
        var whisper2 = new NoiseBand();
        var whisper3 = new NoiseBand();
        var driveNorm = style.Drive > 1f ? 1f / MathF.Tanh(style.Drive) : 1f;

        var voiceGain = 0f;
        var fricationGain = 0f;
        var aspirationGain = 0f;
        var phase = 0f;
        var periodRate = 1f;
        var periodAmp = 1f;
        var alternate = false;
        var jitter = 0f;
        var shake = 0f;
        var shakeStep = MathF.Tau * style.VibratoRate / SampleRate;
        var wobblePhase = 0f;

        var words = 0;
        foreach (var blip in blips)
        {
            words = Math.Max(words, blip.Word + 1);
        }

        var wordFrom = new int[words];
        var wordTo = new int[words];
        for (var b = 0; b < blips.Count; b++)
        {
            if (blips[b].Gap)
                continue;

            var w = blips[b].Word;
            if (wordTo[w] == 0)
                wordFrom[w] = starts[b];
            wordTo[w] = starts[b + 1];
        }
        var letterPitch = 1f;
        var popStart = new int[MaxPops];
        var popFrequency = new float[MaxPops];
        var popDecay = new float[MaxPops];
        var popEnd = new int[MaxPops];
        var popFade = new float[MaxPops];
        var popAmp = new float[MaxPops];

        // A slow pitch and loudness swing, each cycle 0.33 to 0.47 s long.
        var swingPhase = 0f;
        var swingStep = MathF.Tau / (0.4f * SampleRate);
        var swingDepth = (0.03f + 0.08f * intonation) * melody.Swing;

        for (var b = 0; b < blips.Count; b++)
        {
            var start = starts[b];
            if (start >= length)
                break;

            var blip = blips[b];
            var slotLength = starts[b + 1] - start;
            var end = Math.Min(starts[b + 1], length);

            if (blip.Gap)
            {
                voiceGain = fricationGain = aspirationGain = 0f;
                continue;
            }

            var blipTilt = blip.Shout ? shoutTilt : tilt;

            // Each letter gets its own note, kept a step away from the last one.
            var spread = 0.7f * style.PitchVariance * voice.PitchVariance * style.PitchSpread * melody.Spread;

            var note = 1f + spread * _noise.Next();
            var step = spread > 0f ? 0.02f * style.PitchSpread * MathF.Min(1f, voice.PitchVariance) : 0f;
            if (MathF.Abs(note - letterPitch) < step)
                note = letterPitch + (note >= letterPitch ? step : -step);
            letterPitch = note;

            var blipGain = blip.Gain;
            var lastLift = 1f;
            if (blip.Last && blip.Ending == '?')
                lastLift = 1.13f + 0.17f * MathF.Min(1f, intonation / FullIntonation);
            if (blip.Last && blip.Ending == '!')
                blipGain *= 1.25f;

            var tune = letterPitch * Contour(blip, intonation) * blip.Lift * lastLift;
            if (melody.Snap)
                tune = SnapToScale(tune);
            var blipF0 = BaseF0 * pitch * tune;
            var blipFormants = 1f + 0.03f * _noise.Next();
            var slotSeconds = slotLength / (float) SampleRate;

            // Micro-prosody: pitch starts a little high after a voiceless consonant, a little low after a voiced one.
            var bump = blip.Onset.Kind == SoundKind.None || melody.Snap ? 0f : blip.Onset.Voiced ? -0.03f : 0.05f;
            var onsetEnd = blip.ConsonantOnly ? 0.8f
                : blip.OnsetShare > 0f ? blip.OnsetShare
                : OnsetLength(blip.Onset.Kind);
            if (blip.Onset.Kind is SoundKind.Fricative or SoundKind.Affricate && !blip.ConsonantOnly)
                onsetEnd = MathF.Min(0.7f, onsetEnd * style.FricativeStretch);
            var trilled = blip.Onset.Kind == SoundKind.Liquid || blip.Coda.Kind == SoundKind.Liquid;
            var wordStart = wordFrom[blip.Word];
            var wordLength = MathF.Max(1f, wordTo[blip.Word] - wordFrom[blip.Word]);
            var hasCoda = blip.Coda.Kind != SoundKind.None;
            var codaStart = hasCoda ? 0.5f : 1f;
            var noiseInCoda = false;
            var snap = b == 0 || blips[b - 1].Gap;
            var endsWord = b + 1 >= blips.Count || blips[b + 1].Gap;
            var wordFade = MathF.Min(WordFadeSeconds * SampleRate, 0.5f * slotLength);
            wobblePhase = MathF.Tau * (0.5f + 0.5f * _noise.Next());

            if (blip.Onset.NoiseGain > 0f)
                TuneNoise(ref frication, blip.Onset, style);

            // Small bubble pops on the vowel: damped sines that rise as they fade (van den Doel 2005).
            var popCount = 0;
            if (style.Pops > 0f && !blip.ConsonantOnly)
            {
                popCount = Math.Clamp(3 + (int) MathF.Round(_noise.Next()), 2, MaxPops);
                for (var k = 0; k < popCount; k++)
                {
                    popStart[k] = start + (int) (slotLength * (0.25f + 0.25f * (_noise.Next() + 1f)));
                    popFrequency[k] = 1150f + 650f * _noise.Next();
                    popDecay[k] = 0.014f + 0.006f * _noise.Next();
                    popEnd[k] = popStart[k] + (int) (4.6f * popDecay[k] * SampleRate);
                    popFade[k] = MathF.Exp(-1f / (popDecay[k] * SampleRate));
                }
            }

            for (var i = start; i < end; i++)
            {
                var t = (i - start) / (float) slotLength;

                Consonant sound;
                float u;
                Vector3 target;
                float voiced, fric, aspiration;

                if (t < onsetEnd)
                {
                    sound = blip.Onset;
                    u = t / onsetEnd;
                    target = Vector3.Lerp(new Vector3(sound.F1, sound.F2, sound.F3), blip.VowelStart, Coarticulation);
                    (voiced, fric, aspiration) = ConsonantGains(sound, u);
                }
                else if (t < codaStart)
                {
                    sound = blip.Onset;
                    u = (t - onsetEnd) / (codaStart - onsetEnd);
                    target = Vector3.Lerp(Vector3.Lerp(blip.VowelStart, blip.VowelEnd, u), style.VowelTarget,
                        style.VowelPull);
                    (voiced, fric, aspiration) = blip.ConsonantOnly ? (0f, 0f, 0f) : (1f, 0f, 0f);
                }
                else
                {
                    sound = blip.Coda;
                    u = (t - codaStart) / (1f - codaStart);
                    target = new Vector3(sound.F1, sound.F2, sound.F3);
                    (voiced, fric, aspiration) = ConsonantGains(sound, 0.5f + 0.5f * u);

                    if (!noiseInCoda && sound.NoiseGain > 0f)
                    {
                        TuneNoise(ref frication, sound, style);
                        noiseInCoda = true;
                    }
                }

                // Eases out at the end of the slot, so letters run back to back without a click.
                var cut = MathF.Min(1f, (start + slotLength - i) / fade);
                var envelope = MathF.Min(MathF.Min(1f, (i - start) / attack), cut * cut * (3f - 2f * cut));

                // The last letter of a word has silence after it, where a sharp cut clicks, so it eases out for longer.
                if (endsWord)
                {
                    var ease = MathF.Min(1f, (start + slotLength - i) / wordFade);
                    envelope = MathF.Min((i - start) / attack, ease * ease * (3f - 2f * ease));
                    envelope = MathF.Min(1f, envelope);
                }

                envelope *= blipGain;

                if (i % Block == 0)
                {
                    var wordT = Math.Clamp((i - wordStart) / wordLength, 0f, 1f);
                    var shape = ShapeFor(type, t, wordT);
                    var mouth = Vector3.Lerp(target, shape.Formants, shape.Pull) * (formantScale * blipFormants);
                    if (style.Wobble > 0f)
                    {
                        var ripple = 1f + 0.12f * style.Wobble
                            * MathF.Sin(MathF.Tau * 9f * i / SampleRate + wobblePhase);
                        mouth.X *= ripple;
                        mouth.Y *= 2f - ripple;
                    }

                    mouth.X *= shape.F1;
                    mouth.Y *= shape.F2;

                    // A word's first letter starts on its own mouth shape, later letters glide from the one before.
                    formants = snap ? mouth : formants + (mouth - formants) * formantGlide;
                    snap = false;
                    Array.Copy(ampTo, ampFrom, ampTo.Length);
                    harmonics = Harmonics(ampTo, blipTilt, f0, formants, formantScale, style);
                    breath.Tune(MathF.Min(formants.Z, MaxFrequency), 3000f * formantScale, SampleRate);
                    if (whisperAmount > 0f)
                    {
                        var width = formantScale * style.Bandwidth;
                        whisper1.Tune(MathF.Min(formants.X, MaxFrequency), 150f * width, SampleRate);
                        whisper2.Tune(MathF.Min(formants.Y, MaxFrequency), 200f * width, SampleRate);
                        whisper3.Tune(MathF.Min(formants.Z, MaxFrequency), 300f * width, SampleRate);
                    }
                    var sinceVowel = MathF.Max(0f, t - onsetEnd) * slotSeconds;
                    f0Target = blipF0 * shape.F0 * (1f + bump * MathF.Exp(-sinceVowel / 0.03f));
                }

                f0 += (f0Target - f0) * f0Glide;
                shake += shakeStep;
                swingPhase += swingStep;
                if (swingPhase >= MathF.Tau)
                {
                    swingPhase -= MathF.Tau;
                    swingStep = MathF.Tau / ((0.4f + 0.07f * _noise.Next()) * SampleRate);
                }

                var swing = swingDepth * MathF.Sin(swingPhase);
                var currentF0 = f0 * (1f + swing);
                if (style.VibratoDepth > 0f)
                    currentF0 *= 1f + style.VibratoDepth * MathF.Sin(shake);
                var growl = style.Growl > 0f ? MathF.Sin(MathF.Tau * 40f * i / SampleRate) : 0f;
                if (style.Growl > 0f)
                    currentF0 *= 1f + 0.04f * style.Growl * growl;
                if (style.Trill > 0f && trilled)
                    currentF0 *= 1f + 0.08f * style.Trill * MathF.Sin(MathF.Tau * 25f * i / SampleRate);

                phase += currentF0 * periodRate / SampleRate;
                if (phase >= 1f)
                {
                    phase -= 1f;
                    jitter = 0.7f * jitter + 0.3f * _noise.Next();
                    periodRate = 1f + style.Jitter * 2f * jitter;
                    periodAmp = 1f + style.Shimmer * _noise.Next();
                    alternate = !alternate;
                    if (alternate)
                        periodAmp *= MathF.Max(0f, 1f - style.Subharmonic);
                }

                // Sum of harmonics by the sine recurrence sin(k x) = 2 cos(x) sin((k-1) x) - sin((k-2) x).
                var x = MathF.Tau * phase;
                var cos2 = 2f * MathF.Cos(x);
                var current = MathF.Sin(x);
                var previous = 0f;
                var mix = (i % Block) / (float) Block;
                var harmonic = 0f;
                for (var k = 1; k <= harmonics; k++)
                {
                    harmonic += (ampFrom[k] + (ampTo[k] - ampFrom[k]) * mix) * current;
                    var next = cos2 * current - previous;
                    previous = current;
                    current = next;
                }

                voiceGain += (voiced - voiceGain) * gainGlide;
                fricationGain += (fric - fricationGain) * gainGlide;
                aspirationGain += (aspiration - aspirationGain) * gainGlide;

                var noise = _noise.Next();
                var boost = sound.NoiseFrequency >= 2500f ? style.FricationBoost : 1f;
                var breathNoise = breath.Process(noise);

                // Breath rides on the open phase of each glottal cycle (Klatt and Klatt 1990).
                var open = phase < 0.6f ? MathF.Sin(MathF.PI * phase / 0.6f) : 0.15f;
                var breathAmount = style.Breath;
                if (style.BreathGust > 0f)
                {
                    var gust = 0.5f + 0.5f * MathF.Sin(MathF.Tau * 2.5f * i / SampleRate);
                    breathAmount += 0.35f * style.BreathGust * gust;
                }

                if (whisperAmount > 0f)
                {
                    var whispered = whisper1.Process(noise) + 0.7f * whisper2.Process(noise)
                                    + 0.4f * whisper3.Process(noise);
                    harmonic = harmonic * (1f - whisperAmount) + whispered * whisperAmount * WhisperLevel;
                }

                var voicedPart = voiceGain * periodAmp
                                 * (harmonic * (1f - breathAmount) + breathNoise * open * breathAmount * BreathLevel);
                var noisePart = frication.Process(noise) * fricationGain * sound.NoiseGain * boost * FricationLevel
                                + breathNoise * aspirationGain * AspirationLevel;
                var sample = (voicedPart + noisePart) * envelope * (1f + 0.5f * swing);

                if (style.Bubble > 0f && t >= onsetEnd)
                {
                    var blub = 0.5f + 0.5f * MathF.Sin(MathF.Tau * 15f * i / SampleRate + wobblePhase);
                    sample *= 1f - 0.4f * style.Bubble * blub;
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

                for (var k = 0; k < popCount; k++)
                {
                    if (i < popStart[k] || i > popEnd[k])
                        continue;

                    var dt = (i - popStart[k]) / (float) SampleRate;
                    popAmp[k] = i == popStart[k] ? 1f : popAmp[k] * popFade[k];

                    var popPhase = MathF.Tau * popFrequency[k] * (dt + 0.05f * dt * dt / popDecay[k]);
                    sample += PopLevel * style.Pops * blipGain * popAmp[k] * MathF.Sin(popPhase);
                }

                buffer[i] = sample;
            }
        }
    }

    /// <summary>Tunes the hiss of a consonant. A hissing species widens s, z and sh and moves them higher.</summary>
    private static void TuneNoise(ref NoiseBand band, in Consonant sound, in Style style)
    {
        var frequency = sound.NoiseFrequency;
        var bandwidth = sound.NoiseBandwidth;
        if (style.HissWidth > 1f && sound.Kind == SoundKind.Fricative && bandwidth <= 2000f)
        {
            var amount = (style.HissWidth - 1f) / 2f;
            frequency = MathHelper.Lerp(frequency, MathF.Max(frequency, 5500f), amount);
            bandwidth *= style.HissWidth;
        }

        band.Tune(MathF.Min(frequency, MaxFrequency), bandwidth, SampleRate);
    }

    /// <summary>Fills harmonic amplitudes, normalised to unit power, and returns how many fit under Nyquist.</summary>
    // Amplitudes come from the glottal tilt and the formant curves at each harmonic, so every vowel is about as loud.
    private static int Harmonics(float[] amps,
        float[] tilt,
        float f0,
        Vector3 formants,
        float formantScale,
        in Style style)
    {
        // Harmonics well above F4 add almost nothing, so stop there.
        var f4 = formants.Z + 1000f * formantScale;
        var top = MathF.Min(MaxFrequency, 1.4f * f4);
        var count = Math.Min(MaxHarmonics, (int) (top / MathF.Max(f0, 1f)));
        var power = 0f;

        // Wider formants at high pitch, so widely spaced harmonics still sound each vowel evenly.
        var widen = 0.2f * f0;
        var width = style.Bandwidth;

        // A nasal resonance and anti-resonance. Equal at 0 they cancel, then the anti-resonance moves up.
        var nasalPole = 250f * formantScale;
        var nasalZero = nasalPole * (1f + style.Nasal);
        var nasalWidth = 100f * formantScale;

        var whistle = style.Whistle > 0f ? (int) MathF.Round(1800f * formantScale / MathF.Max(f0, 1f)) : 0;

        for (var k = 1; k <= count; k++)
        {
            var f = k * f0;
            var a = tilt[k]
                    * Resonance(f, formants.X, (80f * formantScale + widen) * width)
                    * Resonance(f, formants.Y, (100f * formantScale + widen) * width)
                    * Resonance(f, formants.Z, (140f * formantScale + widen) * width)
                    * Resonance(f, f4, (250f * formantScale + widen) * width);
            if (style.Nasal > 0f)
                a *= Resonance(f, nasalPole, nasalWidth) / Resonance(f, nasalZero, nasalWidth);
            if (k == whistle)
                a *= 1f + 3f * style.Whistle;
            amps[k] = a;
            power += a * a;
        }

        var norm = power > 0f ? 1f / MathF.Sqrt(power) : 0f;
        for (var k = 1; k <= count; k++)
        {
            amps[k] *= norm;
        }

        for (var k = count + 1; k <= MaxHarmonics; k++)
        {
            amps[k] = 0f;
        }

        return count;
    }

    /// <summary>Radiated LF glottal spectrum at harmonic k, from one Rd value (Fant 1995).</summary>
    // Low Rd is pressed and bright, high Rd soft and breathy. A peak near the glottal formant, then a roll-off above
    // the return-phase corner.
    private static float LfHarmonic(int k, float rd)
    {
        rd = Math.Clamp(rd, 0.3f, 4f);
        var ra = (-1f + 4.8f * rd) / 100f;
        var rk = (22.4f + 11.8f * rd) / 100f;
        var rg = 0.25f * rk / (0.11f * rd / (0.5f + 1.2f * rk) - ra);
        var glottal = k / rg;
        var corner = k * MathF.Tau * ra;
        return glottal / (1f + glottal * glottal) / MathF.Sqrt(1f + corner * corner);
    }

    /// <summary>Gain of a two-pole resonance with unity gain at 0 Hz, the analog form of a Klatt formant.</summary>
    private static float Resonance(float f, float centre, float bandwidth)
    {
        var ratio = f / centre;
        var a = 1f - ratio * ratio;
        var b = f * bandwidth / (centre * centre);
        return 1f / MathF.Sqrt(a * a + b * b);
    }

    /// <summary>Per-sample step of a one-pole glide with this time constant.</summary>
    private static float Smoothing(float seconds)
    {
        return 1f - MathF.Exp(-1f / (seconds * SampleRate));
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

    /// <summary>16-bit samples levelled by loudness, so one loud burst doesn't make the whole line quiet.</summary>
    // RMS is taken over the voiced part only, then the gain is capped so the peak stays under TargetPeak.
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
