using System.Drawing;
using System.Windows.Forms;

namespace SelectTranslate.App.Shell;

/// <summary>
/// 托盘常驻：菜单项与事件。UI 线程上创建与释放。
/// </summary>
public sealed class TrayService : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _enabledItem;
    private readonly ToolStripMenuItem _autoStartItem;
    private readonly ToolStripMenuItem _elevateItem;
    private bool _disposed;

    public TrayService(string tooltip, bool enabled, bool autoStart)
    {
        _enabledItem = new ToolStripMenuItem("启用翻译")
        {
            CheckOnClick = true,
            Checked = enabled,
        };
        _enabledItem.Click += (_, _) => EnabledToggled?.Invoke();

        _autoStartItem = new ToolStripMenuItem("开机自启")
        {
            CheckOnClick = true,
            Checked = autoStart,
        };
        _autoStartItem.Click += (_, _) => AutoStartToggled?.Invoke(_autoStartItem.Checked);

        var menu = new ContextMenuStrip();
        menu.Items.Add(_enabledItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("打开设置", null, (_, _) => OpenSettingsRequested?.Invoke()));

        _elevateItem = new ToolStripMenuItem("以管理员身份重启（管理员窗口取词）");
        _elevateItem.Click += (_, _) => ElevateRequested?.Invoke();
        menu.Items.Add(_elevateItem);
        menu.Items.Add(_autoStartItem);
        menu.Items.Add(new ToolStripMenuItem("查看日志", null, (_, _) => OpenLogsRequested?.Invoke()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("退出", null, (_, _) => ExitRequested?.Invoke()));

        _icon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = tooltip,
            Visible = true,
            ContextMenuStrip = menu,
        };
        _icon.DoubleClick += (_, _) => OpenSettingsRequested?.Invoke();
    }

    public event Action? EnabledToggled;

    public event Action? OpenSettingsRequested;

    public event Action? ElevateRequested;

    public event Action? OpenLogsRequested;

    public event Action? ExitRequested;

    public event Action<bool>? AutoStartToggled;

    /// <summary>菜单当前勾选状态，供外部读取。</summary>
    public bool EnabledState => _enabledItem.Checked;

    /// <summary>由程序内部同步状态，不触发 AutoStartToggled。</summary>
    public void SetEnabledState(bool enabled) => _enabledItem.Checked = enabled;

    public void SetAutoStartState(bool enabled) => _autoStartItem.Checked = enabled;

    /// <summary>已经是管理员权限时禁用重启动菜单项。</summary>
    public void SetElevatedState(bool elevated)
    {
        _elevateItem.Enabled = !elevated;
        _elevateItem.Text = elevated ? "已以管理员身份运行" : "以管理员身份重启（管理员窗口取词）";
    }

    public void ShowBalloon(string title, string text)
    {
        _icon.BalloonTipTitle = title;
        _icon.BalloonTipText = text;
        _icon.ShowBalloonTip(2000);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
    }
}