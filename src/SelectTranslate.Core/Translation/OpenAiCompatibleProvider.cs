using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace SelectTranslate.Core.Translation;

/// <summary>
/// OpenAI 兼容的 /chat/completions 实现。M2 先用非流式（stream=false）打通链路，
/// 流式解析在 M3 接入（design §5.3）。
/// </summary>
public sealed class OpenAiCompatibleProvider(HttpClient httpClient, Func<ApiSettings> settingsFactory)
    : ITranslationProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public string Id => "openai-compatible";

    public string DisplayName => "OpenAI 兼容接口";

    public ProviderCapabilities Capabilities { get; } = new(SupportsStreaming: true, MaxInputChars: 8_000);

    public async Task TranslateAsync(
        TranslationRequest request,
        IProgress<TranslationChunk>? onChunk,
        CancellationToken cancellationToken)
    {
        var settings = settingsFactory();

        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            throw new TranslationException(TranslationErrorKind.ConfigMissing, "尚未配置 API Key");
        }

        var url = BuildEndpoint(settings.BaseUrl);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(settings.TimeoutMs);

        try
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
            httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            foreach (var header in settings.ExtraHeaders)
            {
                if (!string.IsNullOrWhiteSpace(header.Key))
                {
                    httpRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            var payload = BuildPayload(request, settings);
            httpRequest.Content = new StringContent(payload, Encoding.UTF8, "application/json");

            using var response = await httpClient.SendAsync(
                httpRequest,
                HttpCompletionOption.ResponseHeadersRead,
                timeoutCts.Token);

            var body = await response.Content.ReadAsStringAsync(timeoutCts.Token);

            if (!response.IsSuccessStatusCode)
            {
                throw MapError(response, body, settings);
            }

            var text = ExtractContent(body);
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new TranslationException(TranslationErrorKind.BadResponse, "服务端返回了空译文");
            }

            onChunk?.Report(new TranslationChunk(text, IsFinal: true));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TranslationException(
                TranslationErrorKind.Timeout,
                $"请求超时（{settings.TimeoutMs / 1000}s）");
        }
        catch (HttpRequestException ex)
        {
            throw new TranslationException(TranslationErrorKind.Network, "网络不可达", inner: ex);
        }
    }

    /// <summary>补全 {baseUrl}/chat/completions，容忍用户只填到域名或带尾斜杠。</summary>
    public static string BuildEndpoint(string? baseUrl)
    {
        var root = string.IsNullOrWhiteSpace(baseUrl)
            ? "https://api.deepseek.com/v1"
            : baseUrl.Trim().TrimEnd('/');

        if (root.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            return root;
        }

        return root + "/chat/completions";
    }

    private static string BuildPayload(TranslationRequest request, ApiSettings settings)
    {
        var systemPrompt = string.IsNullOrWhiteSpace(request.SystemPrompt)
            ? PromptBuilder.BuildSystemPrompt(request.TargetLanguage, settings.SystemPrompt)
            : request.SystemPrompt;

        var payload = new Dictionary<string, object?>
        {
            ["model"] = settings.Model,
            ["temperature"] = request.Temperature,
            ["stream"] = false,
            ["messages"] = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = request.Text },
            },
        };

        if (settings.MaxTokens is > 0)
        {
            payload["max_tokens"] = settings.MaxTokens;
        }

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    /// <summary>解析 choices[0].message.content；兼容少数返回 choices[0].text 的实现。</summary>
    public static string? ExtractContent(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            if (root.TryGetProperty("choices", out var choices)
                && choices.ValueKind == JsonValueKind.Array
                && choices.GetArrayLength() > 0)
            {
                var first = choices[0];

                if (first.TryGetProperty("message", out var message)
                    && message.TryGetProperty("content", out var content)
                    && content.ValueKind == JsonValueKind.String)
                {
                    return content.GetString();
                }

                if (first.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                {
                    return text.GetString();
                }
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static TranslationException MapError(
        HttpResponseMessage response,
        string body,
        ApiSettings settings)
    {
        var detail = ExtractErrorMessage(body);
        var message = string.IsNullOrWhiteSpace(detail) ? $"HTTP {(int)response.StatusCode}" : detail;

        return response.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                new TranslationException(TranslationErrorKind.Auth, $"API Key 无效或无权限（{message}）"),

            HttpStatusCode.NotFound =>
                new TranslationException(TranslationErrorKind.Model, $"接口或模型不存在（{message}），请检查地址与模型名"),

            HttpStatusCode.TooManyRequests =>
                new TranslationException(
                    TranslationErrorKind.RateLimit,
                    "请求过于频繁",
                    retryAfterSeconds: ReadRetryAfter(response)),

            >= HttpStatusCode.InternalServerError =>
                new TranslationException(TranslationErrorKind.Internal, $"服务端错误（{message}）"),

            _ => new TranslationException(TranslationErrorKind.BadResponse, $"请求失败（{message}）"),
        };
    }

    private static int? ReadRetryAfter(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta)
        {
            return Math.Max(1, (int)Math.Ceiling(delta.TotalSeconds));
        }

        if (retryAfter?.Date is { } date)
        {
            return Math.Max(1, (int)Math.Ceiling((date - DateTimeOffset.UtcNow).TotalSeconds));
        }

        return null;
    }

    private static string? ExtractErrorMessage(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.String)
                {
                    return error.GetString();
                }

                if (error.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
                {
                    return message.GetString();
                }
            }
        }
        catch (JsonException)
        {
            // 非 JSON 错误页，忽略。
        }

        return null;
    }
}