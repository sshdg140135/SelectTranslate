using System.Net;
using System.Net.Http.Headers;
using System.Text;
using SelectTranslate.Core.Translation;

namespace SelectTranslate.Core.Tests;

public sealed class OpenAiCompatibleProviderTests
{
    private static string? _lastBody;

    private static ApiSettings Settings(
        string? key = "sk-test",
        string extraBody = "",
        string model = "test-model",
        string thinkingMode = "default") => new(
        BaseUrl: "https://api.example.com/v1",
        ApiKey: key,
        Model: model,
        Temperature: 0.2,
        TimeoutMs: 5_000,
        ConnectTimeoutMs: 3_000,
        SystemPrompt: "translate",
        ExtraHeaders: new Dictionary<string, string>(),
        Stream: false,
        MaxTokens: null,
        ExtraBodyJson: extraBody,
        ThinkingMode: thinkingMode);

    private static HttpResponseMessage Json(HttpStatusCode status, string body)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static OpenAiCompatibleProvider Create(
        HttpStatusCode status,
        string body,
        out List<HttpRequestMessage> captured,
        params (string Name, string Value)[] headers)
        => Create(status, body, Settings(), out captured, headers);

    private static OpenAiCompatibleProvider Create(
        HttpStatusCode status,
        string body,
        ApiSettings settings,
        out List<HttpRequestMessage> captured,
        params (string Name, string Value)[] headers)
    {
        var requests = new List<HttpRequestMessage>();
        var handler = new StubHandler(request =>
        {
            requests.Add(request);
            _lastBody = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            var response = Json(status, body);
            foreach (var (name, value) in headers)
            {
                response.Headers.TryAddWithoutValidation(name, value);
            }

            return response;
        });

        captured = requests;
        return new OpenAiCompatibleProvider(new HttpClient(handler), () => settings);
    }

    private static TranslationRequest Request() => new()
    {
        Text = "hello",
        TargetLanguage = "zh-CN",
    };

    [Fact]
    public async Task TranslateAsync_ReturnsMessageContent()
    {
        var provider = Create(
            HttpStatusCode.OK,
            """{"choices":[{"message":{"role":"assistant","content":"你好"}}]}""",
            out var requests);

        string? captured = null;
        var progress = new Progress<TranslationChunk>(chunk => captured = chunk.Text);

        await provider.TranslateAsync(Request(), progress, CancellationToken.None);

        // Progress<T> 在线程池回调，等一小会儿。
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Equal("你好", captured);
        Assert.Single(requests);
        Assert.Equal("Bearer sk-test", requests[0].Headers.Authorization?.ToString());
        Assert.Equal("https://api.example.com/v1/chat/completions", requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task TranslateAsync_ThrowsAuth_On401()
    {
        var provider = Create(HttpStatusCode.Unauthorized, """{"error":{"message":"bad key"}}""", out _);

        var error = await Assert.ThrowsAsync<TranslationException>(
            () => provider.TranslateAsync(Request(), null, CancellationToken.None));

        Assert.Equal(TranslationErrorKind.Auth, error.Kind);
        Assert.Contains("bad key", error.Message);
    }

    [Fact]
    public async Task TranslateAsync_ThrowsModel_On404()
    {
        var provider = Create(HttpStatusCode.NotFound, "{}", out _);

        var error = await Assert.ThrowsAsync<TranslationException>(
            () => provider.TranslateAsync(Request(), null, CancellationToken.None));

        Assert.Equal(TranslationErrorKind.Model, error.Kind);
    }

    [Fact]
    public async Task TranslateAsync_ThrowsRateLimit_WithRetryAfter()
    {
        var provider = Create(HttpStatusCode.TooManyRequests, "{}", out _, ("Retry-After", "5"));

        var error = await Assert.ThrowsAsync<TranslationException>(
            () => provider.TranslateAsync(Request(), null, CancellationToken.None));

        Assert.Equal(TranslationErrorKind.RateLimit, error.Kind);
        Assert.Equal(5, error.RetryAfterSeconds);
    }

    [Fact]
    public async Task TranslateAsync_ThrowsInternal_On500()
    {
        var provider = Create(HttpStatusCode.InternalServerError, "boom", out _);

        var error = await Assert.ThrowsAsync<TranslationException>(
            () => provider.TranslateAsync(Request(), null, CancellationToken.None));

        Assert.Equal(TranslationErrorKind.Internal, error.Kind);
    }

    [Fact]
    public async Task TranslateAsync_ThrowsBadResponse_OnMalformedJson()
    {
        var provider = Create(HttpStatusCode.OK, "not-json", out _);

        var error = await Assert.ThrowsAsync<TranslationException>(
            () => provider.TranslateAsync(Request(), null, CancellationToken.None));

        Assert.Equal(TranslationErrorKind.BadResponse, error.Kind);
    }

    [Fact]
    public async Task TranslateAsync_ThrowsConfigMissing_WhenApiKeyEmpty()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, "{}"));
        var provider = new OpenAiCompatibleProvider(new HttpClient(handler), () => Settings(key: null));

        var error = await Assert.ThrowsAsync<TranslationException>(
            () => provider.TranslateAsync(Request(), null, CancellationToken.None));

