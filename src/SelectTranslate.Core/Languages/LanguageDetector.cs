namespace SelectTranslate.Core.Languages;

public enum DominantScript
{
    Unknown,
    Chinese,
    Japanese,
    Korean,
    Cyrillic,
    Arabic,
    Latin,
}

/// <summary>语种猜测结果。Code 使用简化的 BCP-47 主语言子标签（zh / en / ja ...）。</summary>
public sealed record LanguageGuess(string Code, string DisplayName, DominantScript Script, double Confidence)
{
    public static readonly LanguageGuess Unknown = new("unknown", "未知", DominantScript.Unknown, 0);
}

/// <summary>
/// 依据字符构成判断主导语种（REQ-03.1）。这里只做"够用"的启发式：
/// 假名、谚文、汉字、西里尔、阿拉伯、拉丁各自计数，取主导者。
/// </summary>
public static class LanguageDetector
{
    public static LanguageGuess Detect(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return LanguageGuess.Unknown;
        }

        var cjk = 0;
        var hiragana = 0;
        var katakana = 0;
        var hangul = 0;
        var cyrillic = 0;
        var arabic = 0;
        var latin = 0;

        foreach (var ch in text)
        {
            if (ch is >= '\u4E00' and <= '\u9FFF')
            {
                cjk++;
            }
            else if (ch is >= '\u3040' and <= '\u309F')
            {
                hiragana++;
            }
            else if (ch is >= '\u30A0' and <= '\u30FF')
            {
                katakana++;
            }
            else if (ch is >= '\uAC00' and <= '\uD7AF')
            {
                hangul++;
            }
            else if (ch is >= '\u0400' and <= '\u04FF')
            {
                cyrillic++;
            }
            else if (ch is >= '\u0600' and <= '\u06FF')
            {
                arabic++;
            }
            else if (ch is >= 'A' and <= 'Z' or >= 'a' and <= 'z'
                     or >= '\u00C0' and <= '\u024F')
            {
                latin++;
            }
        }

        var letters = cjk + hiragana + katakana + hangul + cyrillic + arabic + latin;
        if (letters == 0)
        {
            return LanguageGuess.Unknown;
        }

        var kana = hiragana + katakana;

        // 日文：出现假名即可判定（汉字与假名混排），纯汉字仍算中文。
        if (kana > 0 && kana >= letters * 0.05)
        {
            return new LanguageGuess("ja", "日语", DominantScript.Japanese, kana / (double)letters);
        }

        if (hangul >= letters * 0.2)
        {
            return new LanguageGuess("ko", "韩语", DominantScript.Korean, hangul / (double)letters);
        }

        if (cjk >= letters * 0.2)
        {
            return new LanguageGuess("zh", "中文", DominantScript.Chinese, cjk / (double)letters);
        }

        if (cyrillic >= letters * 0.5)
        {
            return new LanguageGuess("ru", "俄语", DominantScript.Cyrillic, cyrillic / (double)letters);
        }

        if (arabic >= letters * 0.5)
        {
            return new LanguageGuess("ar", "阿拉伯语", DominantScript.Arabic, arabic / (double)letters);
        }

        if (latin >= letters * 0.5)
        {
            return new LanguageGuess("en", "英语", DominantScript.Latin, latin / (double)letters);
        }

        return LanguageGuess.Unknown;
    }

    /// <summary>判断文本的主导语种是否就是目标语言（用于 REQ-03.2 的短路）。</summary>
    public static bool MatchesTarget(string? text, string? targetLanguage)
    {
        var target = NormalizeTarget(targetLanguage);
        if (target.Length == 0)
        {
            return false;
        }

        var guess = Detect(text);
        return guess.Code switch
        {
            "zh" => target is "zh" or "zh-cn" or "zh-tw" or "zh-hk",
            "en" => target is "en",
            "ja" => target is "ja",
            "ko" => target is "ko",
            "ru" => target is "ru",
            "ar" => target is "ar",
            _ => false,
        };
    }

    public static string DisplayName(string? targetLanguage) => NormalizeTarget(targetLanguage) switch
    {
        "zh" or "zh-cn" => "简体中文",
        "zh-tw" or "zh-hk" => "繁体中文",
        "en" => "英语",
        "ja" => "日语",
        "ko" => "韩语",
        "ru" => "俄语",
        "ar" => "阿拉伯语",
        _ => targetLanguage ?? string.Empty,
    };

    private static string NormalizeTarget(string? target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return string.Empty;
        }

        var trimmed = target.Trim().ToLowerInvariant().Replace('_', '-');
        var dash = trimmed.IndexOf('-');
        return dash < 0 ? trimmed : trimmed[..dash];
    }
}