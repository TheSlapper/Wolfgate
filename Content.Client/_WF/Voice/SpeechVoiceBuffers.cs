namespace Content.Client._WF.Voice;

/// <summary>Buffers kept between renders, so each line doesn't allocate on the large object heap.</summary>
public sealed class SpeechVoiceBuffers
{
    /// <summary>The line as it is built.</summary>
    public readonly float[] Samples = new float[SpeechVoiceSynth.MaxSamples];

    /// <summary>The finished line.</summary>
    public readonly short[] Pcm = new short[SpeechVoiceSynth.MaxSamples];
}
