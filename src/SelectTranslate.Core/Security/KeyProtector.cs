using System.Security.Cryptography;
using System.Text;

namespace SelectTranslate.Core.Security;

/// <summary>
/// 用 DPAPI（CurrentUser 范围）保护 API Key。密文离开当前 Windows 账户不可解密。
/// </summary>
public static class KeyProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SelectTranslate.ApiKey.v1");

    public static string? Protect(string? plainText)
    {
        if (string.IsNullOrEmpty(plainText))
        {
            return null;
        }

        try
        {
            var bytes = Encoding.UTF8.GetBytes(plainText);
            var encrypted = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(encrypted);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    public static string? Unprotect(string? protectedBase64)
    {
        if (string.IsNullOrWhiteSpace(protectedBase64))
        {
            return null;
        }

        try
        {
            var encrypted = Convert.FromBase64String(protectedBase64);
            var bytes = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return null;
        }
    }
}