using System.Text.Json.Serialization;

namespace SelectTranslate.Core.Configuration;

/// <summary>
/// 应用配置。字段与 docs/specs/001-mvp/design.md §7.2 对应。
/// v2 起支持多个服务商档案（Providers）+ 当前选中项（ActiveProviderId）。
/// </summary>
public sealed class AppConfig
{
    /// <summary>旧版内置提示词，仅用于在加载配置时识别并迁移成"空值 = 使用内置模板"。</summary>
    private const string LegacyDefaultSystemPrompt =
        "你是一名专业翻译。将用户提供的文本翻译成目标语言。要求：只输出译文，保留换行与专有名词，不要解释。";

    public int SchemaVersion { get; set; } = 2;

    /// <summary>服务商档案列表，至少一个。</summary>
    public List<ApiConfig> Providers { get; set; } = [];

    /// <summary>当前使用的服务商 Id。</summary>
    public string ActiveProviderId { get; set; } = string.Empty;

    /// <summary>v1 配置里的单个 api 节点；迁移后置空，不再写出。</summary>
    [JsonPropertyName("api")]
    public ApiConfig? LegacyApi { get; set; }

    /// <summary>当前生效的服务商档案（指向 <see cref="Providers"/> 中的对象）。</summary>
    [JsonIgnore]
    public ApiConfig ActiveProvider { get; set; } = new();

    /// <summary>兼容旧调用：等价于 <see cref="ActiveProvider"/>。</summary>
    [JsonIgnore]
    public ApiConfig Api => ActiveProvider;

    public TargetConfig Target { get; set; } = new();

    public HotkeyConfig Hotkey { get; set; } = new();

    public BehaviorConfig Behavior { get; set; } = new();

    public UiConfig Ui { get; set; } = new();

    public GeneralConfig General { get; set; } = new();

    /// <summary>把越界或缺失的值收敛到合法范围，同时完成 v1 → v2 迁移。</summary>
    public void Normalize()
    {
        if (SchemaVersion < 2)
        {
            SchemaVersion = 2;
        }

        Target ??= new TargetConfig();
        Hotkey ??= new HotkeyConfig();
        Behavior ??= new BehaviorConfig();
        Ui ??= new UiConfig();
        General ??= new GeneralConfig();

        NormalizeProviders();

        Target.Lang = string.IsNullOrWhiteSpace(Target.Lang) ? "zh-CN" : Target.Lang.Trim();

        Hotkey.Mouse = string.IsNullOrWhiteSpace(Hotkey.Mouse) ? "Middle" : Hotkey.Mouse.Trim();
        Hotkey.BypassModifier = string.IsNullOrWhiteSpace(Hotkey.BypassModifier) ? "Ctrl" : Hotkey.BypassModifier.Trim();

        Behavior.MaxChars = Math.Clamp(Behavior.MaxChars, 100, 100_000);
        Behavior.Blacklist = (Behavior.Blacklist ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Ui.Width = Math.Clamp(Ui.Width, 300, 560);
        Ui.FontSize = Math.Clamp(Ui.FontSize, 12, 20);
        Ui.AutoCloseSec = Math.Max(0, Ui.AutoCloseSec);
        Ui.Theme = Ui.Theme is "light" or "dark" or "system" ? Ui.Theme : "system";

        General.LogLevel = General.LogLevel is "debug" or "info" or "warn" or "error" ? General.LogLevel : "info";
    }

    /// <summary>切换当前服务商；Id 不存在时保持原选择。</summary>
    public bool SetActiveProvider(string? id)
    {
        NormalizeProviders();

        if (string.IsNullOrWhiteSpace(id)
            || Providers.All(p => !string.Equals(p.Id, id, StringComparison.Ordinal)))
        {
            return false;
        }

        ActiveProviderId = id;
        ActiveProvider = Providers.First(p => p.Id == id);
        return true;
    }

    private void NormalizeProviders()
    {
        Providers ??= [];

        // v1 → v2：把旧的单服务商节点搬进列表。
        if (Providers.Count == 0 && LegacyApi is not null)
        {
            Providers.Add(LegacyApi);
        }

        LegacyApi = null;

        Providers.RemoveAll(p => p is null);
        if (Providers.Count == 0)
        {
            Providers.Add(new ApiConfig { Name = "默认服务商" });
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < Providers.Count; index++)
        {
            var provider = Providers[index];

            provider.Id = string.IsNullOrWhiteSpace(provider.Id)
                ? Guid.NewGuid().ToString("N")
                : provider.Id.Trim();

            // 重复 Id 直接换新，避免切换时指向错档案。
            if (!seen.Add(provider.Id))
            {
                provider.Id = Guid.NewGuid().ToString("N");
                seen.Add(provider.Id);
            }

            provider.Name = string.IsNullOrWhiteSpace(provider.Name)
                ? $"服务商 {index + 1}"
                : provider.Name.Trim();

            provider.ProviderType = string.IsNullOrWhiteSpace(provider.ProviderType)
                ? "openai-compatible"
                : provider.ProviderType.Trim();

            provider.BaseUrl = string.IsNullOrWhiteSpace(provider.BaseUrl)
                ? "https://api.deepseek.com/v1"
                : provider.BaseUrl.Trim();

            provider.Model = string.IsNullOrWhiteSpace(provider.Model) ? "deepseek-chat" : provider.Model.Trim();
            provider.Temperature = Math.Clamp(provider.Temperature, 0d, 1d);
            provider.TimeoutMs = Math.Clamp(provider.TimeoutMs, 10_000, 120_000);
            provider.ConnectTimeoutMs = Math.Clamp(provider.ConnectTimeoutMs, 3_000, 30_000);

            var prompt = provider.SystemPrompt?.Trim();
            provider.SystemPrompt = string.IsNullOrEmpty(prompt) || prompt == LegacyDefaultSystemPrompt
                ? string.Empty
                : prompt;

            provider.ExtraHeaders ??= new Dictionary<string, string>();
            provider.ExtraBodyJson = provider.ExtraBodyJson?.Trim() ?? string.Empty;
            provider.ThinkingMode = provider.ThinkingMode is "on" or "off" ? provider.ThinkingMode : "default";
        }

        if (string.IsNullOrWhiteSpace(ActiveProviderId)
            || Providers.All(p => !string.Equals(p.Id, ActiveProviderId, StringComparison.Ordinal)))
        {
            ActiveProviderId = Providers[0].Id;
        }

        ActiveProvider = Providers.First(p => p.Id == ActiveProviderId);
    }
}

/// <summary>一个服务商档案（v2 前是全局唯一的 api 配置）。</summary>
public sealed class ApiConfig
{
    /// <summary>档案唯一标识，由程序生成。</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>用户可见名称，例如"DeepSeek 官方"。</summary>
    public string Name { get; set; } = string.Empty;

