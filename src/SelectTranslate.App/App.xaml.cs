using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using SelectTranslate.App.Capture;
using SelectTranslate.App.Interop;
using SelectTranslate.App.Shell;
using SelectTranslate.App.Views;
using SelectTranslate.Core.Configuration;
using SelectTranslate.Core.Input;
using SelectTranslate.Core.Languages;
using SelectTranslate.Core.Logging;
using SelectTranslate.Core.Startup;
using SelectTranslate.Core.Translation;

// UseWindowsForms 会引入 System.Windows.Forms 的全局 using，这里显式指定 WPF 的 Application。
using Application = System.Windows.Application;
using Clipboard = System.Windows.Clipboard;

namespace SelectTranslate.App;

public partial class App : Application
{
    private SingleInstance? _single;
    private ConfigService? _config;
    private AppLog? _log;
    private ServiceProvider? _services;
    private AutoStartService? _autoStart;
    private MouseHook? _hook;
    private KeyboardHook? _keyboardHook;
    private TrayService? _tray;
    private PopupWindow? _popup;
    private SettingsWindow? _settings;
    private bool _portable;
    private bool _exiting;
    private readonly SelectionReader _selectionReader = new();
    private CancellationTokenSource? _captureCts;
    private CancellationTokenSource? _translationCts;
    private TranslationService? _translation;
    private HttpClient? _httpClient;
    private bool _selfTest;
    private bool _hookTest;
    private int _hookTestCount;
    private volatile int _swallowedVirtualKey;
    private string _lastText = string.Empty;
    private int _lastX;
    private int _lastY;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var renderIndex = Array.FindIndex(e.Args, a =>
            string.Equals(a, "--render-card", StringComparison.OrdinalIgnoreCase));
        if (renderIndex >= 0)
        {
            var pngPath = renderIndex + 1 < e.Args.Length
                ? e.Args[renderIndex + 1]
                : Path.Combine(AppContext.BaseDirectory, "card-preview.png");

            Dispatcher.InvokeAsync(() => RenderCardPreview(pngPath));
            return;
        }

        var probeIndex = Array.FindIndex(e.Args, a =>
            string.Equals(a, "--probe-selection", StringComparison.OrdinalIgnoreCase));
        if (probeIndex >= 0)
        {
            var outFile = probeIndex + 1 < e.Args.Length
                ? e.Args[probeIndex + 1]
                : Path.Combine(AppContext.BaseDirectory, "selection-probe.txt");

            Dispatcher.InvokeAsync(async () =>
            {
                var text = await _selectionReader.ReadAsync(
                    NativeMethods.GetForegroundWindow(),
                    CancellationToken.None);
                File.WriteAllText(outFile, text ?? "<null>");
                Shutdown();
            });

            return;
        }

