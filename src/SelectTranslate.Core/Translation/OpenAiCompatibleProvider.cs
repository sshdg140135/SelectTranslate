using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace SelectTranslate.Core.Translation;

/// <summary>
/// OpenAI 兼容的 /chat/completions 实现。M2 先用非流式（stream=false）打通链路，
/// 流式解析在 M3 接入（design §5.3）。
/// </summary>
public sealed class OpenAiCompatibleProvider(
    HttpClient httpClient,
    Func<ApiSettings> settingsFactory,
    Action<string>? debugLog = null)
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

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

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

            var ttfb = stopwatch.ElapsedMilliseconds;

            var body = await response.Content.ReadAsStringAsync(timeoutCts.Token);

            debugLog?.Invoke(
                $"LLM 响应：status={(int)response.StatusCode}，ttfb={ttfb}ms，total={stopwatch.ElapsedMilliseconds}ms"
                + DescribeUsage(body));

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

        ApplyThinking(payload, settings);

        // 附加参数最后合并：用户手填的内容永远覆盖自动推断。
        ApplyExtraBody(payload, settings.ExtraBodyJson);

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    /// <summary>按模型名前缀写入思考开关字段；未知模型不写。</summary>
    private static void ApplyThinking(Dictionary<string, object?> payload, ApiSettings settings)
    {
        if (settings.ThinkingMode is not ("on" or "off"))
        {
            return;
        }

        var plan = ThinkingStrategies.Detect(settings.Model);
        var patch = ThinkingStrategies.BuildPatch(plan, settings.ThinkingMode == "on");
        if (patch is null)
        {
            return;
        }

        foreach (var pair in patch)
        {
            payload[pair.Key] = pair.Value;
        }
    }

    /// <summary>把"附加请求参数"里的顶层属性合并进请求体（可覆盖 stream/model 等）。</summary>
    private static void ApplyExtraBody(Dictionary<string, object?> payload, string? extraBodyJson)
    {
        if (string.IsNullOrWhiteSpace(extraBodyJson))
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(extraBodyJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new TranslationException(
                    TranslationErrorKind.ConfigMissing,
                    "附加请求参数必须是 JSON 对象，例如 {\"thinking\":{\"type\":\"disabled\"}}");
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                payload[property.Name] = property.Value.Clone();
            }
        }
        catch (JsonException ex)
        {
            throw new TranslationException(
                TranslationErrorKind.ConfigMissing,
                "附加请求参数不是合法 JSON，请到设置里检查。",
                inner: ex);
        }
    }

    /// <summary>从响应里取 token 用量，日志只写数字，不写内容。</summary>
    private static string DescribeUsage(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("usage", out var usage)
                || usage.ValueKind != JsonValueKind.Object)
            {
                return string.Empty;
            }

            var parts = new List<string>();
            if (usage.TryGetProperty("prompt_tokens", out var prompt) && prompt.TryGetInt32(out var p))
            {
                parts.Add($"prompt={p}");
            }

            if (usage.TryGetProperty("completion_tokens", out var completion) && completion.TryGetInt32(out var c))
            {
                parts.Add($"completion={c}");
            }

            if (usage.TryGetProperty("completion_tokens_details", out var details)
                && details.ValueKind == JsonValueKind.Object
                && details.TryGetProperty("reasoning_tokens", out var reasoning)
                && reasoning.TryGetInt32(out var r))
            {
                parts.Add($"reasoning={r}");
            }

            return parts.Count == 0 ? string.Empty : "，" + string.Join("，", parts);
        }
        catch (JsonException)
        {
            return string.Empty;
        }
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