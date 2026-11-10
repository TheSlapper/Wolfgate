namespace Content.Client._WF.Voice;

/// <summary>Band-pass filter with a peak gain of 1, for fixed tunings such as the radio's presence band.</summary>
internal struct NoiseBand
{
    private float _b0;
    private float _a1;
    private float _a2;
    private float _x1;
    private float _x2;
    private float _y1;
    private float _y2;

    /// <summary>Sets the centre frequency and bandwidth, both in Hz.</summary>
    public void Tune(float frequency, float bandwidth, int sampleRate)
    {
        var w = MathF.Tau * frequency / sampleRate;
        var alpha = MathF.Sin(w) * bandwidth / (2f * frequency);
        var a0 = 1f + alpha;
        _b0 = alpha / a0;
        _a1 = -2f * MathF.Cos(w) / a0;
        _a2 = (1f - alpha) / a0;
    }

    /// <summary>Filters one sample.</summary>
    public float Process(float x)
    {
        var y = _b0 * (x - _x2) - _a1 * _y1 - _a2 * _y2;
        _x2 = _x1;
        _x1 = x;
        _y2 = _y1;
        _y1 = y;
        return y;
    }
}

/// <summary>Two-pole state-variable filter that stays stable and keeps its state when retuned mid-line.</summary>
internal struct Svf
{
    private float _k;
    private float _a1;
    private float _a2;
    private float _a3;
    private float _ic1;
    private float _ic2;

    public float Low;
    public float Band;
    public float High;

    /// <summary>Sets the centre frequency and bandwidth, both in Hz, keeping the filter's state.</summary>
    public void Tune(float frequency, float bandwidth, int sampleRate)
    {
        frequency = Math.Clamp(frequency, 20f, 0.45f * sampleRate);
        bandwidth = MathF.Max(bandwidth, 10f);
        var g = MathF.Tan(MathF.PI * frequency / sampleRate);
        _k = bandwidth / frequency;
        _a1 = 1f / (1f + g * (g + _k));
        _a2 = g * _a1;
        _a3 = g * _a2;
    }

    /// <summary>Filters one sample into <see cref="Low"/>, <see cref="Band"/> and <see cref="High"/>.</summary>
    public void Process(float x)
    {
        var v3 = x - _ic2;
        var v1 = _a1 * _ic1 + _a2 * v3;
        var v2 = _ic2 + _a2 * _ic1 + _a3 * v3;
        _ic1 = 2f * v1 - _ic1;
        _ic2 = 2f * v2 - _ic2;
        Band = _k * v1;
        Low = v2;
        High = x - Band - v2;
    }
}

/// <summary>Xorshift white noise, seeded so a line renders the same for every listener.</summary>
internal struct NoiseSource
{
    private uint _state;

    /// <summary>Starts the sequence from a seed.</summary>
    public NoiseSource(int seed)
    {
        _state = (uint) seed | 1u;
    }

    /// <summary>A uniform value from -1 to 1.</summary>
    public float Next()
    {
        _state ^= _state << 13;
        _state ^= _state >> 17;
        _state ^= _state << 5;
        return _state / (float) uint.MaxValue * 2f - 1f;
    }

    /// <summary>Near-Gaussian noise with unit variance and no offset, the sum of four uniform values.</summary>
    public float Gaussian()
    {
        return 0.866f * (Next() + Next() + Next() + Next());
    }
}

/// <summary>One period of the glottal pulse, shaped by Rd from pressed and bright to soft and breathy.</summary>
internal struct Glottis
{
    private float _te;
    private float _epsilon;
    private float _shift;
    private float _delta;
    private float _omega;
    private float _alpha;
    private float _e0;
    private float _norm;

    /// <summary>Phase at which the folds close, 0 to 1. The open phase runs from 0 to here.</summary>
    public float Closing => _te;

    /// <summary>Builds the period's waveform for this Rd.</summary>
    public void Setup(float rd)
    {
        rd = Math.Clamp(rd, 0.5f, 2.7f);
        var ra = -0.01f + 0.048f * rd;
        var rk = 0.224f + 0.118f * rd;
        var rg = rk / 4f * (0.5f + 1.2f * rk) / (0.11f * rd - ra * (0.5f + 1.2f * rk));
        var tp = 1f / (2f * rg);
        var te = tp + tp * rk;

        _epsilon = 1f / ra;
        _shift = MathF.Exp(-_epsilon * (1f - te));
        _delta = 1f - _shift;
        var rhs = ((1f / _epsilon) * (_shift - 1f) + (1f - te) * _shift) / _delta;
        var lower = -(te - tp) / 2f + rhs;
        var upper = -lower;
        _omega = MathF.PI / tp;
        var s = MathF.Sin(_omega * te);
        var y = -MathF.PI * s * upper / (tp * 2f);
        _alpha = MathF.Log(y) / (tp / 2f - te);
        _e0 = -1f / (s * MathF.Exp(_alpha * te));
        _te = te;

        _norm = 1f;
        var power = 0f;
        const int points = 64;
        for (var i = 0; i < points; i++)
        {
            var v = Sample((i + 0.5f) / points);
            power += v * v;
        }

        _norm = power > 0f ? 1f / MathF.Sqrt(power / points) : 1f;
    }

    /// <summary>The flow derivative at this phase of the period, 0 to 1.</summary>
    public float Sample(float phase)
    {
        var v = phase > _te
            ? (-MathF.Exp(-_epsilon * (phase - _te)) + _shift) / _delta
            : _e0 * MathF.Exp(_alpha * phase) * MathF.Sin(_omega * phase);
        return v * _norm;
    }
}
