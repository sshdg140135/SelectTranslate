namespace SelectTranslate.Core.Translation;

/// <summary>从当前配置投影出的服务商参数，每次请求前重新取，保证改配置后无需重启（REQ-07.1）。</summary>
public sealed record ApiSettings(
    string BaseUrl,
    string? ApiKey,
    string Model,
    double Temperature,
    int TimeoutMs,
    int ConnectTimeoutMs,
    string SystemPrompt,
    IReadOnlyDictionary<string, string> ExtraHeaders,
    bool Stream,
    int? MaxTokens);