using System.Numerics;

namespace Content.Client._WF.Voice;

public sealed partial class SpeechVoiceSynth
{
    private enum SoundKind : byte
    {
        None,
        Plosive,
        Affricate,
        Fricative,
        Nasal,
        Liquid,
        Aspirate,
    }

    /// <summary>One consonant's recipe: mouth shape, voicing, and the hiss or burst that marks it.</summary>
    private struct Consonant
    {
        public SoundKind Kind;
        public float F1;
        public float F2;
        public float F3;
        public bool Voiced;

        /// <summary>Formant bandwidths in Hz while the mouth holds this shape. Zero takes the vowel defaults.</summary>
        public Vector3 Bandwidth;

        /// <summary>Loudness of the voice while the consonant holds, against the vowel.</summary>
        public float Level;

        /// <summary>The nasal dip that tells m, n and ng apart, in Hz. 0 for none.</summary>
        public float NasalZero;

        /// <summary>Up to three noise resonances in Hz.</summary>
        public Vector3 NoiseFrequency;

        /// <summary>Bandwidth of each noise resonance in Hz.</summary>
        public Vector3 NoiseBandwidth;

        /// <summary>Loudness of each noise resonance against the others.</summary>
        public Vector3 NoiseShape;

        /// <summary>Noise that skips the resonances, flat like breath past the lips.</summary>
        public float NoiseFlat;

        /// <summary>Noise loudness against the vowel, in dB.</summary>
        public float NoiseDb;

        /// <summary>How long a stop's burst lasts, in seconds.</summary>
        public float Burst;

        /// <summary>Breath after a voiceless stop's burst, in seconds.</summary>
        public float Aspiration;

        /// <summary>How long the consonant holds, in seconds: a stop's closure, or the hiss, hum or breath.</summary>
        public float Hold;
    }

    /// <summary>What a letter asks for at one moment: voice and noise loudness, and where the mouth is.</summary>
    private struct Moment
    {
        public float Voice;

        /// <summary>Hiss or burst through the consonant's own resonances.</summary>
        public float Noise;

        /// <summary>Breath through the mouth's current shape, for h and after p, t and k.</summary>
        public float Breath;

        /// <summary>The mouth holds the consonant's shape rather than the vowel's.</summary>
        public bool AtLocus;

        public bool Nasal;

        /// <summary>A stop's closure and release, where the voice drops out even inside a word.</summary>
        public bool Stopped;
    }

    /// <summary>One letter or digit: an onset, a vowel that may glide, a coda. A gap is a pause instead.</summary>
    private struct Blip
    {
        public bool Gap;

        /// <summary>Length in letter slots.</summary>
        public float Length;

        public Consonant Onset;
        public Vector3 VowelStart;
        public Vector3 VowelEnd;

        /// <summary>Formant bandwidths of the vowel in Hz.</summary>
        public Vector3 VowelBandwidth;
        public Consonant Coda;

        /// <summary>Position in its phrase, 0 at the start and 1 at the end.</summary>
        public float PhrasePosition;

        /// <summary>The punctuation that ends its phrase: '.', '?', '!', ',' or '…'.</summary>
        public char Ending;

        /// <summary>Loudness against other letters, up for shouts, down as an ellipsis trails off.</summary>
        public float Gain;

        /// <summary>Pitch multiplier, raised for shouted words.</summary>
        public float Lift;

        /// <summary>A shouted word, sung with a pressed, brighter source.</summary>
        public bool Shout;

        /// <summary>Index of its word, so pitch steps once per word.</summary>
        public int Word;

        /// <summary>The last letter of its phrase, where ? lifts the pitch and ! the loudness.</summary>
        public bool Last;

        /// <summary>A consonant on its own with no vowel, such as a trailing hiss.</summary>
        public bool ConsonantOnly;
    }

    /// <summary>The quiet hum under a voiced stop's closure.</summary>
    private const float VoiceBar = 0.12f;

    /// <summary>The lowest the voice dips at a consonant inside a word, so a word sounds as one stream.</summary>
    private const float StreamFloor = 0.8f;

    private const float VoicelessReturn = 0.015f;
    private const float VoicedReturn = 0.006f;

