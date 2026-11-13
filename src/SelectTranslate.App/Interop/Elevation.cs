using System.Runtime.InteropServices;

namespace SelectTranslate.App.Interop;

/// <summary>
/// 判断前台窗口是否以管理员身份运行。提权窗口无法被普通权限进程取词（UIPI），
/// 与其静默失败，不如给出明确提示（AC-27）。
/// </summary>
public static class Elevation
{
    public static bool IsCurrentProcessElevated() => IsProcessElevated(Environment.ProcessId);

    public static bool IsWindowElevated(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        var threadId = NativeMethods.GetWindowThreadProcessId(hwnd, out var processId);
        return threadId != 0 && processId != 0 && IsProcessElevated(processId);
    }

    private static bool IsProcessElevated(int processId)
    {
        var processHandle = NativeMethods.OpenProcess(
            NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION,
            inheritHandle: false,
            processId);

        if (processHandle == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            if (!NativeMethods.OpenProcessToken(processHandle, NativeMethods.TOKEN_QUERY, out var token))
            {
                return false;
            }

            try
            {
                var size = Marshal.SizeOf<NativeMethods.TOKEN_ELEVATION>();
                var buffer = Marshal.AllocHGlobal(size);

                try
                {
                    if (!NativeMethods.GetTokenInformation(
                            token,
                            NativeMethods.TokenElevationClass,
                            buffer,
                            size,
                            out _))
                    {
                        return false;
                    }

                    var elevation = Marshal.PtrToStructure<NativeMethods.TOKEN_ELEVATION>(buffer);
                    return elevation.TokenIsElevated != 0;
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            finally
            {
                NativeMethods.CloseHandle(token);
            }
        }
        finally
        {
            NativeMethods.CloseHandle(processHandle);
        }
    }
}