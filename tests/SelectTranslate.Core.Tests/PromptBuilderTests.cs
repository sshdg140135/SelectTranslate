using SelectTranslate.Core.Translation;

namespace SelectTranslate.Core.Tests;

public sealed class PromptBuilderTests
{
    [Fact]
    public void BuildSystemPrompt_InjectsTargetLanguageDisplayName()
    {
        var prompt = PromptBuilder.BuildSystemPrompt("zh-CN", customPrompt: null);

        Assert.Contains("简体中文", prompt);
        Assert.DoesNotContain("{0}", prompt);
        Assert.Contains("只输出译文", prompt);
        Assert.Contains("若原文已是目标语言，原样返回", prompt);
        Assert.Contains("标识符保留原词", prompt);
        Assert.Contains("不得原样返回原文", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_UsesJapaneseNameForJa()
    {
        Assert.Contains("日语", PromptBuilder.BuildSystemPrompt("ja", null));
    }

    [Fact]
    public void BuildSystemPrompt_CustomPromptWins()
    {
        var prompt = PromptBuilder.BuildSystemPrompt("zh-CN", "  my custom prompt  ");

        Assert.Equal("my custom prompt", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_UnknownTarget_FallsBackToGenericName()
    {
        var prompt = PromptBuilder.BuildSystemPrompt(null, null);

        Assert.Contains("目标语言", prompt);
        Assert.DoesNotContain("{0}", prompt);
    }
}