    /// <summary>How long a burst takes to reach full loudness, in seconds.</summary>
    private const float BurstRise = 0.0025f;

    /// <summary>The shortest fade a hiss or breath gets at its end, in seconds.</summary>
    private const float NoiseFall = 0.006f;

    /// <summary>How long a glide (y, w, l, r) takes to open into its vowel, in seconds.</summary>
    private const float GlideSeconds = 0.022f;

    /// <summary>Voice loudness of a glide against the vowel, 10 dB down.</summary>
    private const float GlideLevel = 0.32f;

    private static readonly Vector3 Ee = new(290, 2540, 3160);
    private static readonly Vector3 Ih = new(410, 2230, 2810);
    private static readonly Vector3 Eh = new(570, 2090, 2730);
    private static readonly Vector3 Ah = new(700, 1220, 2600);
    private static readonly Vector3 Uh = new(640, 1190, 2390);
    private static readonly Vector3 Oh = new(570, 840, 2410);
    private static readonly Vector3 Uu = new(440, 1020, 2240);
    private static readonly Vector3 Oo = new(335, 910, 2455);
    private static readonly Vector3 WStart = new(300, 700, 2200);

    private static readonly Vector3 DefaultBandwidth = new(60, 90, 150);

    private static Vector3 BandwidthOf(Vector3 vowel)
    {
        if (vowel == Ee)
            return new Vector3(45, 200, 400);
        if (vowel == Ih)
            return new Vector3(50, 100, 140);
        if (vowel == Eh)
            return new Vector3(60, 90, 200);
        if (vowel == Ah)
            return new Vector3(130, 70, 160);
        if (vowel == Uh)
            return new Vector3(80, 50, 140);
        if (vowel == Oh)
            return new Vector3(80, 70, 70);
        if (vowel == Uu)
            return new Vector3(80, 100, 80);
        if (vowel == Oo)
            return new Vector3(65, 110, 140);
        return DefaultBandwidth;
    }

    private static readonly (Vector3 F, Vector3 B, Vector3 Shape, float Flat) LipBurst =
        (new(800, 1800, 3200), new(800, 1200, 2000), new(1f, 0.35f, 0.15f), 0.5f);
    private static readonly (Vector3 F, Vector3 B, Vector3 Shape, float Flat) TipBurst =
        (new(3500, 4800, 6300), new(1500, 2000, 2500), new(0.5f, 1f, 0.7f), 0.2f);
    private static readonly (Vector3 F, Vector3 B, Vector3 Shape, float Flat) BackBurst =
        (new(1800, 2700, 3800), new(600, 900, 1500), new(1f, 0.45f, 0.1f), 0f);
    private static readonly (Vector3 F, Vector3 B, Vector3 Shape, float Flat) SHiss =
        (new(5000, 6800, 8500), new(1500, 2000, 2500), new(0.6f, 1f, 0.5f), 0f);
    private static readonly (Vector3 F, Vector3 B, Vector3 Shape, float Flat) ShHiss =
        (new(2600, 3800, 5500), new(800, 1400, 2500), new(0.7f, 1f, 0.4f), 0f);
    private static readonly (Vector3 F, Vector3 B, Vector3 Shape, float Flat) FHiss =
        (new(7000, 4000, 2000), new(6000, 3000, 2000), new(0.5f, 0.2f, 0.1f), 1f);
    private static readonly (Vector3 F, Vector3 B, Vector3 Shape, float Flat) ThHiss =
        (new(8000, 5000, 3000), new(4000, 3000, 2000), new(0.8f, 0.3f, 0.1f), 0.7f);

