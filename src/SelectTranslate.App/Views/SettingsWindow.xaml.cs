using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SelectTranslate.Core.Input;

// WinForms 有同名 KeyEventArgs，这里固定用 WPF 的。
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using SelectTranslate.Core.Configuration;
using SelectTranslate.Core.Logging;
using SelectTranslate.Core.Translation;

namespace SelectTranslate.App.Views;

/// <summary>
/// 设置窗口（M2 最小配置页；M3 补齐五个页面与首次向导）。
/// 关闭按钮只隐藏，退出程序时由 App 调用 CloseForShutdown。
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly ConfigService _config;
    private readonly AppLog _log;
    private readonly TranslationService _translation;
    private bool _allowClose;

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
        var current = _config.Current;

        HotkeyBox.Text = current.Hotkey.Keyboard;
        HotkeyHint.Visibility = Visibility.Collapsed;
        BaseUrlBox.Text = current.Api.BaseUrl;
        ModelBox.Text = current.Api.Model;
        ApiKeyBox.Password = _config.GetApiKey() ?? string.Empty;

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

        VersionText.Text = $"版本：{AppInfo.Version}（{AppInfo.Framework}）";
        ConfigPathText.Text = $"配置：{_config.FilePath}";
        LogPathText.Text = $"日志：{_log.DirectoryPath}";
        HotkeyText.Text = current.Hotkey.Enabled ? "翻译：已启用" : "翻译：已暂停（托盘可恢复）";
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

    private void OnCloseClick(object sender, RoutedEventArgs e) => Hide();

    private void OnClearHotkeyClick(object sender, RoutedEventArgs e)
    {
        HotkeyBox.Text = string.Empty;
        HotkeyHint.Visibility = Visibility.Collapsed;
    }

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
        Save();
        StatusText.Text = _config.LastError is null ? "已保存，下一次翻译生效。" : $"保存失败：{_config.LastError}";
    }

    private async void OnTestClick(object sender, RoutedEventArgs e)
    {
        Save();

        StatusText.Text = "正在测试连接…";
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
    }

    private void Save()
    {
        var current = _config.Current;
        current.Api.BaseUrl = BaseUrlBox.Text.Trim();
        current.Api.Model = ModelBox.Text.Trim();

        if (ApiKeyBox.Password.Length > 0)
        {
            _config.SetApiKey(ApiKeyBox.Password);
        }

        if (TargetBox.SelectedItem is ComboBoxItem item && item.Tag is string lang)
        {
            current.Target.Lang = lang;
        }

        var hotkeyText = HotkeyBox.Text.Trim();
        if (hotkeyText.Length > 0 && !HotkeyParser.TryParse(hotkeyText, out _))
        {
            HotkeyHint.Text = "热键格式不正确，示例：Ctrl+Alt+T。";
            HotkeyHint.Visibility = Visibility.Visible;
            return;
        }

        HotkeyBox.Text = hotkeyText;
        current.Hotkey.Keyboard = hotkeyText;

        _config.Save();
        Refresh();
    }

    private static string Shorten(string text)
    {
        var singleLine = text.Replace('\n', ' ').Trim();
        return singleLine.Length <= 40 ? singleLine : singleLine[..40] + "…";
    }
}