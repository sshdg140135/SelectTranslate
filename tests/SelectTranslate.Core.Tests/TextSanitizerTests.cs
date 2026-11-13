using SelectTranslate.Core.Text;

namespace SelectTranslate.Core.Tests;

public sealed class TextSanitizerTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \r\n\t  ")]
    public void Normalize_BlankInput_ReturnsNull(string? input)
    {
        Assert.Null(TextSanitizer.Normalize(input));
    }

    [Fact]
    public void Normalize_TrimsOuterWhitespace()
    {
        Assert.Equal("hello world", TextSanitizer.Normalize("  hello world  \r\n"));
    }

    [Fact]
    public void Normalize_RemovesInvisibleCharacters()
    {
        Assert.Equal("abc", TextSanitizer.Normalize("a\u200Bb\uFEFFc"));
    }

    [Fact]
    public void Normalize_KeepsParagraphBreaks_ButCollapsesExtraBlankLines()
    {
        var input = "第一段\r\n\r\n\r\n\r\n第二段\n\n\n第三段";

        Assert.Equal("第一段\n\n第二段\n\n第三段", TextSanitizer.Normalize(input));
    }

    [Fact]
    public void Normalize_PreservesSingleLineBreaks()
    {
        Assert.Equal("line1\nline2", TextSanitizer.Normalize("line1\nline2"));
    }

    [Fact]
    public void Normalize_DropsControlCharacters()
    {
        Assert.Equal("ab", TextSanitizer.Normalize("a\u0001b"));
    }
}