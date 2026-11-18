using SelectTranslate.Core.Translation;

namespace SelectTranslate.Core.Tests;

public sealed class ThinkingStrategiesTests
{
    [Theory]
    [InlineData("doubao-seed-2-1-lite-260915", "doubao", ThinkingParameterKind.ThinkingType)]
    [InlineData("ep-20250101-abcde", "doubao", ThinkingParameterKind.ThinkingType)]
    [InlineData("deepseek-chat", "deepseek", ThinkingParameterKind.ThinkingType)]
    [InlineData("deepseek-reasoner", "deepseek", ThinkingParameterKind.ThinkingType)]
    [InlineData("glm-4.6", "glm", ThinkingParameterKind.ThinkingType)]
    [InlineData("kimi-k2-0905", "kimi", ThinkingParameterKind.ThinkingType)]
    [InlineData("moonshot-v1-8k", "kimi", ThinkingParameterKind.ThinkingType)]
    [InlineData("qwen3-max", "qwen", ThinkingParameterKind.EnableThinking)]
    [InlineData("gpt-5", "openai", ThinkingParameterKind.ReasoningEffort)]
    [InlineData("o3-mini", "openai", ThinkingParameterKind.ReasoningEffort)]
    [InlineData("o4-mini", "openai", ThinkingParameterKind.ReasoningEffort)]
    public void Detect_ByModelPrefix(string model, string id, ThinkingParameterKind kind)
    {
        var plan = ThinkingStrategies.Detect(model);

        Assert.Equal(id, plan.Id);
        Assert.Equal(kind, plan.Kind);
    }

    [Theory]
    [InlineData("llama-3.1-70b")]
    [InlineData("my-gateway-model")]
    [InlineData("")]
    public void Detect_UnknownModel_DoesNotWriteAnything(string model)
    {
        var plan = ThinkingStrategies.Detect(model);

        Assert.Equal(ThinkingParameterKind.None, plan.Kind);
        Assert.Null(ThinkingStrategies.BuildPatch(plan, false));
        Assert.Null(ThinkingStrategies.DescribePatch(plan, false));
    }

    [Fact]
    public void BuildPatch_ThinkingType()
    {
        var plan = ThinkingStrategies.Detect("doubao-seed-2-1-lite");

        var off = ThinkingStrategies.DescribePatch(plan, false);
        var on = ThinkingStrategies.DescribePatch(plan, true);

        Assert.Equal("""{"thinking":{"type":"disabled"}}""", off);
        Assert.Equal("""{"thinking":{"type":"enabled"}}""", on);
    }

    [Fact]
    public void BuildPatch_EnableThinking()
    {
        var plan = ThinkingStrategies.Detect("qwen3-max");

        Assert.Equal("""{"enable_thinking":false}""", ThinkingStrategies.DescribePatch(plan, false));
        Assert.Equal("""{"enable_thinking":true}""", ThinkingStrategies.DescribePatch(plan, true));
    }

    [Fact]
    public void BuildPatch_ReasoningEffort()
    {
        var plan = ThinkingStrategies.Detect("gpt-5");

        Assert.Equal("""{"reasoning_effort":"minimal"}""", ThinkingStrategies.DescribePatch(plan, false));
        Assert.Equal("""{"reasoning_effort":"high"}""", ThinkingStrategies.DescribePatch(plan, true));
        Assert.NotNull(plan.Note);
    }

    [Fact]
    public void Detect_IsCaseInsensitive()
    {
        Assert.Equal("doubao", ThinkingStrategies.Detect("Doubao-Seed").Id);
        Assert.Equal("qwen", ThinkingStrategies.Detect("QWEN3").Id);
    }
}