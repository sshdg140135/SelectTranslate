using System.Text.Json;
using System.Text.Json.Serialization;
using SelectTranslate.Core.Security;

namespace SelectTranslate.Core.Configuration;

/// <summary>
/// 配置读写：默认值、原子写盘、损坏文件备份、Key 的 DPAPI 封装。
/// </summary>
public sealed class ConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public ConfigService(string baseDirectory)
    {
        BaseDirectory = baseDirectory;
        FilePath = Path.Combine(baseDirectory, "config.json");
    }

    public string BaseDirectory { get; }

    public string FilePath { get; }

    public AppConfig Current { get; private set; } = new();

    /// <summary>最近一次加载/保存的失败原因；成功后为 null。</summary>
    public string? LastError { get; private set; }

    public void Load()
    {
        LastError = null;

        try
        {
            if (!File.Exists(FilePath))
            {
                Current = new AppConfig();
                Current.Normalize();
                return;
            }

            var json = File.ReadAllText(FilePath);
            var loaded = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? new AppConfig();
            loaded.Normalize();
            Current = loaded;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            LastError = ex.Message;
            BackupBrokenFile();
            Current = new AppConfig();
            Current.Normalize();
        }
    }

    public void Save()
    {
        LastError = null;

        try
        {
            Current.Normalize();
            Directory.CreateDirectory(BaseDirectory);

            var json = JsonSerializer.Serialize(Current, JsonOptions);
            var tempPath = FilePath + ".tmp";
            File.WriteAllText(tempPath, json);

            // 同卷内的覆盖式移动，避免写到一半断电留下半截配置。
            File.Move(tempPath, FilePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LastError = ex.Message;
        }
    }

    public string? GetApiKey() => KeyProtector.Unprotect(Current.Api.ApiKeyProtected);

    public void SetApiKey(string? plainText)
        => Current.Api.ApiKeyProtected = KeyProtector.Protect(plainText);

    private void BackupBrokenFile()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return;
            }

            var backup = Path.Combine(BaseDirectory, $"config.bad-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            File.Move(FilePath, backup, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 备份失败不影响以默认值启动。
        }
    }
}