        try
        {
            Initialize(e);
        }
        catch (Exception ex)
        {
            FailStartup(ex);
        }
    }

    private void Initialize(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            _log?.Error("未处理异常（AppDomain）", args.ExceptionObject as Exception);

        _portable = e.Args.Any(a => string.Equals(a, "-portable", StringComparison.OrdinalIgnoreCase));
        _hookTest = e.Args.Any(a => string.Equals(a, "--hook-test", StringComparison.OrdinalIgnoreCase));

        var restartedElevated = e.Args.Any(a =>
            string.Equals(a, "--restarted-elevated", StringComparison.OrdinalIgnoreCase));

        _single = new SingleInstance(
            restartedElevated ? TimeSpan.FromSeconds(5) : null);
        if (!_single.IsFirstInstance)
        {
            SingleInstance.SignalExisting();
            Shutdown();
            return;
        }

        // 仅用于受限环境排查界面/XAML 能否构造；正常启动不允许低完整性。
        var allowLowIntegrity = e.Args.Any(a =>
            string.Equals(a, "--allow-low-integrity", StringComparison.OrdinalIgnoreCase));
        _selfTest = e.Args.Any(a => string.Equals(a, "--self-test", StringComparison.OrdinalIgnoreCase));

        if (!allowLowIntegrity && IntegrityLevel.IsCurrentProcessLow())
        {
            // 低完整性进程写不了用户目录、托盘图标会被 UIPI 拦掉、也收不到中键，
            // 与其静默变砖，不如直接说清楚。
            throw new InvalidOperationException(
                "程序以低完整性（Low Integrity）运行，系统会禁止它写用户目录、显示托盘图标和接收中键。"
                + "请把程序复制到普通目录（桌面、%LOCALAPPDATA% 等）后再启动，不要从受控/沙箱目录直接运行。");
        }

        var requestedBase = ConfigPaths.ResolveBaseDirectory(_portable, AppContext.BaseDirectory);
        var baseDirectory = ConfigPaths.EnsureWritableBase(requestedBase, AppContext.BaseDirectory);
        var fellBack = !string.Equals(
            Path.GetFullPath(baseDirectory),
            Path.GetFullPath(requestedBase),
            StringComparison.OrdinalIgnoreCase);

        if (fellBack && string.Equals(
                Path.GetFullPath(baseDirectory),
                Path.GetFullPath(ConfigPaths.ResolvePortableDirectory(AppContext.BaseDirectory)),
                StringComparison.OrdinalIgnoreCase))
        {
            // 回退到程序目录时按便携模式处理：不写开机自启。
            _portable = true;
        }

        _config = new ConfigService(baseDirectory);
        _config.Load();
        if (_portable)
        {
            _config.Current.General.Portable = true;
        }

        _log = new AppLog(Path.Combine(baseDirectory, "logs"), _config.Current.General.LogLevel);
        _log.Info($"启动 {AppInfo.DisplayName} {AppInfo.Version}，base={baseDirectory}，portable={_portable}");

        if (fellBack)
        {
            _log.Warn($"首选配置目录不可写（{requestedBase}），已回退到 {baseDirectory}");
        }

        if (_config.LastError is not null)
        {
            _log.Warn($"配置加载失败，已回退默认值：{_config.LastError}");
        }

        _autoStart = new AutoStartService();

        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            AutomaticDecompression = DecompressionMethods.All,
        };
        _httpClient = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };

        var registry = new ProviderRegistry();
        registry.Register(new OpenAiCompatibleProvider(
            _httpClient,
            BuildApiSettings,
            message => _log?.Debug(message)));
        _translation = new TranslationService(registry, () => _config!.Current);

        var services = new ServiceCollection();
        services.AddSingleton(_config);
        services.AddSingleton(_log);
        services.AddSingleton(_autoStart);
        services.AddSingleton<PopupWindow>();
        services.AddSingleton<SettingsWindow>();
        services.AddSingleton<MouseHook>();
        services.AddSingleton(registry);
        services.AddSingleton(_translation);
        services.AddSingleton(sp => new TrayService(
            AppInfo.DisplayName,
            _config.Current.Hotkey.Enabled,
            _autoStart.IsEnabled()));
        _services = services.BuildServiceProvider();

        _popup = _services.GetRequiredService<PopupWindow>();
        _settings = _services.GetRequiredService<SettingsWindow>();
        _hook = _services.GetRequiredService<MouseHook>();
        _tray = _services.GetRequiredService<TrayService>();
        _log.Info(
            $"托盘已就绪（权限：{(Elevation.IsCurrentProcessElevated() ? "管理员" : "标准用户")}）");

        _popup.CopyRequested += (_, _) => CopyTranslation();
        _popup.RetryRequested += (_, _) => RetryTranslation();
        _popup.TranslateAnywayRequested += (_, _) => TranslateAnyway();
        _popup.OpenSettingsRequested += (_, _) => ShowSettings();
        _popup.CloseRequested += (_, _) => HideCard();

        _tray.EnabledToggled += OnEnabledToggled;
        _tray.AutoStartToggled += OnAutoStartToggled;
        _tray.OpenSettingsRequested += ShowSettings;
        _tray.ElevateRequested += RestartElevated;
        _tray.SetElevatedState(Elevation.IsCurrentProcessElevated());
        _tray.OpenLogsRequested += OpenLogs;
        _tray.ExitRequested += ExitApplication;

        _single.ActivationRequested += () => Dispatcher.BeginInvoke(new Action(ShowSettings));
        _single.StartListening();

        SyncAutoStart();

        _hook.Enabled = _config.Current.Hotkey.Enabled;
        _hook.Diagnostic += message => _log?.Debug(message);
        _hook.MiddlePressed += OnMiddlePressed;
        _hook.LeftButtonDown += OnLeftButtonDown;

        _keyboardHook = new KeyboardHook();
        _keyboardHook.EscapePressed += (_, _) => Dispatcher.BeginInvoke(new Action(OnEscapePressed));
        _keyboardHook.KeyFilter = FilterKeyboardHotkey;
        if (_keyboardHook.Start())
        {
            _log.Info("键盘钩子已安装（Esc 关闭卡片）");
        }
        else
        {
            _log.Warn("键盘钩子安装失败，Esc 关闭不可用");
        }

        if (_selfTest)
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                _log?.Info("自检完成");
                ExitApplication();
            };
            timer.Start();
        }

        if (_hookTest)
        {
            var injectTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            injectTimer.Tick += (_, _) =>
            {
                injectTimer.Stop();
                _log?.Info("钩子自测：注入一次中键");
                NativeMethods.SetCursorPos(600, 400);
                NativeMethods.mouse_event(NativeMethods.MOUSEEVENTF_MIDDLEDOWN, 0, 0, 0, UIntPtr.Zero);
                NativeMethods.mouse_event(NativeMethods.MOUSEEVENTF_MIDDLEUP, 0, 0, 0, UIntPtr.Zero);
            };
            injectTimer.Start();

            var finishTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
            finishTimer.Tick += (_, _) =>
            {
                finishTimer.Stop();
                _log?.Info($"钩子自测完成：收到 {_hookTestCount} 次中键");
                ExitApplication();
            };
            finishTimer.Start();
        }

        if (_hook.Start())
        {
            _log.Info("鼠标钩子已安装");
        }
        else
        {
            _log.Error("鼠标钩子安装失败");
            _tray.ShowBalloon("快捷键不可用", "鼠标钩子安装失败，可能被安全软件拦截。");
        }
    }

    private void OnMiddlePressed(object? sender, MouseTriggerEventArgs e)
    {
        if (_hookTest)
        {
            _hookTestCount++;
        }

        _log?.Debug($"中键触发 {e.X},{e.Y}");
        // 钩子线程上只做投递，取词与渲染回到 UI 线程。
        Dispatcher.BeginInvoke(new Action(() => _ = HandleMiddlePressAsync(e)));
    }

    private Task HandleMiddlePressAsync(MouseTriggerEventArgs e) => TriggerAtAsync(e.X, e.Y);

    /// <summary>键盘热键触发：以当前光标位置为锚点。</summary>
    private bool FilterKeyboardHotkey(KeyStroke stroke)
    {
        // 抬起：配对吞掉，避免前台应用收到半次组合键。
        if (!stroke.IsDown)
        {
            if (_swallowedVirtualKey == stroke.VirtualKey)
            {
                _swallowedVirtualKey = 0;
                return true;
            }

            return false;
        }

        if (_config?.Current.Hotkey.Enabled != true)
        {
            return false;
        }

        if (!HotkeyParser.TryParse(_config.Current.Hotkey.Keyboard, out var hotkey))
        {
            return false;
        }

        if (!HotkeyParser.Matches(hotkey, stroke.VirtualKey, stroke.Ctrl, stroke.Alt, stroke.Shift, stroke.Win))
        {
            return false;
        }

        _swallowedVirtualKey = stroke.VirtualKey;

        NativeMethods.GetCursorPos(out var point);
        var x = point.X;
        var y = point.Y;

        Dispatcher.BeginInvoke(new Action(() => _ = TriggerAtAsync(x, y)));
        return true;
    }

    private async Task TriggerAtAsync(int x, int y)
    {
        if (_popup is null)
        {
            return;
        }

        // 再次触发：先关旧卡并取消在途请求，再在本次光标位置重新取词。
        HideCard();

        var foreground = NativeMethods.GetForegroundWindow();
        if (Elevation.IsWindowElevated(foreground) && !Elevation.IsCurrentProcessElevated())
        {
            _popup.ShowBlocked(
                x,
                y,
                "前台程序以管理员身份运行，普通权限无法读取选区。请以管理员身份运行本程序后再试。");
            _log?.Info("前台窗口已提权，跳过取词");
            return;
        }

        var previous = _captureCts;
        previous?.Cancel();

        var cts = new CancellationTokenSource();
        _captureCts = cts;

        try
        {
            var text = await _selectionReader.ReadAsync(NativeMethods.GetForegroundWindow(), cts.Token);
            if (cts.IsCancellationRequested)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                _log?.Info("未选中文本，忽略中键");
                return;
            }

            _popup.ShowLoading(x, y, text);
            _log?.Info($"取词成功，{text.Length} 字");

            _lastText = text;
            _lastX = x;
            _lastY = y;
            await StartTranslationAsync(text, bypassLanguageCheck: false, x, y);
        }
        catch (OperationCanceledException)
        {
            // 被下一次触发取消，正常。
        }
        catch (Exception ex)
        {
            _log?.Error("取词失败", ex);
        }
        finally
        {
            if (ReferenceEquals(_captureCts, cts))
            {
                _captureCts = null;
            }

            cts.Dispose();
        }
    }

    private void OnEscapePressed()
    {
        if (_config?.Current.Behavior.EscapeCloses != true)
        {
            return;
        }

        if (_popup is { IsVisible: true })
        {
            HideCard();
        }
    }

    private void OnLeftButtonDown(object? sender, MouseTriggerEventArgs e)
    {
        // 钩子线程上不允许触碰任何 WPF 对象（IsVisible / 句柄都会校验线程），
        // 全部挪到 UI 线程再判断。
        var x = e.X;
        var y = e.Y;

        Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                if (_popup is null || !_popup.IsVisible || _popup.IsPinned || _popup.ContainsPoint(x, y))
                {
                    return;
                }

                HideCard();
            }
            catch (Exception ex)
            {
                _log?.Error("关闭卡片失败", ex);
            }
        }));
    }

    private ApiSettings BuildApiSettings()
    {
        var api = _config!.Current.Api;
        return new ApiSettings(
            api.BaseUrl,
            _config.GetApiKey(),
            api.Model,
            api.Temperature,
            api.TimeoutMs,
            api.ConnectTimeoutMs,
            api.SystemPrompt,
            api.ExtraHeaders,
            api.Stream,
            api.MaxTokens,
            api.ExtraBodyJson,
            api.ThinkingMode);
    }

    private async Task StartTranslationAsync(string text, bool bypassLanguageCheck, int anchorX, int anchorY)
    {
        if (_translation is null || _popup is null || _config is null)
        {
            return;
        }

        _translationCts?.Cancel();
        var cts = new CancellationTokenSource();
        _translationCts = cts;

        try
        {
            var progress = new Progress<TranslationChunk>(chunk =>
            {
                if (!cts.IsCancellationRequested)
                {
                    _popup?.AppendTranslation(chunk.Text);
                }
            });

            _log?.Debug(
                $"LLM 请求 model={_config.Current.Api.Model}，target={_config.Current.Target.Lang}，chars={text.Length}");

            var outcome = await _translation.TranslateAsync(text, bypassLanguageCheck, progress, cts.Token);
            if (cts.IsCancellationRequested)
            {
                return;
            }

            if (outcome.SameAsTarget)
            {
                _popup.ShowSameLanguage(
                    anchorX,
                    anchorY,
                    text,
                    LanguageDetector.DisplayName(_config.Current.Target.Lang));
                _log?.Info($"语种短路：{outcome.DetectedLanguage} 已是目标语言");
                return;
            }

            _popup.ShowTranslation(outcome.Text, outcome.Truncated, outcome.Echoed, outcome.Elapsed);
            _log?.Info(
                $"翻译完成 {outcome.Text.Length} 字，{outcome.Elapsed.TotalMilliseconds:0} ms"
                + $"，model={outcome.Model}，provider={_config.Current.ActiveProvider.Name}，target={_config.Current.Target.Lang}"
                + $"，检测={outcome.DetectedLanguage}"
                + (outcome.Truncated ? "，已截断" : string.Empty)
                + (outcome.Echoed ? "，原样返回" : string.Empty));
        }
        catch (OperationCanceledException)
        {
            // 被新的触发或关闭取消，正常。
        }
        catch (TranslationException ex)
        {
            var (message, offerSettings, offerRetry) = Describe(ex);
            _popup.ShowError(message, offerSettings, offerRetry);
            _log?.Error($"翻译失败（{ex.Kind}）", ex);
        }
        catch (Exception ex)
        {
            _popup.ShowError("翻译失败，请重试。", offerSettings: false, offerRetry: true);
            _log?.Error("翻译异常", ex);
        }
        finally
        {
            if (ReferenceEquals(_translationCts, cts))
            {
                _translationCts = null;
            }

            cts.Dispose();
        }
    }

    private static (string Message, bool OfferSettings, bool OfferRetry) Describe(TranslationException ex)
        => ex.Kind switch
        {
            TranslationErrorKind.Auth => ("API Key 无效或无权限，请到设置中检查。", true, false),
            TranslationErrorKind.ConfigMissing => ("尚未配置 API Key，请先到设置中填写。", true, false),
            TranslationErrorKind.Model => ("模型或接口地址不存在，请检查设置。", true, false),
            TranslationErrorKind.RateLimit => (ex.RetryAfterSeconds is { } seconds
                ? $"请求过于频繁，请 {seconds} 秒后重试。"
                : "请求过于频繁，请稍后重试。", false, true),
            TranslationErrorKind.Network => ("网络不可达，请检查网络或代理设置。", false, true),
            TranslationErrorKind.Timeout => ("请求超时，请重试。", false, true),
            TranslationErrorKind.BadResponse => ("服务端返回异常，请重试。", false, true),
            TranslationErrorKind.Internal => ("服务端错误，请稍后重试。", false, true),
            _ => ("翻译失败，请重试。", false, true),
        };

    private void RetryTranslation()
    {
        if (_popup is null || _lastText.Length == 0)
        {
            return;
        }

        _popup.ShowLoading(_lastX, _lastY, _lastText);
        _ = StartTranslationAsync(_lastText, bypassLanguageCheck: false, _lastX, _lastY);
    }

    private void TranslateAnyway()
    {
        if (_popup is null || _lastText.Length == 0)
        {
            return;
        }

        _popup.ShowLoading(_lastX, _lastY, _lastText);
        _ = StartTranslationAsync(_lastText, bypassLanguageCheck: true, _lastX, _lastY);
    }

    private void CopyTranslation()
    {
        var text = _popup?.CurrentTranslation;
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        try
        {
            Clipboard.SetText(text);
            _popup!.SetStatus("已复制");
        }
        catch (Exception ex)
        {
            _popup?.SetHint("复制失败，请重试。");
            _log?.Warn($"复制译文失败：{ex.Message}");
        }
    }

    private void HideCard()
    {
        _translationCts?.Cancel();
        _popup?.HideCard();
    }

    private void OnEnabledToggled()
    {
        if (_config is null || _tray is null || _hook is null)
        {
            return;
        }

        var enabled = _tray.EnabledState;
        _config.Current.Hotkey.Enabled = enabled;
        _hook.Enabled = enabled;
        _config.Save();

        _log?.Info(enabled ? "翻译已启用" : "翻译已暂停");

        if (_settings is { IsVisible: true })
        {
            _settings.Refresh();
        }
    }

    private void OnAutoStartToggled(bool enabled)
    {
        if (_config is null || _autoStart is null)
        {
            return;
        }

        _config.Current.Behavior.AutoStart = enabled;
        try
        {
            _autoStart.Set(enabled, Environment.ProcessPath ?? string.Empty);
            _log?.Info(enabled ? "已写入开机自启" : "已移除开机自启");
        }
        catch (Exception ex)
        {
            _log?.Error("设置开机自启失败", ex);
            _tray?.SetAutoStartState(!enabled);
            _tray?.ShowBalloon("开机自启设置失败", ex.Message);
        }

        _config.Save();
    }

    private void SyncAutoStart()
    {
        if (_config is null || _autoStart is null)
        {
            return;
        }

        try
        {
            // 便携模式不默认写自启；普通模式按配置补齐，保持注册表与配置一致。
            if (!_portable && _config.Current.Behavior.AutoStart && !_autoStart.IsEnabled())
            {
                _autoStart.Enable(Environment.ProcessPath ?? string.Empty);
            }

            if (_autoStart.IsEnabled() != _config.Current.Behavior.AutoStart)
            {
                _config.Current.Behavior.AutoStart = _autoStart.IsEnabled();
                _config.Save();
            }

            _tray?.SetAutoStartState(_autoStart.IsEnabled());
        }
        catch (Exception ex)
        {
            _log?.Error("同步开机自启失败", ex);
        }
    }

    private void RestartElevated()
    {
        if (Elevation.IsCurrentProcessElevated())
        {
            return;
        }

        var exe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exe))
        {
            return;
        }

        var arguments = Environment.GetCommandLineArgs()
            .Skip(1)
            .Where(a => !string.Equals(a, "--restarted-elevated", StringComparison.OrdinalIgnoreCase))
            .Append("--restarted-elevated");

        var startInfo = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = true,
            Verb = "runas",
            Arguments = string.Join(' ', arguments.Select(Quote)),
        };

        try
        {
            Process.Start(startInfo);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            _tray?.ShowBalloon("已取消", "需要管理员授权后才能翻译管理员窗口。");
            return;
        }
        catch (Exception ex)
        {
            _log?.Error("提权重启失败", ex);
            _tray?.ShowBalloon("提权重启失败", ex.Message);
            return;
        }

        _log?.Info("请求以管理员身份重启");
        ExitApplication();
    }

    private static string Quote(string argument)
        => argument.Contains(' ') ? $"\"{argument}\"" : argument;

    private void ShowSettings()
    {
        if (_settings is null || _exiting)
        {
            return;
        }

        _settings.Refresh();

        if (!_settings.IsVisible)
        {
            _settings.Show();
        }

        if (_settings.WindowState == WindowState.Minimized)
        {
            _settings.WindowState = WindowState.Normal;
        }

        _settings.Activate();
    }

    private void OpenLogs()
    {
        try
        {
            if (_log is null)
            {
                return;
            }

            Directory.CreateDirectory(_log.DirectoryPath);
            Process.Start(new ProcessStartInfo
            {
                FileName = _log.DirectoryPath,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            _log?.Error("打开日志目录失败", ex);
        }
    }

    private void ExitApplication()
    {
        if (_exiting)
        {
            return;
        }

        _exiting = true;
        _log?.Info("用户退出");

        _keyboardHook?.Dispose();
        _hook?.Dispose();
        _tray?.Dispose();

        try
        {
            _config?.Save();
        }
        catch (Exception ex)
        {
            _log?.Error("退出时保存配置失败", ex);
        }

        _translationCts?.Cancel();
        _settings?.CloseForShutdown();
        _single?.Dispose();
        _httpClient?.Dispose();
        _log?.Dispose();

        Shutdown();
    }


    private void RenderCardPreview(string pngPath)
    {
        try
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var trace = pngPath + ".steps.txt";
            void Step(string text) => File.AppendAllText(trace, text + Environment.NewLine);

            Step("start");

            var popup = new PopupWindow
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -4000,
                Top = -4000,
                ShowInTaskbar = false,
            };

            Step("created");

            popup.ShowLoading(0, 0, "The quick brown fox jumps over the lazy dog, and this sentence exists only to make the card wide enough.");
            Step("loading");

            popup.ShowTranslation(
                "敏捷的棕色狐狸跳过了懒狗。这句话只是用来把卡片撑得足够宽，顺便看看译文很长时的排版效果。",
                truncated: false,
                echoed: false,
                elapsed: TimeSpan.FromMilliseconds(823));
            Step("translation");

            popup.UpdateLayout();
            Step($"layout {popup.ActualWidth}x{popup.ActualHeight}");

            var width = (int)popup.ActualWidth;
            var height = (int)popup.ActualHeight;
            if (width <= 0 || height <= 0)
            {
                width = 320;
                height = 140;
            }

            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
                width,
                height,
                96,
                96,
                System.Windows.Media.PixelFormats.Pbgra32);

            bitmap.Render(popup);
            Step("rendered");

            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));

            using (var stream = File.Create(pngPath))
            {
                encoder.Save(stream);
            }

            // 再渲染一张加载态：转圈应在译文区中央。
            var loadingPath = Path.Combine(
                Path.GetDirectoryName(pngPath) ?? ".",
                Path.GetFileNameWithoutExtension(pngPath) + "-loading.png");

            popup.ShowLoading(0, 0, "The quick brown fox jumps over the lazy dog.");
            popup.UpdateLayout();
            var loadingBitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
                (int)popup.ActualWidth,
                (int)popup.ActualHeight,
                96,
                96,
                System.Windows.Media.PixelFormats.Pbgra32);
            loadingBitmap.Render(popup);

            var loadingEncoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            loadingEncoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(loadingBitmap));
            using (var loadingStream = File.Create(loadingPath))
            {
                loadingEncoder.Save(loadingStream);
            }

            popup.HideCard();
        }
        catch (Exception ex)
        {
            try
            {
                File.WriteAllText(pngPath + ".error.txt", ex.ToString());
            }
            catch
            {
                // 预览失败不影响其它诊断。
            }
        }

        Shutdown();
    }

    private void FailStartup(Exception exception)
    {
        var detail = $"{exception.GetType().Name}: {exception.Message}";
        try
        {
            File.AppendAllText(
                Path.Combine(AppContext.BaseDirectory, "startup-error.log"),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {detail}{Environment.NewLine}{exception}{Environment.NewLine}");
        }
        catch
        {
            // 连错误日志都写不了时，至少还有弹窗。
        }

        if (_selfTest)
        {
            Shutdown();
            return;
        }

        System.Windows.MessageBox.Show(
            $"SelectTranslate 启动失败：{detail}{Environment.NewLine}{Environment.NewLine}详细信息见程序目录下的 startup-error.log。",
            AppInfo.DisplayName,
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        Shutdown();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _log?.Error("未处理异常（Dispatcher）", e.Exception);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _keyboardHook?.Dispose();
        _hook?.Dispose();
        _tray?.Dispose();
        _single?.Dispose();
        _services?.Dispose();
        _log?.Dispose();
        base.OnExit(e);
    }
}