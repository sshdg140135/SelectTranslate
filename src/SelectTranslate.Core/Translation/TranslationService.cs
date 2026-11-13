using System.Diagnostics;
using SelectTranslate.Core.Configuration;
using SelectTranslate.Core.Languages;

namespace SelectTranslate.Core.Translation;

/// <summary>
/// 翻译编排：语种短路（REQ-03.2）→ 截断（REQ-04.5）→ 调服务商 → 失败重试一次（REQ-04.4）。
/// 更细的流式/退避策略在 M3 完善。
/// </summary>
public sealed class TranslationService(ProviderRegistry registry, Func<AppConfig> configAccessor)
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);

    public async Task<TranslationOutcome> TranslateAsync(
        string text,
        bool bypassLanguageCheck,
        IProgress<TranslationChunk>? onChunk,
        CancellationToken cancellationToken)
    {
        var config = configAccessor();
        var target = config.Target.Lang;
        var guess = LanguageDetector.Detect(text);
        var stopwatch = Stopwatch.StartNew();

        if (!bypassLanguageCheck && LanguageDetector.MatchesTarget(text, target))
        {
            return new TranslationOutcome
            {
                Text = string.Empty,
                DetectedLanguage = guess.Code,
                SameAsTarget = true,
                Elapsed = stopwatch.Elapsed,
            };
        }

        var maxChars = Math.Max(100, config.Behavior.MaxChars);
        var truncated = text.Length > maxChars;
        var payload = truncated ? text[..maxChars] : text;

        var provider = registry.Resolve(config.Api.ProviderType);
        var request = new TranslationRequest
        {
            Text = payload,
            TargetLanguage = target,
            Temperature = config.Api.Temperature,
            SystemPrompt = PromptBuilder.BuildSystemPrompt(target, config.Api.SystemPrompt),
        };

        var collector = new ChunkCollector(onChunk);
        var attempt = 0;

        while (true)
        {
            try
            {
                await provider.TranslateAsync(request, collector, cancellationToken);
                break;
            }
            catch (TranslationException ex) when (attempt == 0 && IsRetryable(ex))
            {
                attempt++;

                var delay = ex.RetryAfterSeconds is { } seconds
                    ? TimeSpan.FromSeconds(Math.Clamp(seconds, 1, 10))
                    : RetryDelay;

                await Task.Delay(delay, cancellationToken);
            }
        }

        var translated = collector.Text;
        var echoed = IsEcho(payload, translated);

        if (echoed)
        {
            // 常见于选中纯标识符：模型按"代码保持原样"处理了。追加强指令再试一次。
            var forced = request with
            {
                SystemPrompt = request.SystemPrompt
                    + "\n"
                    + PromptBuilder.AntiEchoInstruction.Replace("{目标语言}", target),
            };

            var retryCollector = new ChunkCollector(null);
            try
            {
                await provider.TranslateAsync(forced, retryCollector, cancellationToken);
                translated = retryCollector.Text;
                echoed = IsEcho(payload, translated);
            }
            catch (TranslationException)
            {
                // 第二次失败就保留第一次结果，至少不是空白。
            }
        }

        return new TranslationOutcome
        {
            Text = translated,
            DetectedLanguage = guess.Code,
            Truncated = truncated,
            Echoed = echoed,
            Elapsed = stopwatch.Elapsed,
            Model = config.Api.Model,
        };
    }

    public static bool IsEcho(string request, string response)
    {
        static string Normalize(string value) => string.Join(
            ' ',
            value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        return string.Equals(Normalize(request), Normalize(response), StringComparison.Ordinal);
    }

    private static bool IsRetryable(TranslationException exception)
        => exception.Kind is TranslationErrorKind.Network
            or TranslationErrorKind.Internal
            or TranslationErrorKind.RateLimit;

    /// <summary>把增量拼成完整译文，同时原样转发给 UI。</summary>
    private sealed class ChunkCollector(IProgress<TranslationChunk>? inner) : IProgress<TranslationChunk>
    {
        private readonly List<string> _parts = [];

        public string Text => string.Concat(_parts);

        public void Report(TranslationChunk value)
        {
            if (!string.IsNullOrEmpty(value.Text))
            {
                _parts.Add(value.Text);
            }

            inner?.Report(value);
        }
    }
}