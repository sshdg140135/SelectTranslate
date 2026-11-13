using System.Runtime.InteropServices;
using SelectTranslate.App.Interop;

namespace SelectTranslate.App.Shell;

/// <summary>
/// 检测当前进程是否以低完整性运行。低完整性进程无法写用户目录、无法显示托盘图标、
/// 也收不到其他程序的中键输入——出现这些症状时先看这里。
/// </summary>
public static class IntegrityLevel
{
    private const uint TokenQuery = 0x0008;
    private const int TokenIntegrityLevel = 25;
    private const int SecurityMandatoryLowRid = 0x1000;

    public static bool IsCurrentProcessLow()
    {
        if (!NativeMethods.OpenProcessToken(NativeMethods.GetCurrentProcess(), TokenQuery, out var token))
        {
            return false;
        }

        try
        {
            NativeMethods.GetTokenInformation(token, TokenIntegrityLevel, IntPtr.Zero, 0, out var size);
            if (size <= 0)
            {
                return false;
            }

            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                if (!NativeMethods.GetTokenInformation(token, TokenIntegrityLevel, buffer, size, out _))
                {
                    return false;
                }

                var label = Marshal.PtrToStructure<NativeMethods.TOKEN_MANDATORY_LABEL>(buffer);
                if (label.Label.Sid == IntPtr.Zero)
                {
                    return false;
                }

                var count = Marshal.ReadByte(NativeMethods.GetSidSubAuthorityCount(label.Label.Sid));
                if (count == 0)
                {
                    return false;
                }

                var rid = Marshal.ReadInt32(
                    NativeMethods.GetSidSubAuthority(label.Label.Sid, count - 1));

                return rid <= SecurityMandatoryLowRid;
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
}