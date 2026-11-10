namespace Content.Client._WF.Voice;

public sealed partial class SpeechVoiceSynth
{
    private const float WordGap = 1f;
    private const float CommaGap = 4f;
    private const float SentenceGap = 8f;

    /// <summary>Letter slots for a syllable's vowel. Its consonants are added on top at their own length.</summary>
    private const float SyllableLength = 1.4f;

    /// <summary>How much longer a question's last vowel is held, so its rise can be heard.</summary>
    private const float QuestionLength = 1.3f;

    /// <summary>Letter slots for a consonant said alone.</summary>
    private const float LoneConsonantLength = 0.8f;

    private static readonly string[] Ones =
    {
        "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve",
        "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen",
    };

    private static readonly string[] Tens =
    {
        "", "", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety",
    };

    /// <summary>The syllables for a message, with phrases split at punctuation and numbers said in full.</summary>
    private static List<Blip> Blips(string text)
    {
        var blips = new List<Blip>();
        var phraseStart = 0;
        var wordStart = -1;
        var lastWordStart = 0;
        var letters = 0;
        var upper = 0;
        var wordIndex = 0;

        void AddGap(float length)
        {
            if (blips.Count == 0)
                return;

            if (blips[^1].Gap)
            {
                var gap = blips[^1];
                gap.Length = MathF.Max(gap.Length, length);
                blips[^1] = gap;
                return;
            }

            blips.Add(new Blip { Gap = true, Length = length });
        }

        void Add(Blip blip)
        {
            if (wordStart < 0)
                wordStart = blips.Count;

            blip.Word = wordIndex;
            if (!blip.Lone)
                blip.Length *= SyllableLength;
            blips.Add(blip);
        }

        void EndWord()
        {
            if (wordStart < 0)
                return;

            if (letters >= 2 && upper == letters)
            {
                for (var i = wordStart; i < blips.Count; i++)
                {
                    var blip = blips[i];
                    blip.Shout = true;
                    blip.Gain = 1.4f;
                    blip.Lift = 1.15f;
                    blips[i] = blip;
                }
            }

            lastWordStart = wordStart;
            wordStart = -1;
            letters = upper = 0;
            wordIndex++;
            AddGap(WordGap);
        }

        void EndPhrase(char ending, float gap)
        {
            EndWord();

            var end = blips.Count > 0 && blips[^1].Gap ? blips.Count - 1 : blips.Count;
            if (end <= phraseStart)
            {
                AddGap(gap);
                return;
            }

            var voiced = 0;
            var lastVoiced = end - 1;
            for (var i = phraseStart; i < end; i++)
            {
                if (blips[i].Lone)
                    continue;

                voiced++;
                lastVoiced = i;
            }

            var place = 0;
            for (var i = phraseStart; i < end; i++)
            {
                var blip = blips[i];
                if (!blip.Lone)
                    place++;

                var index = Math.Max(0, place - 1);
                blip.PhrasePosition = voiced > 1 ? index / (float) (voiced - 1) : 0f;
                blip.Ending = ending;
                blip.Last = i == lastVoiced;
                if (blip.Last && ending == '?')
                    blip.Length *= QuestionLength;
                blips[i] = blip;
            }

            if (ending == '…')
            {
                var count = end - lastWordStart;
                for (var i = lastWordStart; i < end; i++)
                {
                    var blip = blips[i];
                    blip.Length *= 1.3f;
                    blip.Gain *= 1f - 0.6f * (i - lastWordStart + 1) / count;
                    blips[i] = blip;
                }
            }

            AddGap(gap);
            phraseStart = blips.Count;
        }

        var index = 0;
        while (index < text.Length && blips.Count < MaxBlips)
        {
            var c = text[index];

            if (char.IsAsciiLetter(c))
            {
                var end = index;
                while (end < text.Length && char.IsAsciiLetter(text[end]))
                {
                    end++;
                }

                foreach (var syllable in Syllables(text[index..end]))
                {
                    Add(syllable);
                }

                for (var i = index; i < end; i++)
                {
                    letters++;
                    if (char.IsAsciiLetterUpper(text[i]))
                        upper++;
                }

                index = end;
                continue;
            }

            if (char.IsAsciiDigit(c))
            {
                EndWord();
                var end = index;
                while (end < text.Length && char.IsAsciiDigit(text[end]))
                {
                    end++;
                }

                foreach (var word in NumberToWords(text[index..end]))
                {
                    foreach (var syllable in NumberWords[word])
                    {
                        Add(syllable);
                    }

                    EndWord();
                }

                index = end;
                continue;
            }

            switch (c)
            {
                case ',':
                case ';':
                case ':':
                    EndPhrase(',', CommaGap);
                    break;
                case '?':
                    EndPhrase('?', SentenceGap);
                    break;
                case '!':
                    EndPhrase('!', SentenceGap);
                    break;
                case '…':
                    EndPhrase('…', SentenceGap);
                    break;
                case '.':
                    if (index + 2 < text.Length && text[index + 1] == '.' && text[index + 2] == '.')
                    {
                        EndPhrase('…', SentenceGap);
                        index += 2;
                    }
                    else
                    {
                        EndPhrase('.', SentenceGap);
                    }

                    break;
                default:
                    EndWord();
                    break;
            }

            index++;
        }

        EndPhrase('.', 0f);

        if (blips.Count > MaxBlips)
            blips.RemoveRange(MaxBlips, blips.Count - MaxBlips);

        while (blips.Count > 0 && blips[^1].Gap)
        {
            blips.RemoveAt(blips.Count - 1);
        }

        return blips;
    }

