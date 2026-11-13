using SelectTranslate.Core.Configuration;
using SelectTranslate.Core.Translation;

namespace SelectTranslate.Core.Tests;

public sealed class TranslationServiceTests
{
    private static AppConfig Config(string target = "zh-CN", int maxChars = 8_000)
    {
        var config = new AppConfig();
        config.Target.Lang = target;
        config.Behavior.MaxChars = maxChars;
        config.Api.ProviderType = "stub";
        return config;
    }

    private static (TranslationService Service, StubProvider Provider) Create(
        AppConfig? config = null,
        params TranslationException[] failures)
    {
        var provider = new StubProvider(failures);
        var registry = new ProviderRegistry();
        registry.Register(provider);
        config ??= Config();
        return (new TranslationService(registry, () => config), provider);
    }

    [Fact]
    public async Task TranslateAsync_ShortCircuits_WhenTextAlreadyTargetLanguage()
    {
        var (service, provider) = Create();

        var outcome = await service.TranslateAsync("这是中文句子。", false, null, CancellationToken.None);

        Assert.True(outcome.SameAsTarget);
        Assert.Equal(0, provider.Calls);
        Assert.Equal("zh", outcome.DetectedLanguage);
    }

    [Fact]
    public async Task TranslateAsync_Translates_WhenBypassingLanguageCheck()
    {
        var (service, _) = Create();

        var outcome = await service.TranslateAsync("这是中文句子。", true, null, CancellationToken.None);

        Assert.False(outcome.SameAsTarget);
        Assert.Equal("译文:这是中文句子。", outcome.Text);
    }

    [Fact]
    public async Task TranslateAsync_TruncatesLongText_AndReportsFlag()
    {
        var config = Config(maxChars: 100);
        var (service, provider) = Create(config);
        var text = new string('a', 150);

        var outcome = await service.TranslateAsync(text, false, null, CancellationToken.None);

        Assert.True(outcome.Truncated);
        Assert.Equal(100, provider.LastRequest!.Text.Length);
    }

    [Fact]
    public async Task TranslateAsync_RetriesOnce_OnServerError()
    {
        var (service, provider) = Create(
            null,
            new TranslationException(TranslationErrorKind.Internal, "boom"));

        var outcome = await service.TranslateAsync("hello", false, null, CancellationToken.None);

        Assert.Equal(2, provider.Calls);
        Assert.Equal("译文:hello", outcome.Text);
    }

    [Fact]
    public async Task TranslateAsync_DoesNotRetry_OnAuthError()
    {
        var (service, provider) = Create(
            null,
            new TranslationException(TranslationErrorKind.Auth, "bad key"));

        var error = await Assert.ThrowsAsync<TranslationException>(
            () => service.TranslateAsync("hello", false, null, CancellationToken.None));

        Assert.Equal(TranslationErrorKind.Auth, error.Kind);
        Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public async Task TranslateAsync_ForwardsChunks()
    {
        var config = Config();
        config.Api.ProviderType = "stream";
        var provider = new StreamingProvider();
        var registry = new ProviderRegistry();
        registry.Register(provider);
        var service = new TranslationService(registry, () => config);

        var chunks = new List<string>();
        var progress = new Progress<TranslationChunk>(chunk => chunks.Add(chunk.Text));

        var outcome = await service.TranslateAsync("hello", false, progress, CancellationToken.None);
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.Equal("hel", outcome.Text[..3]);
        Assert.NotEmpty(chunks);
    }

    [Fact]
    public async Task TranslateAsync_RetriesWithForcePrompt_WhenModelEchoesOriginal()
    {
        var config = Config();
        config.Api.ProviderType = "echo";
        var provider = new EchoProvider(resolveOnSecondCall: false);
        var registry = new ProviderRegistry();
        registry.Register(provider);
        var service = new TranslationService(registry, () => config);

        var outcome = await service.TranslateAsync(
            "buildBody ExtractContent",
            false,
            null,
            CancellationToken.None);

        Assert.Equal(2, provider.Calls);
        Assert.True(outcome.Echoed);
        Assert.Contains("不要原样返回", provider.LastSystemPrompt);
        Assert.Equal("buildBody ExtractContent", outcome.Text);
    }

    [Fact]
    public async Task TranslateAsync_AcceptsTranslation_FromAntiEchoRetry()
    {
        var config = Config();
        config.Api.ProviderType = "echo";
        var provider = new EchoProvider(resolveOnSecondCall: true);
        var registry = new ProviderRegistry();
        registry.Register(provider);
        var service = new TranslationService(registry, () => config);

        var outcome = await service.TranslateAsync(
            "buildBody ExtractContent",
            false,
            null,
            CancellationToken.None);

        Assert.Equal(2, provider.Calls);
        Assert.False(outcome.Echoed);
        Assert.Equal("构建请求体；提取内容", outcome.Text);
    }

    [Fact]
    public void IsEcho_IgnoresWhitespaceButNotContent()
    {
        Assert.True(TranslationService.IsEcho("buildBody  ExtractContent", "buildBody\nExtractContent"));
        Assert.False(TranslationService.IsEcho("buildBody", "构建请求体"));
    }

    private sealed class StubProvider(params TranslationException[] failures) : ITranslationProvider
    {
        private int _remainingFailures = failures.Length;

        public int Calls { get; private set; }

        public TranslationRequest? LastRequest { get; private set; }

        public string Id => "stub";

        public string DisplayName => "Stub";

        public ProviderCapabilities Capabilities { get; } = new(false, 8_000);

        public Task TranslateAsync(
            TranslationRequest request,
            IProgress<TranslationChunk>? onChunk,
            CancellationToken cancellationToken)
        {
            Calls++;
            LastRequest = request;

            if (_remainingFailures > 0)
            {
                _remainingFailures--;
                throw failures[^(_remainingFailures + 1)];
            }

            onChunk?.Report(new TranslationChunk($"译文:{request.Text}", true));
            return Task.CompletedTask;
        }
    }

    private sealed class EchoProvider(bool resolveOnSecondCall) : ITranslationProvider
    {
        public int Calls { get; private set; }

        public string? LastSystemPrompt { get; private set; }

        public string Id => "echo";

        public string DisplayName => "Echo";

        public ProviderCapabilities Capabilities { get; } = new(false, 8_000);

        public Task TranslateAsync(
            TranslationRequest request,
            IProgress<TranslationChunk>? onChunk,
            CancellationToken cancellationToken)
        {
            Calls++;
            LastSystemPrompt = request.SystemPrompt;

            var text = resolveOnSecondCall && Calls >= 2
                ? "构建请求体；提取内容"
                : request.Text;

            onChunk?.Report(new TranslationChunk(text, true));
            return Task.CompletedTask;
        }
    }

    private sealed class StreamingProvider : ITranslationProvider
    {
        public string Id => "stream";

        public string DisplayName => "Stream";

        public ProviderCapabilities Capabilities { get; } = new(true, 8_000);

        public Task TranslateAsync(
            TranslationRequest request,
            IProgress<TranslationChunk>? onChunk,
            CancellationToken cancellationToken)
        {
            onChunk?.Report(new TranslationChunk("hel", false));
            onChunk?.Report(new TranslationChunk("lo", true));
            return Task.CompletedTask;
        }
    }
}