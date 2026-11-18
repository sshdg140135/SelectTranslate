using SelectTranslate.Core.Configuration;

namespace SelectTranslate.Core.Tests;

public sealed class ConfigServiceTests : IDisposable
{
    // 用测试程序目录下的临时目录：不依赖系统 TEMP 的权限，跑完即删。
    private readonly string _directory = Path.Combine(
        AppContext.BaseDirectory,
        "test-data",
        Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // 测试清理失败不影响结果。
        }
    }

    [Fact]
    public void Load_MissingFile_UsesDocumentedDefaults()
    {
        var service = new ConfigService(_directory);

        service.Load();

        Assert.Null(service.LastError);
        Assert.Equal(2, service.Current.SchemaVersion);
        Assert.Equal("openai-compatible", service.Current.Api.ProviderType);
        Assert.Equal("https://api.deepseek.com/v1", service.Current.Api.BaseUrl);
        Assert.Equal("deepseek-chat", service.Current.Api.Model);
        Assert.Equal(0.2, service.Current.Api.Temperature);
        Assert.Equal(30_000, service.Current.Api.TimeoutMs);
        Assert.True(service.Current.Api.Stream);
        Assert.Equal("zh-CN", service.Current.Target.Lang);
        Assert.Equal("Middle", service.Current.Hotkey.Mouse);
        Assert.True(service.Current.Hotkey.Enabled);
        Assert.Equal(8_000, service.Current.Behavior.MaxChars);
        Assert.True(service.Current.Behavior.AutoStart);
        Assert.Equal(320, service.Current.Ui.Width);
        Assert.Equal(14, service.Current.Ui.FontSize);
        Assert.Equal(0, service.Current.Ui.AutoCloseSec);
    }

    [Fact]
    public void SaveAndLoad_RoundTripsUserValues()
    {
        var service = new ConfigService(_directory);
        service.Load();

        service.Current.Api.BaseUrl = "https://example.com/v1";
        service.Current.Api.Model = "gpt-4o-mini";
        service.Current.Target.Lang = "ja";
        service.Current.Ui.Width = 480;
        service.Current.Behavior.Blacklist = ["game.exe"];
        service.Save();

        Assert.Null(service.LastError);

        var reloaded = new ConfigService(_directory);
        reloaded.Load();

        Assert.Null(reloaded.LastError);
        Assert.Equal("https://example.com/v1", reloaded.Current.Api.BaseUrl);
        Assert.Equal("gpt-4o-mini", reloaded.Current.Api.Model);
        Assert.Equal("ja", reloaded.Current.Target.Lang);
        Assert.Equal(480, reloaded.Current.Ui.Width);
        Assert.Contains("game.exe", reloaded.Current.Behavior.Blacklist);
    }

    [Theory]
    [InlineData(9999, 10_000, 1, 5.0, 560, 10_000, 100, 1.0)]
    [InlineData(100, 1, 999_999, -1.0, 300, 10_000, 100_000, 0.0)]
    public void Normalize_ClampsOutOfRangeValues(
        int width,
        int timeoutMs,
        int maxChars,
        double temperature,
        int expectedWidth,
        int expectedTimeout,
        int expectedMaxChars,
        double expectedTemperature)
    {
        var config = new AppConfig
        {
            Ui = new UiConfig { Width = width },
            Providers = [new ApiConfig { TimeoutMs = timeoutMs, Temperature = temperature }],
            Behavior = new BehaviorConfig { MaxChars = maxChars },
        };

        config.Normalize();

        Assert.Equal(expectedWidth, config.Ui.Width);
        Assert.Equal(expectedTimeout, config.Api.TimeoutMs);
        Assert.Equal(expectedMaxChars, config.Behavior.MaxChars);
        Assert.Equal(expectedTemperature, config.Api.Temperature, precision: 6);
    }

    [Fact]
    public void Load_CorruptJson_BacksUpFileAndFallsBackToDefaults()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "config.json");
        File.WriteAllText(path, "{ this is not json");

        var service = new ConfigService(_directory);
        service.Load();

        Assert.NotNull(service.LastError);
        Assert.Equal("deepseek-chat", service.Current.Api.Model);
        Assert.False(File.Exists(path));
        Assert.Single(Directory.GetFiles(_directory, "config.bad-*.json"));
    }

    [Fact]
    public void ApiKey_IsEncryptedAtRest_AndRoundTrips()
    {
        var service = new ConfigService(_directory);
        service.Load();
        service.SetApiKey("sk-unit-test-key");
        service.Save();

        Assert.Null(service.LastError);

        var json = File.ReadAllText(service.FilePath);
        Assert.DoesNotContain("sk-unit-test-key", json, StringComparison.Ordinal);

        var reloaded = new ConfigService(_directory);
        reloaded.Load();

        Assert.Equal("sk-unit-test-key", reloaded.GetApiKey());
    }

    [Fact]
    public void ApiKey_EmptyInput_StoresNull()
    {
        var service = new ConfigService(_directory);
        service.Load();
        service.SetApiKey(null);

        Assert.Null(service.Current.Api.ApiKeyProtected);
        Assert.Null(service.GetApiKey());
    }
}