using SelectTranslate.Core.Languages;

namespace SelectTranslate.Core.Translation;

/// <summary>
/// 组装系统提示词：目标语言直接写进提示词，用户消息只留原文，模型更不容易跑偏。
/// 配置里手动填了 systemPrompt 时以配置为准（视为高级用户自定义）。
/// </summary>
public static class PromptBuilder
{
    public const string DefaultTemplate =
        "你是一名专业翻译引擎。把用户给出的文本翻译成{0}。\n"
        + "规则：\n"
        + "1. 只输出译文，不要解释、不要加“译文：”之类的前缀或引号；\n"
        + "2. 保留原文的换行、段落、列表与标点结构；\n"
        + "3. 正文里的代码片段、URL、邮箱、数字保持原样；\n"
        + "4. 变量名、函数名、类名等标识符保留原词，并在其后用括号补中文含义，例如 buildBody（构建请求体）；\n"
        + "5. 若原文已是目标语言，原样返回；否则必须给出{0}译文，不得原样返回原文。";

    /// <summary>检测到模型原样返回时，追加这句再试一次。</summary>
    public const string AntiEchoInstruction =
        "注意：即使原文是代码、标识符或专有名词，也必须给出{目标语言}译文（标识符可保留原词并用括号补中文含义），不要原样返回原文。";

    public static string BuildSystemPrompt(string? targetLanguage, string? customPrompt)
    {
        if (!string.IsNullOrWhiteSpace(customPrompt))
        {
            return customPrompt.Trim();
        }

        var displayName = LanguageDetector.DisplayName(targetLanguage);
        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = "目标语言";
        }

        return string.Format(DefaultTemplate, displayName);
    }
}