    private static readonly Consonant B = Stop(800, 2300, true, LipBurst, -15f, 0.008f, 0f);
    private static readonly Consonant P = Stop(800, 2300, false, LipBurst, -12f, 0.012f, 0.012f);
    private static readonly Consonant D = Stop(1800, 2700, true, TipBurst, -13f, 0.008f, 0f);
    private static readonly Consonant T = Stop(1800, 2700, false, TipBurst, -9f, 0.012f, 0.01f);
    private static readonly Consonant G = Stop(1700, 2100, true, BackBurst, -15f, 0.01f, 0f);
    private static readonly Consonant K = Stop(1700, 2100, false, BackBurst, -12f, 0.02f, 0.012f);
    private static readonly Consonant J = Hiss(SoundKind.Affricate, 300, 2000, 2700, true, ShHiss, -14f, 0.016f);
    private static readonly Consonant S = Hiss(SoundKind.Fricative, 300, 1700, 2600, false, SHiss, -11f, 0.024f);
    private static readonly Consonant Z = Hiss(SoundKind.Fricative, 300, 1700, 2600, true, SHiss, -14f, 0.024f);
    private static readonly Consonant F = Hiss(SoundKind.Fricative, 400, 1200, 2400, false, FHiss, -20f, 0.02f);
    private static readonly Consonant V = Hiss(SoundKind.Fricative, 400, 1200, 2400, true, FHiss, -24f, 0.02f);
    private static readonly Consonant Th = Hiss(SoundKind.Fricative, 400, 1800, 2600, false, ThHiss, -23f, 0.02f);
    private static readonly Consonant H = new() { Kind = SoundKind.Aspirate, NoiseDb = -16f, Hold = 0.018f };
    private static readonly Consonant M = Voiced(SoundKind.Nasal, 280, 1000, 2300, 0.3f, 0.016f, 900f);
    private static readonly Consonant N = Voiced(SoundKind.Nasal, 280, 1780, 2600, 0.3f, 0.016f, 1700f);

    private static readonly Consonant L = Glide(310, 1050, 2880, new(50, 100, 280));
    private static readonly Consonant R = Glide(310, 1060, 1380, new(70, 100, 120));
    private static readonly Consonant W = Glide(290, 610, 2150, new(50, 80, 100));
    private static readonly Consonant Y = Glide(260, 2490, 3100, new(40, 250, 500));
    private static readonly Consonant Silent = default;

    /// <summary>The syllable for one lower-case letter, the same wherever it falls.</summary>
    private static Blip Letter(char c, bool wordStart)
    {
        return c switch
        {
            'a' => Syllable(Silent, Ah),
            'e' => Syllable(Silent, Eh),
            'i' => Syllable(Silent, Ee),
            'o' => Syllable(Silent, Oh),
            'u' => Syllable(Silent, Oo),
            'y' => wordStart ? Syllable(Y, Ee) : Syllable(Silent, Ih),
            'b' => Syllable(B, Uh),
            'p' => Syllable(P, Ah),
            'd' => Syllable(D, Uh),
            't' => Syllable(T, Ah),
            'g' => Syllable(G, Ah),
            'j' => Syllable(J, Uh),
            's' => Syllable(S, Ah),
            'z' => Syllable(Z, Uh),
            'f' => Syllable(F, Ah),
            'v' => Syllable(V, Uh),
            'h' => Syllable(H, Ah),
            'm' => Syllable(M, Ah),
            'n' => Syllable(N, Ah),
            'l' => Syllable(L, Ah),
            'r' => Syllable(R, Ah),
            'w' => Syllable(W, Ah),
            'q' => Syllable(K, WStart, Ah),
            'x' => Syllable(K, Ah, Ah, S),
            _ => Syllable(K, Ah),
        };
    }

    /// <summary>Each digit as one blip: its number word squeezed into a syllable.</summary>
    private static readonly Blip[] Digits =
    {
        Syllable(Z, Ee, Oh),
        Syllable(W, Uh, Uh, N),
        Syllable(T, Oo),
        Syllable(Th, Ee),
        Syllable(F, Oh, Oh, R),
        Syllable(F, Ah, Ih, V),
        Syllable(S, Ih, Ih, S),
        Syllable(S, Eh, Eh, V),
        Syllable(Silent, Eh, Ih, T),
        Syllable(N, Ah, Ih, N),
    };

    private static Consonant Stop(float f2,
        float f3,
        bool voiced,
        (Vector3 F, Vector3 B, Vector3 Shape, float Flat) noise,
        float noiseDb,
        float burst,
        float aspiration)
    {
        var stop = Hiss(SoundKind.Plosive, 300, f2, f3, voiced, noise, noiseDb, voiced ? 0.007f : 0.01f);
        stop.Burst = burst;
        stop.Aspiration = aspiration;
        return stop;
    }

