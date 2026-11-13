using System.Text.Json.Serialization;

namespace SelectTranslate.Core.Configuration;

/// <summary>
/// 应用配置。字段与 docs/specs/001-mvp/design.md §7.2 一一对应。
/// </summary>
public sealed class AppConfig
{
    /// <summary>旧版内置提示词，仅用于在加载配置时识别并迁移成"空值 = 使用内置模板"。</summary>
    private const string LegacyDefaultSystemPrompt =
        "你是一名专业翻译。将用户提供的文本翻译成目标语言。要求：只输出译文，保留换行与专有名词，不要解释。";

    public int SchemaVersion { get; set; } = 1;

    public ApiConfig Api { get; set; } = new();

    public TargetConfig Target { get; set; } = new();

    public HotkeyConfig Hotkey { get; set; } = new();

    public BehaviorConfig Behavior { get; set; } = new();

    public UiConfig Ui { get; set; } = new();

    public GeneralConfig General { get; set; } = new();

    /// <summary>把越界或缺失的值收敛到合法范围，保证运行期不再做防御性判断。</summary>
    public void Normalize()
    {
        if (SchemaVersion < 1)
        {
            SchemaVersion = 1;
        }

        Api ??= new ApiConfig();
        Target ??= new TargetConfig();
        Hotkey ??= new HotkeyConfig();
        Behavior ??= new BehaviorConfig();
        Ui ??= new UiConfig();
        General ??= new GeneralConfig();

        Api.ProviderType = string.IsNullOrWhiteSpace(Api.ProviderType) ? "openai-compatible" : Api.ProviderType.Trim();
        Api.BaseUrl = string.IsNullOrWhiteSpace(Api.BaseUrl) ? "https://api.deepseek.com/v1" : Api.BaseUrl.Trim();
        Api.Model = string.IsNullOrWhiteSpace(Api.Model) ? "deepseek-chat" : Api.Model.Trim();
        Api.Temperature = Math.Clamp(Api.Temperature, 0d, 1d);
        Api.TimeoutMs = Math.Clamp(Api.TimeoutMs, 10_000, 120_000);
        Api.ConnectTimeoutMs = Math.Clamp(Api.ConnectTimeoutMs, 3_000, 30_000);
        var prompt = Api.SystemPrompt?.Trim();
        Api.SystemPrompt = string.IsNullOrEmpty(prompt) || prompt == LegacyDefaultSystemPrompt
            ? string.Empty
            : prompt;
        Api.ExtraHeaders ??= new Dictionary<string, string>();

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
}

public sealed class ApiConfig
{
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