        Assert.Equal(TranslationErrorKind.ConfigMissing, error.Kind);
    }

    [Fact]
    public async Task TranslateAsync_SendsTargetLanguageInSystemPrompt_AndRawTextAsUserMessage()
    {
        var settings = Settings() with { SystemPrompt = string.Empty, Model = "test-model" };
        var provider = Create(
            HttpStatusCode.OK,
            """{"choices":[{"message":{"content":"你好"}}]}""",
            settings,
            out var requests);

        await provider.TranslateAsync(Request(), null, CancellationToken.None);

        using var document = System.Text.Json.JsonDocument.Parse(_lastBody!);
        var messages = document.RootElement.GetProperty("messages");

        var system = messages[0].GetProperty("content").GetString();
        var user = messages[1].GetProperty("content").GetString();

        Assert.Contains("简体中文", system);
        Assert.Contains("只输出译文", system);
        Assert.Equal("hello", user);
        Assert.Equal("test-model", document.RootElement.GetProperty("model").GetString());
    }

    [Fact]
    public async Task TranslateAsync_MergesExtraBodyIntoPayload()
    {
        var provider = Create(
            HttpStatusCode.OK,
            """{"choices":[{"message":{"content":"你好"}}]}""",
            Settings(extraBody: """{"thinking":{"type":"disabled"},"do_sample":false}"""),
            out _);

        await provider.TranslateAsync(Request(), null, CancellationToken.None);

        using var document = System.Text.Json.JsonDocument.Parse(_lastBody!);
        var thinking = document.RootElement.GetProperty("thinking");
        Assert.Equal("disabled", thinking.GetProperty("type").GetString());
        Assert.False(document.RootElement.GetProperty("do_sample").GetBoolean());
    }

    [Fact]
    public async Task TranslateAsync_ThrowsConfigMissing_OnInvalidExtraBody()
    {
        var provider = Create(
            HttpStatusCode.OK,
            """{"choices":[{"message":{"content":"你好"}}]}""",
            Settings(extraBody: "{not-json"),
            out _);

        var error = await Assert.ThrowsAsync<TranslationException>(
            () => provider.TranslateAsync(Request(), null, CancellationToken.None));

        Assert.Equal(TranslationErrorKind.ConfigMissing, error.Kind);
    }

    [Theory]
    [InlineData("doubao-seed-2-1-lite", "thinking.type", "disabled")]
    [InlineData("qwen3-max", "enable_thinking", "false")]
    [InlineData("gpt-5", "reasoning_effort", "minimal")]
    public async Task TranslateAsync_WritesProviderSpecificThinkingField_WhenModeIsOff(
        string model,
        string propertyPath,
        string expected)
    {
        var provider = Create(
            HttpStatusCode.OK,
            """{"choices":[{"message":{"content":"你好"}}]}""",
            Settings(model: model, thinkingMode: "off"),
            out _);

        await provider.TranslateAsync(Request(), null, CancellationToken.None);

        using var document = System.Text.Json.JsonDocument.Parse(_lastBody!);
        var element = document.RootElement;
        foreach (var part in propertyPath.Split('.'))
        {
            element = element.GetProperty(part);
        }

        var actual = element.ValueKind == System.Text.Json.JsonValueKind.String
            ? element.GetString()
            : element.GetRawText();

        Assert.Equal(expected, actual);
        Assert.False(document.RootElement.GetProperty("stream").GetBoolean());
    }

    [Fact]
    public async Task TranslateAsync_WritesNothing_ForUnknownModel()
    {
        var provider = Create(
            HttpStatusCode.OK,
            """{"choices":[{"message":{"content":"你好"}}]}""",
            Settings(model: "llama-3.1", thinkingMode: "off"),
            out _);

        await provider.TranslateAsync(Request(), null, CancellationToken.None);

        Assert.DoesNotContain("thinking", _lastBody);
        Assert.DoesNotContain("enable_thinking", _lastBody);
        Assert.DoesNotContain("reasoning_effort", _lastBody);
    }

    [Fact]
    public async Task TranslateAsync_ExtraBodyOverridesAutoThinking()
    {
        var provider = Create(
            HttpStatusCode.OK,
            """{"choices":[{"message":{"content":"你好"}}]}""",
            Settings(
                model: "doubao-seed-2-1-lite",
                thinkingMode: "off",
                extraBody: """{"thinking":{"type":"enabled"}}"""),
            out _);

        await provider.TranslateAsync(Request(), null, CancellationToken.None);

        using var document = System.Text.Json.JsonDocument.Parse(_lastBody!);
        Assert.Equal(
            "enabled",
            document.RootElement.GetProperty("thinking").GetProperty("type").GetString());
    }

    [Theory]
    [InlineData("https://api.example.com/v1", "https://api.example.com/v1/chat/completions")]
    [InlineData("https://api.example.com/v1/", "https://api.example.com/v1/chat/completions")]
    [InlineData("https://api.example.com/v1/chat/completions", "https://api.example.com/v1/chat/completions")]
    public void BuildEndpoint_NormalizesUrl(string input, string expected)
    {
        Assert.Equal(expected, OpenAiCompatibleProvider.BuildEndpoint(input));
    }

    [Fact]
    public void ExtractContent_SupportsChoicesTextShape()
    {
        Assert.Equal("hi", OpenAiCompatibleProvider.ExtractContent("""{"choices":[{"text":"hi"}]}"""));
        Assert.Null(OpenAiCompatibleProvider.ExtractContent("""{"choices":[]}"""));
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(responder(request));
    }
}