    /// <summary>Splits a word into consonant-plus-vowel syllables ("he-lo"), with a silent final e dropped.</summary>
    private static List<Blip> Syllables(string word)
    {
        var lower = word.ToLowerInvariant();
        if (lower.Length > 2 && lower[^1] == 'e' && !IsVowelLetter(lower[^2]) && HasVowel(lower[..^2]))
            lower = lower[..^1];

        var syllables = new List<Blip>();
        var pending = new List<Consonant>();

        void Alone(Consonant consonant)
        {
            var blip = Syllable(consonant, Uh);
            blip.Lone = true;
            blip.Length = LoneConsonantLength;
            syllables.Add(blip);
        }

        var i = 0;
        while (i < lower.Length)
        {
            var c = lower[i];
            var pair = i + 1 < lower.Length ? lower.Substring(i, 2) : string.Empty;

            var vowel = IsVowelLetter(c) || c == 'y' && i > 0 && !IsVowelLetter(lower[i - 1]);
            if (vowel)
            {
                var isPair = VowelPairs.TryGetValue(pair, out var paired);
                var sound = isPair ? paired : Vowels[c];
                i += isPair ? 2 : 1;
                if (!isPair && c == 'o' && IsOpen(lower, i))
                    sound = (Oh, Uu);

                for (var k = 0; k < pending.Count - 1; k++)
                {
                    Alone(pending[k]);
                }

                var onset = pending.Count > 0 ? pending[^1] : default;
                syllables.Add(Syllable(onset, sound.Start, sound.End));
                pending.Clear();
                continue;
            }

            if (ConsonantPairs.TryGetValue(pair, out var consonantPair))
            {
                pending.Add(consonantPair);
                i += 2;
                continue;
            }

            if (pair == "gh")
            {
                i += 2;
                continue;
            }

            switch (c)
            {
                case 'c':
                    pending.Add(i + 1 < lower.Length && lower[i + 1] is 'e' or 'i' or 'y' ? S : K);
                    break;
                case 'x':
                    pending.Add(K);
                    pending.Add(S);
                    break;
                default:
                    pending.Add(Consonants[c]);
                    break;
            }

            i++;
            if (i < lower.Length && lower[i] == c)
                i++;
        }

        var first = 0;
        if (pending.Count > 0 && syllables.Count > 0 && !syllables[^1].Lone)
        {
            var last = syllables[^1];
            last.Coda = pending[0];
            syllables[^1] = last;
            first = 1;
        }

        for (var k = first; k < pending.Count; k++)
        {
            Alone(pending[k]);
        }

        return syllables;
    }

    /// <summary>Whether a vowel ending before index <paramref name="next"/> has no consonant after it.</summary>
    private static bool IsOpen(string word, int next)
    {
        return next >= word.Length
               || next + 1 < word.Length && !IsVowelLetter(word[next]) && IsVowelLetter(word[next + 1]);
    }

    private static bool IsVowelLetter(char c)
    {
        return c is 'a' or 'e' or 'i' or 'o' or 'u';
    }

    private static bool HasVowel(string text)
    {
        foreach (var c in text)
        {
            if (IsVowelLetter(c))
                return true;
        }

        return false;
    }

    /// <summary>A run of digits as English number words. Long runs and leading zeros are read digit by digit.</summary>
    private static List<string> NumberToWords(string digits)
    {
        var words = new List<string>();

        if (digits.Length > 6 || digits.Length > 1 && digits[0] == '0')
        {
            foreach (var digit in digits)
            {
                words.Add(Ones[digit - '0']);
            }

            return words;
        }

        var number = int.Parse(digits);
        if (number == 0)
        {
            words.Add(Ones[0]);
            return words;
        }

        if (number >= 1000)
        {
            BelowThousand(number / 1000, words);
            words.Add("thousand");
            number %= 1000;
        }

        if (number > 0)
            BelowThousand(number, words);

        return words;
    }

    /// <summary>Adds the words for a number under a thousand.</summary>
    private static void BelowThousand(int number, List<string> words)
    {
        if (number >= 100)
        {
            words.Add(Ones[number / 100]);
            words.Add("hundred");
            number %= 100;
        }

        if (number == 0)
            return;

        if (number < 20)
        {
            words.Add(Ones[number]);
            return;
        }

        words.Add(Tens[number / 10]);
        if (number % 10 > 0)
            words.Add(Ones[number % 10]);
    }
}
