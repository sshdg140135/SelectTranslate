using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SelectTranslate.Core.Configuration;
using SelectTranslate.Core.Input;
using SelectTranslate.Core.Logging;
using SelectTranslate.Core.Translation;

// WinForms 有同名 KeyEventArgs，这里固定用 WPF 的。
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace SelectTranslate.App.Views;

/// <summary>
/// 设置窗口（最小可用版）：多服务商管理 + 目标语言 + 触发热键。
/// 关闭按钮只隐藏，退出程序时由 App 调用 CloseForShutdown。
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly ConfigService _config;
    private readonly AppLog _log;
    private readonly TranslationService _translation;

    private bool _allowClose;
    private bool _loading;
    private ApiConfig? _editing;

    public SettingsWindow(ConfigService config, AppLog log, TranslationService translation)
    {
        InitializeComponent();
        _config = config;
        _log = log;
        _translation = translation;
        Loaded += (_, _) => Refresh();
    }

    public void Refresh()
    {
        _loading = true;

        try
        {
            RebuildProviderList();

            var current = _config.Current;
            ActiveProviderText.Text = $"当前使用：{current.ActiveProvider.Name}";

            foreach (var item in TargetBox.Items.OfType<ComboBoxItem>())
            {
                if (string.Equals(item.Tag as string, current.Target.Lang, StringComparison.OrdinalIgnoreCase))
                {
                    TargetBox.SelectedItem = item;
                    break;
                }
            }

            if (TargetBox.SelectedItem is null && TargetBox.Items.Count > 0)
            {
                TargetBox.SelectedIndex = 0;
            }

            HotkeyBox.Text = current.Hotkey.Keyboard;
            HotkeyHint.Visibility = Visibility.Collapsed;

            VersionText.Text = $"版本：{AppInfo.Version}（{AppInfo.Framework}）";
            ConfigPathText.Text = $"配置：{_config.FilePath}";
            LogPathText.Text = $"日志：{_log.DirectoryPath}";
            HotkeyText.Text = current.Hotkey.Enabled ? "翻译：已启用" : "翻译：已暂停（托盘可恢复）";
        }
        finally
        {
            _loading = false;
        }
    }

    public void CloseForShutdown()
    {
        _allowClose = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnClosing(e);
    }

    private void RebuildProviderList()
    {
        var current = _config.Current;
        var selectedId = _editing?.Id;

        ProviderList.Items.Clear();

        foreach (var provider in current.Providers)
        {
            var marker = provider.Id == current.ActiveProviderId ? "● " : "   ";
            var item = new ProviderItem(provider, $"{marker}{provider.Name}  ·  {provider.Model}");
            ProviderList.Items.Add(item);

            if (provider.Id == selectedId)
            {
                ProviderList.SelectedItem = item;
            }
        }

        if (ProviderList.SelectedItem is null && ProviderList.Items.Count > 0)
        {
            ProviderList.SelectedIndex = 0;
        }

        ActiveProviderText.Text = $"当前使用：{current.ActiveProvider.Name}";
    }

    private void OnProviderSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ProviderList.SelectedItem is not ProviderItem item)
        {
            return;
        }

        // 先把正在编辑的服务商内容存回对象，再加载新选中的。
        ApplyFieldsToEditing();

        _editing = item.Provider;
        LoadFields(_editing);
    }

    private void LoadFields(ApiConfig provider)
    {
        _loading = true;

        try
        {
            NameBox.Text = provider.Name;
            BaseUrlBox.Text = provider.BaseUrl;
            ModelBox.Text = provider.Model;
            ExtraBodyBox.Text = provider.ExtraBodyJson;

            foreach (var item in ThinkingBox.Items.OfType<ComboBoxItem>())
            {
                if (string.Equals(item.Tag as string, provider.ThinkingMode, StringComparison.Ordinal))
                {
                    ThinkingBox.SelectedItem = item;
                    break;
                }
            }

            if (ThinkingBox.SelectedItem is null && ThinkingBox.Items.Count > 0)
            {
                ThinkingBox.SelectedIndex = 0;
            }

            UpdateThinkingInfo();
            ApiKeyBox.Password = _config.GetApiKey(provider) ?? string.Empty;

            foreach (var item in ProviderTypeBox.Items.OfType<ComboBoxItem>())
            {
                if (string.Equals(item.Tag as string, provider.ProviderType, StringComparison.OrdinalIgnoreCase))
                {
                    ProviderTypeBox.SelectedItem = item;
                    break;
                }
            }

            if (ProviderTypeBox.SelectedItem is null && ProviderTypeBox.Items.Count > 0)
            {
                ProviderTypeBox.SelectedIndex = 0;
            }
        }
        finally
        {
            _loading = false;
        }
    }

    private void ApplyFieldsToEditing()
    {
        if (_editing is null)
        {
            return;
        }

        _editing.Name = string.IsNullOrWhiteSpace(NameBox.Text) ? _editing.Name : NameBox.Text.Trim();
        _editing.BaseUrl = BaseUrlBox.Text.Trim();
        _editing.Model = ModelBox.Text.Trim();
        _editing.ExtraBodyJson = ExtraBodyBox.Text.Trim();

        if (ThinkingBox.SelectedItem is ComboBoxItem thinkingItem && thinkingItem.Tag is string mode)
        {
            _editing.ThinkingMode = mode;
        }

        if (ProviderTypeBox.SelectedItem is ComboBoxItem typeItem && typeItem.Tag is string type)
        {
            _editing.ProviderType = type;
        }

        if (ApiKeyBox.Password.Length > 0)
        {
            _config.SetApiKey(_editing, ApiKeyBox.Password);
        }
    }

    private void OnAddProviderClick(object sender, RoutedEventArgs e)
    {
        ApplyFieldsToEditing();

        var provider = new ApiConfig
        {
            Name = $"服务商 {_config.Current.Providers.Count + 1}",
            BaseUrl = "https://api.deepseek.com/v1",
            Model = "deepseek-chat",
        };

        _config.Current.Providers.Add(provider);
        _config.Current.Normalize();
        _config.Save();

        _editing = null;
        Refresh();
        SelectProvider(provider.Id);
        StatusText.Text = "已新增服务商，填写信息后点保存。";
    }

    private void OnDeleteProviderClick(object sender, RoutedEventArgs e)
    {
        if (ProviderList.SelectedItem is not ProviderItem item)
        {
            return;
        }

        var current = _config.Current;
        if (current.Providers.Count <= 1)
        {
            StatusText.Text = "至少保留一个服务商。";
            return;
        }

        current.Providers.Remove(item.Provider);
        current.Normalize();
        _editing = null;
        _config.Save();

        Refresh();
        StatusText.Text = $"已删除「{item.Provider.Name}」。";
    }

    private void OnSetActiveClick(object sender, RoutedEventArgs e)
    {
        if (ProviderList.SelectedItem is not ProviderItem item)
        {
            return;
        }

        ApplyFieldsToEditing();

        if (!_config.Current.SetActiveProvider(item.Provider.Id))
        {
            StatusText.Text = "切换失败：服务商不存在。";
            return;
        }

        _config.Current.Normalize();
        _config.Save();
        Refresh();

        StatusText.Text = $"已切换到「{item.Provider.Name}」，下一次翻译立即生效。";
        _log.Info($"切换服务商：{item.Provider.Name}（{item.Provider.ProviderType}）");
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Hide();

    private void OnClearHotkeyClick(object sender, RoutedEventArgs e)
    {
        HotkeyBox.Text = string.Empty;
        HotkeyHint.Visibility = Visibility.Collapsed;
    }

    private void OnThinkingChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        UpdateThinkingInfo();
    }

    private void OnModelChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        UpdateThinkingInfo();
    }

    /// <summary>展示"按模型名推断出哪种写法 + 本次将写入什么"。</summary>
    private void UpdateThinkingInfo()
    {
        var plan = ThinkingStrategies.Detect(ModelBox.Text);
        var mode = (ThinkingBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "default";

        ThinkingInfoText.Text = plan.Kind == ThinkingParameterKind.None
            ? $"推断：{plan.DisplayName}｜{plan.Note}"
            : $"推断：{plan.DisplayName}（{DescribeKind(plan.Kind)}）"
              + (string.IsNullOrEmpty(plan.Note) ? string.Empty : $"｜{plan.Note}");

        if (mode is "on" or "off")
        {
            var patch = ThinkingStrategies.DescribePatch(plan, mode == "on");
            if (patch is null)
            {
                ThinkingHint.Text = "该模型不会被自动写入思考参数；如需关闭请用下面的附加请求参数。";
            }
            else
            {
                ThinkingHint.Text = $"将写入：{patch}";
            }

            ThinkingHint.Visibility = Visibility.Visible;
        }
        else
        {
            ThinkingHint.Visibility = Visibility.Collapsed;
        }
    }

    private static string DescribeKind(ThinkingParameterKind kind) => kind switch
    {
        ThinkingParameterKind.ThinkingType => "thinking.type",
        ThinkingParameterKind.EnableThinking => "enable_thinking",
        ThinkingParameterKind.ReasoningEffort => "reasoning_effort",
        _ => "无",
    };

    private void OnHotkeyBoxKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl
            or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin
            or Key.None)
        {
            return;
        }

        if (key == Key.Escape)
        {
            HotkeyBox.Text = string.Empty;
            HotkeyHint.Visibility = Visibility.Collapsed;
            return;
        }

        var modifiers = Keyboard.Modifiers;
        if (modifiers == ModifierKeys.None)
        {
            HotkeyHint.Text = "至少再加一个 Ctrl / Alt / Shift / Win。";
            HotkeyHint.Visibility = Visibility.Visible;
            return;
        }

        var virtualKey = KeyInterop.VirtualKeyFromKey(key);
        HotkeyBox.Text = HotkeyParser.Format(
            virtualKey,
            modifiers.HasFlag(ModifierKeys.Control),
            modifiers.HasFlag(ModifierKeys.Alt),
            modifiers.HasFlag(ModifierKeys.Shift),
            modifiers.HasFlag(ModifierKeys.Windows));

        HotkeyHint.Visibility = Visibility.Collapsed;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (!Save())
        {
            return;
        }

        StatusText.Text = "已保存，下一次翻译生效。";
    }

    private bool Save()
    {
        ApplyFieldsToEditing();

        if (!ValidateExtraBody())
        {
            return false;
        }

        var hotkeyText = HotkeyBox.Text.Trim();
        if (hotkeyText.Length > 0 && !HotkeyParser.TryParse(hotkeyText, out _))
        {
            HotkeyHint.Text = "热键格式不正确，示例：Ctrl+Alt+T。";
            HotkeyHint.Visibility = Visibility.Visible;
            return false;
        }

        HotkeyBox.Text = hotkeyText;
        _config.Current.Hotkey.Keyboard = hotkeyText;

        if (TargetBox.SelectedItem is ComboBoxItem targetItem && targetItem.Tag is string lang)
        {
            _config.Current.Target.Lang = lang;
        }

        _config.Save();

        if (_config.LastError is not null)
        {
            StatusText.Text = $"保存失败：{_config.LastError}";
            return false;
        }

        Refresh();
        return true;
    }

    private bool ValidateExtraBody()
    {
        var text = ExtraBodyBox.Text.Trim();
        if (text.Length == 0)
        {
            return true;
        }

        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(text);
            if (document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object)
            {
                StatusText.Text = "附加请求参数必须是 JSON 对象，例如 {\"thinking\":{\"type\":\"disabled\"}}";
                return false;
            }

            return true;
        }
        catch (System.Text.Json.JsonException)
        {
            StatusText.Text = "附加请求参数不是合法 JSON，请检查后重试。";
            return false;
        }
    }

    private async void OnTestClick(object sender, RoutedEventArgs e)
    {
        if (ProviderList.SelectedItem is not ProviderItem item)
        {
            return;
        }

        ApplyFieldsToEditing();

        // 测试"选中的"服务商（可能不是当前使用的）：临时切换，测完还原。
        var current = _config.Current;
        var previousId = current.ActiveProviderId;
        current.SetActiveProvider(item.Provider.Id);

        StatusText.Text = $"正在测试「{item.Provider.Name}」…";

        try
        {
            var outcome = await _translation.TranslateAsync(
                "Hello, this is a connection test.",
                bypassLanguageCheck: true,
                onChunk: null,
                CancellationToken.None);

            StatusText.Text = $"连接正常，{(int)outcome.Elapsed.TotalMilliseconds} ms：{Shorten(outcome.Text)}";
        }
        catch (TranslationException ex)
        {
            StatusText.Text = $"连接失败：{ex.Message}";
            _log.Warn($"测试连接失败（{ex.Kind}）：{ex.Message}");
        }
        catch (Exception ex)
        {
            StatusText.Text = $"连接失败：{ex.Message}";
            _log.Warn($"测试连接异常：{ex.Message}");
        }
        finally
        {
            current.SetActiveProvider(previousId);
        }
    }

    private void SelectProvider(string providerId)
    {
        foreach (var item in ProviderList.Items.OfType<ProviderItem>())
        {
            if (item.Provider.Id == providerId)
            {
                ProviderList.SelectedItem = item;
                _editing = item.Provider;
                LoadFields(item.Provider);
                return;
            }
        }
    }

    private static string Shorten(string text)
    {
        var singleLine = text.Replace('\n', ' ').Trim();
        return singleLine.Length <= 40 ? singleLine : singleLine[..40] + "…";
    }

    private sealed record ProviderItem(ApiConfig Provider, string Text);
}