    private static Consonant Hiss(SoundKind kind,
        float f1,
        float f2,
        float f3,
        bool voiced,
        (Vector3 F, Vector3 B, Vector3 Shape, float Flat) noise,
        float noiseDb,
        float hold)
    {
        return new Consonant
        {
            Kind = kind,
            F1 = f1,
            F2 = f2,
            F3 = f3,
            Voiced = voiced,
            Level = voiced ? 0.5f : 0f,
            NoiseFrequency = noise.F,
            NoiseBandwidth = noise.B,
            NoiseShape = noise.Shape,
            NoiseFlat = noise.Flat,
            NoiseDb = noiseDb,
            Burst = 0.006f,
            Hold = hold,
        };
    }

    private static Consonant Voiced(SoundKind kind,
        float f1,
        float f2,
        float f3,
        float level,
        float hold,
        float nasalZero = 0f)
    {
        return new Consonant
        {
            Kind = kind,
            F1 = f1,
            F2 = f2,
            F3 = f3,
            Voiced = true,
            Level = level,
            NasalZero = nasalZero,
            Hold = hold,
        };
    }

    private static Consonant Glide(float f1, float f2, float f3, Vector3 bandwidth)
    {
        var glide = Voiced(SoundKind.Liquid, f1, f2, f3, GlideLevel, 0.01f);
        glide.Bandwidth = bandwidth;
        return glide;
    }

    private static Blip Syllable(Consonant onset, Vector3 vowel)
    {
        return Syllable(onset, vowel, vowel, Silent);
    }

    private static Blip Syllable(Consonant onset, Vector3 vowelStart, Vector3 vowelEnd, Consonant coda = default)
    {
        return new Blip
        {
            Length = 1f,
            Onset = onset,
            VowelStart = vowelStart,
            VowelEnd = vowelEnd,
            VowelBandwidth = BandwidthOf(vowelStart),
            Coda = coda,
            Gain = 1f,
            Lift = 1f,
        };
    }

    /// <summary>The closure before an affricate's hiss.</summary>
    private const float AffricateClosure = 0.008f;

    /// <summary>How much a consonant shrinks to fit a letter too short for its own length.</summary>
    private static float Fit(in Consonant c, float slot, float stretch)
    {
        var natural = c.Kind switch
        {
            SoundKind.Plosive => c.Hold + c.Burst + c.Aspiration,
            SoundKind.Affricate => AffricateClosure + c.Hold,
            SoundKind.Fricative => c.Hold * stretch,
            SoundKind.Liquid => c.Hold + GlideSeconds,
            _ => c.Hold,
        };

        return natural > 0f ? MathF.Min(1f, 0.7f * slot / natural) : 1f;
    }

    /// <summary>How long a letter's consonant holds before its vowel, in seconds.</summary>
    private static float ConsonantSeconds(in Consonant sound, float slot, float stretch)
    {
        var fit = Fit(sound, slot, stretch);
        return sound.Kind switch
        {
            SoundKind.Affricate => (AffricateClosure + sound.Hold) * fit,
            SoundKind.Fricative => sound.Hold * stretch * fit,
            SoundKind.None => 0f,
            _ => sound.Hold * fit,
        };
    }

