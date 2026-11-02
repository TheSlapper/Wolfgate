using Content.Shared._WF.Voice;

namespace Content.Client._WF.Voice;

public sealed partial class SpeechVoiceSynth
{
    /// <summary>Reshapes the blips for a speaking habit. Seeded from the text, so every listener hears the same.</summary>
    private void ApplyQuirk(List<Blip> blips, SpeechVoiceQuirk quirk)
    {
        switch (quirk)
        {
            case SpeechVoiceQuirk.TrailingMumble:
                TrailingMumble(blips);
                break;
            case SpeechVoiceQuirk.Giggle:
                Giggle(blips);
                break;
            case SpeechVoiceQuirk.Sigh:
                Sigh(blips);
                break;
        }
    }

    /// <summary>The back half of each statement fades quieter, lower and slower.</summary>
    private static void TrailingMumble(List<Blip> blips)
    {
        for (var i = 0; i < blips.Count; i++)
        {
            var blip = blips[i];
            if (blip.Gap || blip.Ending != '.' || blip.PhrasePosition < 0.6f)
                continue;

            var t = (blip.PhrasePosition - 0.6f) / 0.4f;
            blip.Gain *= 1f - 0.85f * t;
            blip.Lift *= 1f - 0.2f * t;
            blip.Length *= 1f + 0.6f * t;
            blips[i] = blip;
        }
    }

    /// <summary>Three or four high "hee" sounds after the line, and after about half its other sentences.</summary>
    private void Giggle(List<Blip> blips)
    {
        var word = 10000;
        var lineEnd = true;
        for (var i = blips.Count - 1; i >= 0; i--)
        {
            var blip = blips[i];
            if (blip.Gap || !blip.Last)
                continue;

            var always = lineEnd;
            lineEnd = false;
            if (blip.Ending is not ('.' or '!') || !always && _noise.Next() < 0f)
                continue;

            var count = _noise.Next() < 0f ? 3 : 4;
            if (blips.Count + 2 * count > MaxBlips)
                continue;

            var giggle = new List<Blip> { new() { Gap = true, Length = 0.4f } };
            for (var k = 0; k < count; k++)
            {
                if (k > 0)
                    giggle.Add(new Blip { Gap = true, Length = 0.15f });

                var hee = Syllable(H, Ee);
                hee.Lift = k % 2 == 0 ? 1.6f : 1.45f;
                hee.Gain = 0.9f;
                hee.Length = 0.6f;
                hee.Word = word++;
                giggle.Add(hee);
            }

            blips.InsertRange(i + 1, giggle);
        }
    }

    /// <summary>A long, low, breathy "haah" at the start of every line.</summary>
    private static void Sigh(List<Blip> blips)
    {
        if (blips.Count + 2 > MaxBlips)
            return;

        var sigh = Syllable(H, Ah);
        sigh.OnsetShare = 0.5f;
        sigh.Length = 3f;
        sigh.Gain = 0.8f;
        sigh.Lift = 0.8f;
        sigh.Word = 20000;
        blips.InsertRange(0, new List<Blip> { sigh, new() { Gap = true, Length = 1f } });
    }
}
