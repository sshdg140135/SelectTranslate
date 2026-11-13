namespace SelectTranslate.Core.Configuration;

public static class ConfigPaths
{
    public const string AppFolderName = "SelectTranslate";

    /// <summary>
    /// 解析配置与日志的根目录。普通模式在 %APPDATA%\SelectTranslate，便携模式在 exe 同目录的 data\。
    /// </summary>
    public static string ResolveBaseDirectory(bool portable, string? exeDirectory = null)
    {
        if (portable)
        {
            return ResolvePortableDirectory(exeDirectory);
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppFolderName);
    }

    public static string ResolvePortableDirectory(string? exeDirectory = null)
    {
        var root = string.IsNullOrWhiteSpace(exeDirectory) ? AppContext.BaseDirectory : exeDirectory;
        return Path.Combine(root, "data");
    }

    /// <summary>
    /// 返回第一个真正可写的目录：首选目录 → %LOCALAPPDATA%\SelectTranslate → exe\data。
    /// 受限账户、只读安装目录或低完整性进程下，首选目录可能不可写。
    /// 全部失败时抛 IOException，由调用方提示用户。
    /// </summary>
    public static string EnsureWritableBase(string preferredDirectory, string? exeDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(preferredDirectory);

        var candidates = new List<string> { preferredDirectory };

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            candidates.Add(Path.Combine(localAppData, AppFolderName));
        }

        candidates.Add(ResolvePortableDirectory(exeDirectory));

        var tried = new List<string>();
        foreach (var candidate in candidates)
        {
            var full = Path.GetFullPath(candidate);
            if (tried.Contains(full, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            tried.Add(full);

            if (TryPrepare(full))
            {
                return full;
            }
        }

        throw new IOException($"没有可写的配置目录，已尝试：{string.Join("；", tried)}");
    }

    private static bool TryPrepare(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);

            // 目录存在不代表可写（例如低完整性进程对用户目录只有读权限），实际写一次才算数。
            var probe = Path.Combine(directory, ".write-probe");
            File.WriteAllText(probe, "1");
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }
}