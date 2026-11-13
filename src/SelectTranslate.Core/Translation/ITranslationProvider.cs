namespace SelectTranslate.Core.Translation;

/// <summary>服务商能力声明；设置页据此显示可选项（design §5.2）。</summary>
public sealed record ProviderCapabilities(bool SupportsStreaming, int MaxInputChars);

public interface ITranslationProvider
{
    /// <summary>配置里的 providerType。</summary>
    string Id { get; }

    string DisplayName { get; }

    ProviderCapabilities Capabilities { get; }

    /// <summary>
    /// 执行翻译。实现应在拿到增量文本时立即 Report；非流式实现只在结束时 Report 一次。
    /// 失败时抛 <see cref="TranslationException"/>。
    /// </summary>
    Task TranslateAsync(
        TranslationRequest request,
        IProgress<TranslationChunk>? onChunk,
        CancellationToken cancellationToken);
}