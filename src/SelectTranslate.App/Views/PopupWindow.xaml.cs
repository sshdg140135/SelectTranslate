using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using SelectTranslate.App.Interop;
using SelectTranslate.Core.Placement;

// UseWindowsForms 会引入 WinForms 的同名类型，这里固定用 WPF 的。
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;
using ScrollBar = System.Windows.Controls.Primitives.ScrollBar;
using TextBoxBase = System.Windows.Controls.Primitives.TextBoxBase;
using Point = System.Windows.Point;

namespace SelectTranslate.App.Views;

/// <summary>
/// 结果卡片：非激活、置顶、DWM 圆角、贴着光标出现；展示原文、译文与操作按钮。
/// </summary>
public partial class PopupWindow : Window
{
    private const int MaxOriginalChars = 200;

    private NativeMethods.POINT _anchor;
    private bool _styled;
    private bool _userMoved;

    public PopupWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => ApplyWindowStyle();
        SizeChanged += (_, _) => PositionNearAnchor();
        DpiChanged += (_, _) => PositionNearAnchor();
    }

    public event EventHandler? CopyRequested;

    public event EventHandler? RetryRequested;

    public event EventHandler? TranslateAnywayRequested;

    public event EventHandler? OpenSettingsRequested;

    public event EventHandler? CloseRequested;

    /// <summary>图钉状态：钉住后点击卡片外部不关闭（REQ-05.4）。</summary>
    public bool IsPinned { get; private set; }

    public string CurrentOriginal { get; private set; } = string.Empty;

    public string CurrentTranslation { get; private set; } = string.Empty;

    /// <summary>取词成功、请求已发出，等待译文。</summary>
    public void ShowLoading(int screenX, int screenY, string original)
    {
        Prepare(screenX, screenY, original);
        CurrentTranslation = string.Empty;
        TranslationArea.Visibility = Visibility.Visible;
        TranslationText.Text = string.Empty;
        StatusText.Text = string.Empty;
        StartSpinner();
        HintText.Visibility = Visibility.Collapsed;
        SetButtons(copy: false, retry: false, anyway: false, settings: false);
        ShowCard();
    }

    /// <summary>前台是提权窗口等无法取词的场景，只给说明不显示译文区（AC-27）。</summary>
    public void ShowBlocked(int screenX, int screenY, string message)
    {
        Prepare(screenX, screenY, string.Empty);
        StopSpinner();
        TranslationArea.Visibility = Visibility.Collapsed;
        TranslationText.Text = string.Empty;
        CurrentTranslation = string.Empty;
        StatusText.Text = "无法取词";
        HintText.Text = message;
        HintText.Visibility = Visibility.Visible;
        SetButtons(copy: false, retry: false, anyway: false, settings: false);
        ShowCard();
    }

    /// <summary>原文已经是目标语言，未发起请求（REQ-03.2）。</summary>
    public void ShowSameLanguage(int screenX, int screenY, string original, string targetDisplayName)
    {
        Prepare(screenX, screenY, original);
        CurrentTranslation = string.Empty;
        TranslationText.Text = string.Empty;
        StatusText.Text = string.Empty;
        StopSpinner();
        TranslationArea.Visibility = Visibility.Collapsed;
        HintText.Text = $"原文已是{targetDisplayName}，未发起翻译。";
        HintText.Visibility = Visibility.Visible;
        SetButtons(copy: false, retry: false, anyway: true, settings: false);
        ShowCard();
    }

    /// <summary>流式增量：追加到译文区。</summary>
    public void AppendTranslation(string chunk)
    {
        if (string.IsNullOrEmpty(chunk))
        {
            return;
        }

        CurrentTranslation += chunk;
        TranslationText.Text = CurrentTranslation;
        StopSpinner();
        CopyButton.Visibility = Visibility.Visible;
    }

    /// <summary>翻译完成：整段替换并给出耗时。</summary>
    public void ShowTranslation(string text, bool truncated, bool echoed, TimeSpan elapsed)
    {
        TranslationArea.Visibility = Visibility.Visible;
        CurrentTranslation = text;
        TranslationText.Text = text;
        StopSpinner();
        StatusText.Text = $"{(int)elapsed.TotalMilliseconds} ms";

        var hint = truncated
            ? "文本过长，已截断（上限见设置）。"
            : echoed
                ? "模型返回了原文（可能被判为代码/标识符），可点「重试」。"
                : string.Empty;

        HintText.Text = hint;
        HintText.Visibility = string.IsNullOrEmpty(hint) ? Visibility.Collapsed : Visibility.Visible;
        SetButtons(copy: text.Length > 0, retry: echoed, anyway: false, settings: false);
    }

    public void ShowError(string message, bool offerSettings, bool offerRetry)
    {
        StopSpinner();
        StatusText.Text = "出错";
        HintText.Text = message;
        HintText.Visibility = Visibility.Visible;
        SetButtons(copy: CurrentTranslation.Length > 0, retry: offerRetry, anyway: false, settings: offerSettings);
    }

    public void SetStatus(string text) => StatusText.Text = text;

    public void SetHint(string text)
    {
        HintText.Text = text;
        HintText.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    public void HideCard()
    {
        StopSpinner();

        if (IsVisible)
        {
            Hide();
        }
    }

    private void StartSpinner()
    {
        LoadingPanel.Visibility = Visibility.Visible;
        Spinner.Visibility = Visibility.Visible;

        var animation = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.9))
        {
            RepeatBehavior = RepeatBehavior.Forever,
        };

        SpinnerRotate.BeginAnimation(RotateTransform.AngleProperty, animation);
    }

    private void StopSpinner()
    {
        SpinnerRotate.BeginAnimation(RotateTransform.AngleProperty, null);
        Spinner.Visibility = Visibility.Collapsed;
        LoadingPanel.Visibility = Visibility.Collapsed;
    }

    /// <summary>判断屏幕坐标是否落在卡片内（用于"点击外部关闭"）。</summary>
    public bool ContainsPoint(int screenX, int screenY)
    {
        if (!IsVisible)
        {
            return false;
        }

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || !NativeMethods.GetWindowRect(hwnd, out var rect))
        {
            return false;
        }

        return screenX >= rect.Left && screenX < rect.Right
            && screenY >= rect.Top && screenY < rect.Bottom;
    }

    private void Prepare(int screenX, int screenY, string original)
    {
        _anchor = new NativeMethods.POINT { X = screenX, Y = screenY };
        CurrentOriginal = original;

        // 每次新内容都从"未钉住、未拖动"开始。
        IsPinned = false;
        _userMoved = false;
        UpdatePinVisual();

        OriginalText.Text = original.Length > MaxOriginalChars
            ? original[..MaxOriginalChars] + "…"
            : original;
    }

    private void ShowCard()
    {
        if (!IsVisible)
        {
            Show();
        }

        PositionNearAnchor();
    }

    private void SetButtons(bool copy, bool retry, bool anyway, bool settings)
    {
        CopyButton.Visibility = copy ? Visibility.Visible : Visibility.Collapsed;
        RetryButton.Visibility = retry ? Visibility.Visible : Visibility.Collapsed;
        TranslateAnywayButton.Visibility = anyway ? Visibility.Visible : Visibility.Collapsed;
        SettingsButton.Visibility = settings ? Visibility.Visible : Visibility.Collapsed;
        Toolbar.Visibility = copy || retry || anyway || settings
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void OnCardMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // 已钉住 + 落在"最上方一条"（顶部内边距 + 头部行）才允许拖动。
        if (!IsPinned || IsInteractiveSource(e.OriginalSource))
        {
            return;
        }

        if (!IsInHeaderBand(e.GetPosition(CardBorder)))
        {
            return;
        }

        try
        {
            DragMove();
            _userMoved = true;
        }
        catch (InvalidOperationException)
        {
            // 鼠标已经抬起等边缘情况，忽略。
        }
    }

    /// <summary>顶部条带：从卡片顶边（含 14px 内边距）到头部行底部。</summary>
    private bool IsInHeaderBand(Point point)
    {
        if (HeaderBar.ActualHeight <= 0)
        {
            return false;
        }

        var bounds = HeaderBar
            .TransformToAncestor(CardBorder)
            .TransformBounds(new Rect(0, 0, HeaderBar.ActualWidth, HeaderBar.ActualHeight));

        return point.Y >= 0 && point.Y <= bounds.Bottom;
    }

    private static bool IsInteractiveSource(object? source)
    {
        if (source is not DependencyObject node)
        {
            return false;
        }

        for (var current = node; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is ButtonBase or TextBoxBase or ScrollBar)
            {
                return true;
            }
        }

        return false;
    }

    private void OnPinClick(object sender, RoutedEventArgs e)
    {
        IsPinned = !IsPinned;
        UpdatePinVisual();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
        => CloseRequested?.Invoke(this, EventArgs.Empty);

    private void UpdatePinVisual()
    {
        if (IsPinned)
        {
            PinButton.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE3, 0xEC, 0xFD));
            PinButton.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xB4, 0xC8, 0xF5));
            PinButton.ToolTip = "取消钉住";
        }
        else
        {
            PinButton.ClearValue(BackgroundProperty);
            PinButton.ClearValue(BorderBrushProperty);
            PinButton.ToolTip = "钉住卡片";
        }
    }

    private void OnCopyClick(object sender, RoutedEventArgs e) => CopyRequested?.Invoke(this, EventArgs.Empty);

    private void OnRetryClick(object sender, RoutedEventArgs e) => RetryRequested?.Invoke(this, EventArgs.Empty);

    private void OnTranslateAnywayClick(object sender, RoutedEventArgs e)
        => TranslateAnywayRequested?.Invoke(this, EventArgs.Empty);

    private void OnSettingsClick(object sender, RoutedEventArgs e)
        => OpenSettingsRequested?.Invoke(this, EventArgs.Empty);

    private void OnTranslationDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (CurrentTranslation.Length > 0)
        {
            CopyRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ApplyWindowStyle()
    {
        if (_styled)
        {
            return;
        }

        _styled = true;

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var exStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        exStyle |= NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW;
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(exStyle));

        var preference = NativeMethods.DWMWCP_ROUND;
        NativeMethods.DwmSetWindowAttribute(
            hwnd,
            NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE,
            ref preference,
            Marshal.SizeOf<int>());
    }

    private void PositionNearAnchor()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || !IsVisible)
        {
            return;
        }

        var dpi = NativeMethods.GetDpiForWindow(hwnd) / 96.0;
        if (dpi <= 0)
        {
            dpi = 1.0;
        }

        var widthPx = (int)Math.Round(Width * dpi);
        var heightPx = (int)Math.Round(Math.Max(ActualHeight, MinHeight) * dpi);

        var monitor = NativeMethods.MonitorFromPoint(_anchor, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var info = new NativeMethods.MONITORINFO { CbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (!NativeMethods.GetMonitorInfo(monitor, ref info))
        {
            return;
        }

        var workHeight = info.RcWork.Bottom - info.RcWork.Top;
        TranslationScroll.MaxHeight = Math.Max(120, workHeight * 0.6 - 150);

        if (IsPinned && _userMoved)
        {
            // 用户拖动过：保持左上角，只随内容更新高度。
            NativeMethods.SetWindowPos(
                hwnd,
                NativeMethods.HWND_TOPMOST,
                0,
                0,
                widthPx,
                heightPx,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
            return;
        }

        var workArea = new WorkArea(
            info.RcWork.Left,
            info.RcWork.Top,
            info.RcWork.Right,
            info.RcWork.Bottom);

        var (x, y) = CardPlacement.Compute(
            _anchor.X,
            _anchor.Y,
            widthPx,
            heightPx,
            workArea);

        NativeMethods.SetWindowPos(
            hwnd,
            NativeMethods.HWND_TOPMOST,
            x,
            y,
            widthPx,
            heightPx,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
    }
}