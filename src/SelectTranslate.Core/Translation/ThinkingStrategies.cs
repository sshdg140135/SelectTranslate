using System.Text.Json;

namespace SelectTranslate.Core.Translation;

public enum ThinkingParameterKind
{
    None,
    ThinkingType,
    EnableThinking,
    ReasoningEffort,
}

/// <summary>按模型名前缀推断出的"思考开关"写法。</summary>
public sealed record ThinkingPlan(string Id, string DisplayName, ThinkingParameterKind Kind, string? Note = null);

/// <summary>
/// 各服务商关闭思考的字段并不统一：按模型名前缀推断，未知模型不自动写入任何字段。
/// 生成的补丁会先于"附加请求参数"合并，用户手填的内容永远覆盖这里。
/// </summary>
public static class ThinkingStrategies
{
    public static ThinkingPlan Detect(string? modelName)
    {
        var model = (modelName ?? string.Empty).Trim().ToLowerInvariant();

        if (model.Length == 0)
        {
            return Unknown("尚未填写模型名");
        }

        if (model.StartsWith("doubao", StringComparison.Ordinal) || model.StartsWith("ep-", StringComparison.Ordinal))
        {
            return new ThinkingPlan("doubao", "豆包 / 火山方舟", ThinkingParameterKind.ThinkingType);
        }

        if (model.StartsWith("deepseek", StringComparison.Ordinal))
        {
            return new ThinkingPlan("deepseek", "DeepSeek", ThinkingParameterKind.ThinkingType);
        }

        if (model.StartsWith("glm", StringComparison.Ordinal))
        {
            return new ThinkingPlan("glm", "智谱 GLM", ThinkingParameterKind.ThinkingType);
        }

        if (model.StartsWith("kimi", StringComparison.Ordinal) || model.StartsWith("moonshot", StringComparison.Ordinal))
        {
            return new ThinkingPlan("kimi", "Kimi / Moonshot", ThinkingParameterKind.ThinkingType);
        }

        if (model.StartsWith("qwen", StringComparison.Ordinal))
        {
            return new ThinkingPlan("qwen", "通义千问", ThinkingParameterKind.EnableThinking);
        }

        if (model.StartsWith("gpt", StringComparison.Ordinal) || IsOpenAiReasoning(model))
        {
            return new ThinkingPlan(
                "openai",
                "OpenAI",
                ThinkingParameterKind.ReasoningEffort,
                "该系列只能把推理强度降到 minimal，无法完全关闭思考。");
        }

        return Unknown("未识别该模型，不会自动写入思考参数");
    }

    /// <summary>生成要合并进请求体的字段；未知模型返回 null（不写入）。</summary>
    public static Dictionary<string, object?>? BuildPatch(ThinkingPlan plan, bool enabled) => plan.Kind switch
    {
        ThinkingParameterKind.ThinkingType => new Dictionary<string, object?>
        {
            ["thinking"] = new Dictionary<string, object?>
            {
                ["type"] = enabled ? "enabled" : "disabled",
            },
        },
        ThinkingParameterKind.EnableThinking => new Dictionary<string, object?>
        {
            ["enable_thinking"] = enabled,
        },
        ThinkingParameterKind.ReasoningEffort => new Dictionary<string, object?>
        {
            ["reasoning_effort"] = enabled ? "high" : "minimal",
        },
        _ => null,
    };

    /// <summary>人类可读的请求片段，用于设置页展示"将写入什么"。</summary>
    public static string? DescribePatch(ThinkingPlan plan, bool enabled)
    {
        var patch = BuildPatch(plan, enabled);
        return patch is null ? null : JsonSerializer.Serialize(patch);
    }

    private static ThinkingPlan Unknown(string note)
        => new("unknown", "未识别", ThinkingParameterKind.None, note);

    private static bool IsOpenAiReasoning(string model)
        => model.StartsWith("o1", StringComparison.Ordinal)
            || model.StartsWith("o3", StringComparison.Ordinal)
            || model.StartsWith("o4", StringComparison.Ordinal);
}