    public string ProviderType { get; set; } = "openai-compatible";

    public string BaseUrl { get; set; } = "https://api.deepseek.com/v1";

    /// <summary>DPAPI 密文（Base64）；明文只在内存中出现。</summary>
    public string? ApiKeyProtected { get; set; }

    public string Model { get; set; } = "deepseek-chat";

    public double Temperature { get; set; } = 0.2;

    public int TimeoutMs { get; set; } = 30_000;

    public int ConnectTimeoutMs { get; set; } = 10_000;

    public bool Stream { get; set; } = true;

    public int? MaxTokens { get; set; }

    /// <summary>空值表示使用内置提示词模板（见 PromptBuilder）。</summary>
    public string SystemPrompt { get; set; } = string.Empty;

    public Dictionary<string, string> ExtraHeaders { get; set; } = new();

    /// <summary>附加请求体参数（JSON 对象文本），合并进 /chat/completions 请求体顶层；优先于思考开关。</summary>
    public string ExtraBodyJson { get; set; } = string.Empty;

    /// <summary>思考开关：default（不指定）/ on / off；具体写什么字段由模型名前缀推断。</summary>
    public string ThinkingMode { get; set; } = "default";
}

public sealed class TargetConfig
{
    public string Lang { get; set; } = "zh-CN";
}

public sealed class HotkeyConfig
{
    public string Mouse { get; set; } = "Middle";

    public string Keyboard { get; set; } = string.Empty;

    public string BypassModifier { get; set; } = "Ctrl";

    public bool Enabled { get; set; } = true;
}

public sealed class BehaviorConfig
{
    public bool InterceptMiddle { get; set; } = true;

    public int MaxChars { get; set; } = 8_000;

    public bool RestoreClipboard { get; set; } = true;

    public bool AutoStart { get; set; } = true;

    public string[] Blacklist { get; set; } = [];

    public bool EscapeCloses { get; set; } = true;

    public bool CloseOnOutsideClick { get; set; } = true;
}

public sealed class UiConfig
{
    public int Width { get; set; } = 320;

    public int FontSize { get; set; } = 14;

    public string Theme { get; set; } = "system";

    public int AutoCloseSec { get; set; }

    public bool Animate { get; set; } = true;

    public bool ShowModelName { get; set; } = true;
}

public sealed class GeneralConfig
{
    public string LogLevel { get; set; } = "info";

    public bool Portable { get; set; }
}