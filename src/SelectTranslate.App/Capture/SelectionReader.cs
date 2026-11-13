using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using SelectTranslate.App.Interop;
using SelectTranslate.Core.Text;

// UseWindowsForms 会引入 WinForms 的同名类型，这里固定用 WPF 的。
using Application = System.Windows.Application;
using Clipboard = System.Windows.Clipboard;
using IDataObject = System.Windows.IDataObject;

namespace SelectTranslate.App.Capture;

/// <summary>
/// 读取当前选区：先用 UI Automation（无副作用），失败时用 Ctrl+C 兜底并还原剪贴板。
/// 只在 UI 线程调用（剪贴板部分要求 STA）。
/// </summary>
public sealed class SelectionReader
{
    private static readonly TimeSpan UiaTimeout = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan ClipboardTimeout = TimeSpan.FromMilliseconds(550);

    public async Task<string?> ReadAsync(IntPtr foregroundWindow, CancellationToken cancellationToken)
    {
        var viaUia = await TryReadUiaAsync(foregroundWindow, cancellationToken);
        var cleaned = TextSanitizer.Normalize(viaUia);
        if (cleaned is not null)
        {
            return cleaned;
        }

        return TextSanitizer.Normalize(ReadClipboardSelection());
    }

    private static async Task<string?> TryReadUiaAsync(IntPtr hwnd, CancellationToken cancellationToken)
    {
        if (hwnd == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            // UIA 偶发卡死，放到线程池并设超时；超时后不等待它。
            var task = Task.Run(() => ReadUia(hwnd), CancellationToken.None);
            return await task.WaitAsync(UiaTimeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? ReadUia(IntPtr hwnd)
    {
        AutomationElement? focused;
        try
        {
            focused = AutomationElement.FocusedElement;
        }
        catch (Exception)
        {
            return null;
        }

        if (focused is null)
        {
            return null;
        }

        try
        {
            var isPassword = focused.GetCurrentPropertyValue(AutomationElement.IsPasswordProperty);
            if (isPassword is true)
            {
                return null;
            }
        }
        catch (Exception)
        {
            // 取不到属性就继续尝试。
        }

        if (!focused.TryGetCurrentPattern(TextPatternPattern, out var patternObject)
            || patternObject is null)
        {
            return null;
        }

        // TextPatternRange 类型在 .NET 10 的引用程序集里不可直接用，改用反射取选区文本。
        if (patternObject.GetType().InvokeMember(
                "GetSelection",
                BindingFlags.InvokeMethod,
                binder: null,
                patternObject,
                args: null) is not Array ranges || ranges.Length == 0)
        {
            return null;
        }

        var parts = new List<string>(ranges.Length);
        foreach (var range in ranges)
        {
            if (range is null)
            {
                continue;
            }

            try
            {
                var text = range.GetType().InvokeMember(
                    "GetText",
                    BindingFlags.InvokeMethod,
                    binder: null,
                    range,
                    args: new object[] { 4000 }) as string;

                if (!string.IsNullOrEmpty(text))
                {
                    parts.Add(text);
                }
            }
            catch (Exception)
            {
                // 单个 range 失败不影响其它 range。
            }
        }

        return parts.Count == 0 ? null : string.Join('\n', parts);
    }

    /// <summary>UIA_TextPatternId。</summary>
    private static readonly AutomationPattern TextPatternPattern = AutomationPattern.LookupById(10014);

    private static string? ReadClipboardSelection()
    {
        if (!Application.Current.Dispatcher.CheckAccess())
        {
            return Application.Current.Dispatcher.Invoke(ReadClipboardSelection);
        }

        string? originalText = null;
        IDataObject? originalData = null;
        try
        {
            if (Clipboard.ContainsText())
            {
                originalText = Clipboard.GetText();
            }

            originalData = Clipboard.GetDataObject();
        }
        catch (Exception)
        {
            // 剪贴板被别的进程占用时继续尝试复制，最多拿不到。
        }

        var sequence = NativeMethods.GetClipboardSequenceNumber();
        SendCopyShortcut();

        string? captured = null;
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds < ClipboardTimeout.TotalMilliseconds)
        {
            Thread.Sleep(25);

            if (NativeMethods.GetClipboardSequenceNumber() == sequence)
            {
                continue;
            }

            try
            {
                if (Clipboard.ContainsText())
                {
                    captured = Clipboard.GetText();
                }
            }
            catch (Exception)
            {
                // 忽略瞬时占用。
            }

            if (!string.IsNullOrWhiteSpace(captured))
            {
                break;
            }
        }

        RestoreClipboard(originalText, originalData);
        return captured;
    }

    private static void SendCopyShortcut()
    {
        // keybd_event 已过时但足够稳定；按下 Ctrl+C 后立即抬起。
        NativeMethods.keybd_event((byte)NativeMethods.VK_CONTROL, 0, 0, UIntPtr.Zero);
        NativeMethods.keybd_event(NativeMethods.VK_C, 0, 0, UIntPtr.Zero);
        Thread.Sleep(10);
        NativeMethods.keybd_event(NativeMethods.VK_C, 0, NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);
        NativeMethods.keybd_event((byte)NativeMethods.VK_CONTROL, 0, NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    private static void RestoreClipboard(string? originalText, IDataObject? originalData)
    {
        try
        {
            if (!string.IsNullOrEmpty(originalText))
            {
                Clipboard.SetText(originalText);
            }
            else if (originalData is not null)
            {
                Clipboard.SetDataObject(originalData, true);
            }
            else
            {
                Clipboard.Clear();
            }
        }
        catch (Exception)
        {
            // 还原失败不影响本次取词。
        }
    }
}