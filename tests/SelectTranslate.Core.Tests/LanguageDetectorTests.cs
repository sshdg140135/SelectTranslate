using SelectTranslate.Core.Languages;

namespace SelectTranslate.Core.Tests;

public sealed class LanguageDetectorTests
{
    [Theory]
    [InlineData("今天天气不错，我们去公园散步吧。", "zh")]
    [InlineData("Machine translation is getting better every year.", "en")]
    [InlineData("これは日本語の文章です。", "ja")]
    [InlineData("오늘 날씨가 정말 좋네요.", "ko")]
    [InlineData("Сегодня отличная погода.", "ru")]
    [InlineData("الطقس جميل اليوم.", "ar")]
    public void Detect_IdentifiesDominantLanguage(string text, string expected)
    {
        Assert.Equal(expected, LanguageDetector.Detect(text).Code);
    }

    [Fact]
    public void Detect_ReturnsUnknown_ForDigitsAndPunctuationOnly()
    {
        Assert.Equal("unknown", LanguageDetector.Detect("12345 !!! ---").Code);
    }

    [Fact]
    public void Detect_ReturnsUnknown_ForBlankInput()
    {
        Assert.Equal("unknown", LanguageDetector.Detect("   ").Code);
    }

    [Fact]
    public void Detect_ChineseWithFewLatinLetters_StaysChinese()
    {
        Assert.Equal("zh", LanguageDetector.Detect("使用 .NET 开发桌面应用").Code);
    }

    [Theory]
    [InlineData("这是中文", "zh-CN", true)]
    [InlineData("这是中文", "zh-TW", true)]
    [InlineData("这是中文", "zh", true)]
    [InlineData("这是中文", "en", false)]
    [InlineData("this is english", "en-US", true)]
    [InlineData("this is english", "zh-CN", false)]
    public void MatchesTarget_ComparesPrimarySubtag(string text, string target, bool expected)
    {
        Assert.Equal(expected, LanguageDetector.MatchesTarget(text, target));
    }

    [Fact]
    public void MatchesTarget_FalseWhenTargetMissing()
    {
        Assert.False(LanguageDetector.MatchesTarget("hello", ""));
        Assert.False(LanguageDetector.MatchesTarget("hello", null));
    }

    [Theory]
    [InlineData("zh-CN", "简体中文")]
    [InlineData("ja", "日语")]
    [InlineData("en-US", "英语")]
    public void DisplayName_ReturnsChineseName(string target, string expected)
    {
        Assert.Equal(expected, LanguageDetector.DisplayName(target));
    }
}