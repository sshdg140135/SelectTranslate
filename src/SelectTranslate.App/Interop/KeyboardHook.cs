using System.Runtime.InteropServices;

namespace SelectTranslate.App.Interop;

/// <summary>一次按键事件（含修饰键状态）。</summary>
public sealed record KeyStroke(int VirtualKey, bool Ctrl, bool Alt, bool Shift, bool Win, bool IsDown);

/// <summary>
/// 全局低级键盘钩子：监听 Esc 关闭卡片，并支持自定义键盘热键。
/// 独占一个消息泵线程；<see cref="KeyFilter"/> 返回 true 时吞掉该次按键，避免组合键落到前台应用。
/// </summary>
public sealed class KeyboardHook : IDisposable
{
    private readonly NativeMethods.LowLevelKeyboardProc _callback;
    private readonly ManualResetEventSlim _installed = new(false);

    private Thread? _thread;
    private IntPtr _hook;
    private uint _threadId;
    private volatile bool _running;
    private bool _disposed;

    public KeyboardHook()
    {
        _callback = HookCallback;
    }

    /// <summary>Esc 按下。事件在钩子线程触发，处理方必须立即返回。</summary>
    public event EventHandler? EscapePressed;

    /// <summary>按键过滤器：返回 true 表示吞掉该按键。</summary>
    public Func<KeyStroke, bool>? KeyFilter { get; set; }

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
            Name = "SelectTranslate.KeyboardHook",
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

        _hook = NativeMethods.SetWindowsHookExKeyboard(
            NativeMethods.WH_KEYBOARD_LL,
            _callback,
            NativeMethods.GetModuleHandle(null),
            0);

        _installed.Set();

        if (_hook == IntPtr.Zero)
        {
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
        if (nCode >= 0)
        {
            try
            {
                var message = (int)wParam;
                var isDown = message is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN;
                var isUp = message is NativeMethods.WM_KEYUP or NativeMethods.WM_SYSKEYUP;

                if (isDown || isUp)
                {
                    var info = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);

                    if (info.VkCode == NativeMethods.VK_ESCAPE && isDown)
                    {
                        EscapePressed?.Invoke(this, EventArgs.Empty);
                    }
                    else if (info.VkCode != NativeMethods.VK_ESCAPE)
                    {
                        var stroke = new KeyStroke(
                            info.VkCode,
                            IsPressed(NativeMethods.VK_CONTROL),
                            IsPressed(NativeMethods.VK_MENU),
                            IsPressed(NativeMethods.VK_SHIFT),
                            IsPressed(NativeMethods.VK_LWIN) || IsPressed(NativeMethods.VK_RWIN),
                            isDown);

                        if (KeyFilter?.Invoke(stroke) == true)
                        {
                            return new IntPtr(1);
                        }
                    }
                }
            }
            catch (Exception)
            {
                // 绝不让托管异常逃回原生栈。
            }
        }

        return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private static bool IsPressed(int virtualKey)
        => (NativeMethods.GetAsyncKeyState(virtualKey) & 0x8000) != 0;

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
            // 线程未启动，忽略。
        }

        _installed.Dispose();
    }
}