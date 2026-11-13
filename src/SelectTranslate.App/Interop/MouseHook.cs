using System.Runtime.InteropServices;

namespace SelectTranslate.App.Interop;

public sealed class MouseTriggerEventArgs(int x, int y) : EventArgs
{
    public int X { get; } = x;

    public int Y { get; } = y;
}

/// <summary>
/// 全局低级鼠标钩子。独占一个带消息泵的线程，回调内只做判断与投递，绝不阻塞。
/// </summary>
public sealed class MouseHook : IDisposable
{
    private readonly NativeMethods.LowLevelMouseProc _callback;
    private readonly ManualResetEventSlim _installed = new(false);

    private Thread? _thread;
    private IntPtr _hook;
    private uint _threadId;
    private volatile bool _running;
    private volatile bool _swallowNextUp;
    private volatile bool _enabled = true;
    private bool _disposed;

    public MouseHook()
    {
        // 必须保住委托实例，否则 GC 后会回调到已释放的地址。
        _callback = HookCallback;
    }

    /// <summary>中键按下且未被旁路时触发。事件在钩子线程上同步触发，处理方必须立即返回。</summary>
    public event EventHandler<MouseTriggerEventArgs>? MiddlePressed;

    /// <summary>诊断信息（钩子是否收到中键、安装失败原因等），由宿主决定是否写日志。</summary>
    public event Action<string>? Diagnostic;

    /// <summary>左键按下（不拦截，仅通知），用于点击卡片外部时关闭卡片。</summary>
    public event EventHandler<MouseTriggerEventArgs>? LeftButtonDown;

    /// <summary>false 时所有输入原样放行（暂停翻译）。</summary>
    public bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    public bool IsRunning => _running;

    public bool Start()
    {
        if (_running)
        {
            return true;
        }

        _installed.Reset();
        _running = true;
        _thread = new Thread(ThreadMain)
        {
            IsBackground = true,
            Name = "SelectTranslate.MouseHook",
        };
        _thread.Start();

        if (!_installed.Wait(TimeSpan.FromSeconds(3)))
        {
            _running = false;
            return false;
        }

        return _hook != IntPtr.Zero;
    }

    private void ThreadMain()
    {
        _threadId = NativeMethods.GetCurrentThreadId();

        // 全局低级钩子：hMod 传当前模块即可，dwThreadId 为 0 表示全局。
        _hook = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_MOUSE_LL,
            _callback,
            NativeMethods.GetModuleHandle(null),
            0);

        _installed.Set();

        if (_hook == IntPtr.Zero)
        {
            Diagnostic?.Invoke($"鼠标钩子安装失败：err={Marshal.GetLastWin32Error()}");
            return;
        }

        while (_running && NativeMethods.GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            NativeMethods.TranslateMessage(ref msg);
            NativeMethods.DispatchMessage(ref msg);
        }

        if (_hook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            return HookCallbackCore(nCode, wParam, lParam);
        }
        catch (Exception)
        {
            // 钩子回调里绝不能让托管异常逃回原生栈：那会直接终止进程。
            return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
        }
    }

    private IntPtr HookCallbackCore(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0)
        {
            return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        var message = (int)wParam;

        if (message == NativeMethods.WM_LBUTTONDOWN)
        {
            var leftInfo = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
            LeftButtonDown?.Invoke(this, new MouseTriggerEventArgs(leftInfo.Pt.X, leftInfo.Pt.Y));
            return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        if (message == NativeMethods.WM_MBUTTONDOWN)
        {
            var ctrlDown = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_CONTROL) & 0x8000) != 0;
            Diagnostic?.Invoke($"中键按下：enabled={_enabled}，ctrl={ctrlDown}");

            if (!_enabled || ctrlDown)
            {
                _swallowNextUp = false;
                return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
            }

            var info = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
            _swallowNextUp = true;
            MiddlePressed?.Invoke(this, new MouseTriggerEventArgs(info.Pt.X, info.Pt.Y));

            // 吞掉按下，浏览器自动滚动等原生行为不再发生。
            return new IntPtr(1);
        }

        if (message == NativeMethods.WM_MBUTTONUP && _swallowNextUp)
        {
            _swallowNextUp = false;
            return new IntPtr(1);
        }

        return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _running = false;

        if (_threadId != 0)
        {
            NativeMethods.PostThreadMessage(_threadId, NativeMethods.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        }

        try
        {
            _thread?.Join(TimeSpan.FromSeconds(2));
        }
        catch (ThreadStateException)
        {
            // 线程尚未启动，忽略。
        }

        _installed.Dispose();
    }
}