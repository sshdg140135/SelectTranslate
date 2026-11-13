using Microsoft.Win32;

namespace SelectTranslate.Core.Startup;

/// <summary>
/// 开机自启：写 HKCU\Software\Microsoft\Windows\CurrentVersion\Run，无需管理员权限。
/// </summary>
public sealed class AutoStartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SelectTranslate";

    public bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) is string value && value.Length > 0;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }

    public void Enable(string exePath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("无法打开开机自启注册表项。");

        key.SetValue(ValueName, $"\"{exePath}\"", RegistryValueKind.String);
    }

    public void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    public void Set(bool enabled, string exePath)
    {
        if (enabled)
        {
            Enable(exePath);
        }
        else
        {
            Disable();
        }
    }
}