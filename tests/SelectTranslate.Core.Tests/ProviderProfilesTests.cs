using SelectTranslate.Core.Configuration;

namespace SelectTranslate.Core.Tests;

public sealed class ProviderProfilesTests
{
    private static string NewDirectory()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "test-data", "providers", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void Normalize_CreatesDefaultProvider_WhenConfigIsEmpty()
    {
        var config = new AppConfig();
        config.Normalize();

        Assert.Single(config.Providers);
        Assert.Equal(config.Providers[0].Id, config.ActiveProviderId);
        Assert.Same(config.Providers[0], config.ActiveProvider);
        Assert.Equal("openai-compatible", config.Api.ProviderType);
        Assert.Equal(2, config.SchemaVersion);
    }

    [Fact]
    public void Normalize_AssignsIdsAndNames_AndKeepsUniqueIds()
    {
        var config = new AppConfig
        {
            Providers =
            [
                new ApiConfig { Id = "same", Name = "" },
                new ApiConfig { Id = "same", Name = "第二个" },
                new ApiConfig { Id = "", Name = "" },
            ],
        };

        config.Normalize();

        Assert.Equal(3, config.Providers.Count);
        Assert.Equal(3, config.Providers.Select(p => p.Id).Distinct().Count());
        Assert.All(config.Providers, p => Assert.False(string.IsNullOrWhiteSpace(p.Name)));
        Assert.Equal("第二个", config.Providers[1].Name);
    }

    [Fact]
    public void SetActiveProvider_SwitchesActiveProfile()
    {
        var config = new AppConfig
        {
            Providers =
            [
                new ApiConfig { Id = "a", Name = "A", Model = "model-a" },
                new ApiConfig { Id = "b", Name = "B", Model = "model-b" },
            ],
            ActiveProviderId = "a",
        };
        config.Normalize();

        Assert.Equal("model-a", config.Api.Model);

        Assert.True(config.SetActiveProvider("b"));
        Assert.Equal("b", config.ActiveProviderId);
        Assert.Equal("model-b", config.Api.Model);

        Assert.False(config.SetActiveProvider("missing"));
        Assert.Equal("b", config.ActiveProviderId);
    }

    [Fact]
    public void Load_MigratesLegacySingleApiConfig()
    {
        var dir = NewDirectory();
        var path = Path.Combine(dir, "config.json");

        File.WriteAllText(path, """
            {
              "schemaVersion": 1,
              "api": {
                "providerType": "openai-compatible",
                "baseUrl": "https://old.example.com/v1",
                "model": "old-model",
                "temperature": 0.5
              },
              "target": { "lang": "ja" }
            }
            """);

        var service = new ConfigService(dir);
        service.Load();

        Assert.Equal(2, service.Current.SchemaVersion);
        Assert.Single(service.Current.Providers);
        Assert.Equal("https://old.example.com/v1", service.Current.Api.BaseUrl);
        Assert.Equal("old-model", service.Current.Api.Model);
        Assert.Equal(0.5, service.Current.Api.Temperature);
        Assert.Equal("ja", service.Current.Target.Lang);
        Assert.Null(service.Current.LegacyApi);
    }

    [Fact]
    public void SaveAndLoad_KeepsMultipleProviders_AndActiveSelection()
    {
        var dir = NewDirectory();
        var service = new ConfigService(dir);
        service.Load();

        var first = service.Current.Providers[0];
        first.Id = "one";
        first.Name = "DeepSeek";
        first.Model = "deepseek-chat";

        service.Current.Providers.Add(new ApiConfig
        {
            Id = "two",
            Name = "本地网关",
            BaseUrl = "http://127.0.0.1:8080/v1",
            Model = "qwen",
        });

        Assert.True(service.Current.SetActiveProvider("two"));
        service.Save();
        Assert.Null(service.LastError);

        var reloaded = new ConfigService(dir);
        reloaded.Load();

        Assert.Equal(2, reloaded.Current.Providers.Count);
        Assert.Equal("two", reloaded.Current.ActiveProviderId);
        Assert.Equal("qwen", reloaded.Current.Api.Model);
        Assert.Equal("本地网关", reloaded.Current.Api.Name);
        Assert.Equal("DeepSeek", reloaded.Current.Providers.First(p => p.Id == "one").Name);
    }

    [Fact]
    public void ApiKey_IsPerProvider()
    {
        var dir = NewDirectory();
        var service = new ConfigService(dir);
        service.Load();

        var first = service.Current.Providers[0];
        var second = new ApiConfig { Id = "b", Name = "B" };
        service.Current.Providers.Add(second);

        service.SetApiKey(first, "key-a");
        service.SetApiKey(second, "key-b");

        Assert.Equal("key-a", service.GetApiKey(first));
        Assert.Equal("key-b", service.GetApiKey(second));

        // 当前档案切换后，无参重载跟着走。
        Assert.True(service.Current.SetActiveProvider("b"));
        Assert.Equal("key-b", service.GetApiKey());
    }
}