namespace Content.Client._WF.Voice;

public sealed partial class SpeechVoiceSynth
{
    /// <summary>Lays a voice's post effect over the rendered line.</summary>
    private static void ApplyEffect(Span<float> buffer, VoiceEffect effect)
    {
        switch (effect)
        {
            case VoiceEffect.Robot:
                Robot(buffer);
                break;
            case VoiceEffect.Insect:
                Insect(buffer);
                break;
        }
    }

    /// <summary>Two one-pole low-passes in a row, a gentle 12 dB per octave roll-off that takes the edge off.</summary>
    private static void LowPass(Span<float> buffer, float frequency)
    {
        if (frequency <= 0f || frequency >= MaxFrequency)
            return;

        var k = OnePole(frequency);
        var a = 0f;
        var b = 0f;
        for (var i = 0; i < buffer.Length; i++)
        {
            a += (buffer[i] - a) * k;
            b += (a - b) * k;
            buffer[i] = b;
        }
    }

    /// <summary>A one-pole high-pass that takes out rumble and any DC offset.</summary>
    private static void HighPass(Span<float> buffer, float frequency)
    {
        var k = OnePole(frequency);
        var low = 0f;
        for (var i = 0; i < buffer.Length; i++)
        {
            low += (buffer[i] - low) * k;
            buffer[i] -= low;
        }
    }

    /// <summary>A small room: three early reflections and a short fading echo, mixed in quietly.</summary>
    private static void Room(Span<float> buffer)
    {
        const float wet = 0.1f;
        const float feedback = 0.35f;
        var size = (int) (0.023f * SampleRate);
        var tap1 = (int) (0.007f * SampleRate);
        var tap2 = (int) (0.011f * SampleRate);
        var tap3 = (int) (0.017f * SampleRate);
        var dry = new float[size];
        var echo = new float[size];

        for (var i = 0; i < buffer.Length; i++)
        {
            var slot = i % size;
            var x = buffer[i];

            var early = 0f;
            if (i >= tap1)
                early += 0.5f * dry[(i - tap1) % size];
            if (i >= tap2)
                early += 0.35f * dry[(i - tap2) % size];
            if (i >= tap3)
                early += 0.25f * dry[(i - tap3) % size];

            var late = i >= size ? dry[slot] + feedback * echo[slot] : 0f;
            echo[slot] = late;
            dry[slot] = x;
            buffer[i] = x + wet * (early + late);
        }
    }

    /// <summary>A 300 Hz to 3 kHz band, a soft clip and a quiet hiss that stops with the speech.</summary>
    private void Radio(Span<float> buffer)
    {
        var peak = 0f;
        foreach (var sample in buffer)
        {
            peak = MathF.Max(peak, MathF.Abs(sample));
        }

        if (peak <= 0f)
            return;

        var highPass = OnePole(300f);
        var lowPass = OnePole(3000f);
        var presence = new NoiseBand();
        presence.Tune(1800f, 900f, SampleRate);

        var clip = 1f / MathF.Tanh(RadioDrive);
        var hissRelease = Smoothing(0.008f);
        var hissGate = 0f;
        var low = 0f;
        var lowA = 0f;
        var lowB = 0f;

        for (var i = 0; i < buffer.Length; i++)
        {
            var x = buffer[i] / peak;
            low += (x - low) * highPass;
            x -= low;

            lowA += (x - lowA) * lowPass;
            lowB += (lowA - lowB) * lowPass;
            x = lowB + 0.6f * presence.Process(lowB);

            x = MathF.Tanh(RadioDrive * x) * clip;

            // The hiss follows the speech, so it can't sit in the gaps between words as static.
            hissGate = MathF.Max(MathF.Abs(x), hissGate * (1f - hissRelease));
            buffer[i] = x + 0.015f * MathF.Min(1f, 4f * hissGate) * _noise.Next();
        }
    }

    /// <summary>The smoothing factor of a one-pole filter with this corner frequency.</summary>
    private static float OnePole(float frequency)
    {
        return 1f - MathF.Exp(-MathF.Tau * frequency / SampleRate);
    }

    /// <summary>Bit crush and sample hold, then a ring modulator.</summary>
    private static void Robot(Span<float> buffer)
    {
        const int hold = 2;
        const float levels = 16f;
        var held = 0f;

        for (var i = 0; i < buffer.Length; i++)
        {
            if (i % hold == 0)
                held = MathF.Round(buffer[i] * levels) / levels;

            buffer[i] = held * MathF.Sin(MathF.Tau * 90f * i / SampleRate) * 1.4f;
        }
    }

    /// <summary>A brightened signal gated by a fast square buzz.</summary>
    private static void Insect(Span<float> buffer)
    {
        var previous = 0f;

        for (var i = 0; i < buffer.Length; i++)
        {
            var sample = buffer[i];
            var bright = sample - 0.7f * previous;
            previous = sample;

            var buzz = MathF.Sin(MathF.Tau * 120f * i / SampleRate) > 0f ? 1f : 0.2f;
            buffer[i] = bright * buzz * 1.3f;
        }
    }
}
