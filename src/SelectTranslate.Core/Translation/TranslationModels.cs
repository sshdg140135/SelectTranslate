namespace SelectTranslate.Core.Translation;

/// <summary>一次翻译请求。原文已清洗，长度已按上限截断。</summary>
public sealed record TranslationRequest
{
    public required string Text { get; init; }

    public required string TargetLanguage { get; init; }

    public string SourceLanguage { get; init; } = "auto";

    public double Temperature { get; init; } = 0.2;

    public string? SystemPrompt { get; init; }
}

/// <summary>增量译文；非流式服务商一次性回传 IsFinal = true 的整段。</summary>
public readonly record struct TranslationChunk(string Text, bool IsFinal);

/// <summary>统一错误分类，UI 负责映射成中文文案（design §5.1）。</summary>
public enum TranslationErrorKind
{
    Auth,
    Model,
    RateLimit,
    Network,
    Timeout,
    BadResponse,
    Internal,
    ConfigMissing,
}

public sealed class TranslationException(
    TranslationErrorKind kind,
    string message,
    int? retryAfterSeconds = null,
    Exception? inner = null) : Exception(message, inner)
{
    public TranslationErrorKind Kind { get; } = kind;

    public int? RetryAfterSeconds { get; } = retryAfterSeconds;
}

/// <summary>一次翻译的最终结果。</summary>
public sealed record TranslationOutcome
{
    public required string Text { get; init; }

    /// <summary>检测到的主导语种（zh / en / ja ...）。</summary>
    public required string DetectedLanguage { get; init; }

    /// <summary>原文是否因超过上限被截断（REQ-04.5）。</summary>
    public bool Truncated { get; init; }

    /// <summary>原文已经是目标语言，未发起请求（REQ-03.2）。</summary>
    public bool SameAsTarget { get; init; }

    /// <summary>译文与原文一致（多为被判定为代码/标识符），UI 需要给出提示。</summary>
    public bool Echoed { get; init; }

    public TimeSpan Elapsed { get; init; }

    public string? Model { get; init; }
}