namespace SelectTranslate.Core.Translation;

/// <summary>按 providerType 解析服务商实现并缓存（design §5.2）。</summary>
public sealed class ProviderRegistry
{
    private readonly Dictionary<string, ITranslationProvider> _providers = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<ITranslationProvider> All => _providers.Values;

    public void Register(ITranslationProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _providers[provider.Id] = provider;
    }

    public bool TryGet(string? id, out ITranslationProvider provider)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            provider = null!;
            return false;
        }

        return _providers.TryGetValue(id.Trim(), out provider!);
    }

    public ITranslationProvider Resolve(string? id)
    {
        if (TryGet(id, out var provider))
        {
            return provider;
        }

        throw new TranslationException(
            TranslationErrorKind.ConfigMissing,
            $"未知的服务商类型：{id ?? "(空)"}，请到设置中重新选择。");
    }
}