    /// <summary>A letter's onset consonant at <paramref name="s"/> seconds into the letter.</summary>
    private static Moment OnsetAt(in Consonant c, float s, float slot, float stretch)
    {
        var end = ConsonantSeconds(c, slot, stretch);
        switch (c.Kind)
        {
            case SoundKind.Plosive:
            case SoundKind.Affricate:
            {
                var fit = Fit(c, slot, stretch);
                var release = (c.Kind == SoundKind.Affricate ? AffricateClosure : c.Hold) * fit;
                if (s < release)
                    return new Moment { Voice = c.Voiced ? VoiceBar : 0f, AtLocus = true, Stopped = true };

                var since = s - release;
                var burstLength = c.Burst * fit;
                var burst = Ramp(since, BurstRise) * (1f - Ramp(since - 0.3f * burstLength, 0.7f * burstLength));
                if (c.Kind == SoundKind.Affricate)
                {
                    var hold = end - release;
                    var hiss = Ramp(since, MathF.Min(0.025f, 0.5f * hold)) * (1f - Ramp(since - hold, NoiseFall));
                    var affricateVoice = c.Voiced
                        ? c.Level + (1f - c.Level) * Ramp(since - hold, 0.005f)
                        : Ramp(since - hold + 0.003f, 0.005f);
                    return new Moment
                    {
                        Voice = affricateVoice,
                        Noise = MathF.Max(burst, hiss),
                        AtLocus = since < hold,
                    };
                }

                if (c.Voiced)
                {
                    var back = since - 0.5f * burstLength;
                    return new Moment
                    {
                        Voice = VoiceBar + (1f - VoiceBar) * Ramp(back, VoicedReturn),
                        Noise = burst,
                        Stopped = back < VoicedReturn,
                    };
                }

                var onset = burstLength + c.Aspiration * fit;
                return new Moment
                {
                    Voice = Ramp(since - onset, VoicelessReturn),
                    Noise = burst,
                    Breath = Ramp(since - 0.5f * burstLength, 0.003f) * (1f - Ramp(since - onset, NoiseFall)),
                    Stopped = since < onset + VoicelessReturn,
                };
            }
            case SoundKind.Fricative:
            {
                var fall = MathF.Max(NoiseFall, 0.4f * end);
                var voice = c.Voiced
                    ? c.Level + (1f - c.Level) * Ramp(s - end, 0.005f)
                    : Ramp(s - 0.85f * end, 0.15f * end + 0.005f);
                var noise = Ramp(s, 0.35f * end) * (1f - Ramp(s - 0.8f * end, fall));
                return new Moment { Voice = voice, Noise = noise, AtLocus = s < end };
            }
            case SoundKind.Aspirate:
                return new Moment
                {
                    Voice = Ramp(s - 0.75f * end, 0.25f * end + 0.005f),
                    Breath = Ramp(s, 0.3f * end) * (1f - Ramp(s - 0.7f * end, MathF.Max(NoiseFall, 0.3f * end))),
                };
            case SoundKind.Nasal:
                return new Moment
                {
                    Voice = c.Level + (1f - c.Level) * Ramp(s - end, 0.005f),
                    AtLocus = s < end,
                    Nasal = s < end,
                };
            case SoundKind.Liquid:
                return new Moment { Voice = c.Level + (1f - c.Level) * Ramp(s - end, GlideSeconds), AtLocus = s < end };
            default:
                return new Moment { Voice = 1f };
        }
    }

    /// <summary>A letter's coda consonant at a time into a coda of a given length.</summary>
    private static Moment CodaAt(in Consonant c, float s, float length)
    {
        switch (c.Kind)
        {
            case SoundKind.Fricative:
            case SoundKind.Affricate:
                return new Moment
                {
                    Voice = c.Voiced ? 1f - (1f - c.Level) * Ramp(s, 0.005f) : 1f - Ramp(s, 0.3f * length),
                    Noise = Ramp(s, 0.4f * length) * (1f - Ramp(s - length + NoiseFall, NoiseFall)),
                    AtLocus = true,
                };
            case SoundKind.Nasal:
                return new Moment { Voice = 1f - (1f - c.Level) * Ramp(s, 0.005f), AtLocus = true, Nasal = true };
            case SoundKind.Liquid:
                return new Moment { Voice = 1f - (1f - c.Level) * Ramp(s, GlideSeconds), AtLocus = true };
            case SoundKind.Plosive:
                return new Moment { Voice = 1f - Ramp(s, 0.3f * length), AtLocus = true };
            default:
                return new Moment { Voice = 1f };
        }
    }

    /// <summary>0 before 0, 1 after <paramref name="length"/>, on a smooth curve between.</summary>
    private static float Ramp(float x, float length)
    {
        var u = length > 0f ? Math.Clamp(x / length, 0f, 1f) : x >= 0f ? 1f : 0f;
        return u * u * (3f - 2f * u);
    }
}
