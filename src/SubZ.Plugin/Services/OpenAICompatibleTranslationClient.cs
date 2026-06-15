using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SubZ.Plugin.Configuration;

namespace SubZ.Plugin.Services;

public sealed class OpenAICompatibleTranslationClient : ISubtitleTranslationClient
{
    private static readonly ConcurrentDictionary<string, bool> ThinkingSupportCache = new ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
    private static readonly HttpClient HttpClient = CreateHttpClient();

    public async Task<TranslationBatchResponse> TranslateBatchAsync(TranslationBatchRequest request, CancellationToken cancellationToken)
    {
        var sep = "<<<SUBZ_SEP>>>";
        var joined = string.Join("\n" + sep + "\n", request.Inputs);
        var prompt = BuildPrompt(
            request.TargetLanguage,
            sep,
            request.Inputs.Count,
            joined,
            request.PromptMode,
            request.RetryReasonHint);

        var baseUrl = request.Profile.BaseUrl?.TrimEnd('/') ?? "https://api.deepseek.com";
        var url = baseUrl + "/chat/completions";
        var model = string.IsNullOrWhiteSpace(request.Profile.Model) ? "deepseek-v4-flash" : request.Profile.Model;
        var cacheKey = BuildThinkingCacheKey(baseUrl, model);
        var hasCache = ThinkingSupportCache.TryGetValue(cacheKey, out var supportsThinkingParam);
        var debug = request.EnableDebugLog;

        var preferredThinking = BuildPreferredThinkingOption(request.Profile);
        var shouldTryThinkingDisabled = preferredThinking != null && (!hasCache || supportsThinkingParam);

        var systemContent = BuildSystemContent(request);
        var userContent = prompt;
        if (request.ExtractGlossary)
        {
            userContent += BuildExtractGlossaryInstruction(request.GlossaryMaxEntries);
        }

        var payloadObj = new ChatCompletionRequest
        {
            model = model,
            temperature = GetSafeTemperature(request.Profile),
            thinking = shouldTryThinkingDisabled ? preferredThinking : null,
            messages = new[]
            {
                new ChatMessage { role = "system", content = systemContent },
                new ChatMessage { role = "user", content = userContent }
            }
        };

        ChatCompletionResponse? response;
        try
        {
            response = await SendAndDeserializeAsync(
                url,
                request.Profile.ApiKey,
                payloadObj,
                request.Profile.TimeoutSeconds,
                cancellationToken,
                debug).ConfigureAwait(false);
            if (shouldTryThinkingDisabled)
            {
                ThinkingSupportCache[cacheKey] = true;
            }
        }
        catch (TranslationServiceException ex) when (shouldTryThinkingDisabled && IsThinkingUnsupportedError(ex))
        {
            // Auto-fallback for providers/models that reject the `thinking` field.
            ThinkingSupportCache[cacheKey] = false;
            if (debug)
            {
                InMemoryTranslationJobDispatcher.AppendRuntimeLog("Debug", "Thinking field rejected by provider; retrying without thinking.");
            }
            payloadObj.thinking = null;
            response = await SendAndDeserializeAsync(
                url,
                request.Profile.ApiKey,
                payloadObj,
                request.Profile.TimeoutSeconds,
                cancellationToken,
                debug).ConfigureAwait(false);
        }

        var rawContent = response?.choices != null && response.choices.Length > 0
            ? response.choices[0]?.message?.content ?? string.Empty
            : string.Empty;

        IReadOnlyList<GlossaryEntry>? extractedGlossary = null;
        string output;
        if (request.ExtractGlossary && !string.IsNullOrWhiteSpace(rawContent))
        {
            BatchSelfHealingTranslator.TryParseGlossary(
                rawContent,
                request.GlossaryMaxEntries,
                out var cleanedOutput,
                out extractedGlossary);
            output = !string.IsNullOrWhiteSpace(cleanedOutput) ? cleanedOutput : rawContent;
        }
        else
        {
            output = rawContent;
        }

        var lines = SplitOutput(output, sep, request.Inputs.Count, request.Inputs);

        return new TranslationBatchResponse
        {
            OutputLines = lines,
            PromptTokens = response?.usage?.prompt_tokens ?? 0,
            CompletionTokens = response?.usage?.completion_tokens ?? 0,
            TotalTokens = response?.usage?.total_tokens ?? 0,
            ExtractedGlossary = extractedGlossary
        };
    }

    private static ThinkingOption? BuildPreferredThinkingOption(TranslationApiProfile profile)
    {
        var provider = (profile.Provider ?? string.Empty).Trim();
        var baseUrl = (profile.BaseUrl ?? string.Empty).Trim();
        var model = (profile.Model ?? string.Empty).Trim();
        var isDeepSeekProvider = string.Equals(provider, "deepseek", StringComparison.OrdinalIgnoreCase);
        var isDeepSeekHost = IsDeepSeekHost(baseUrl);
        var isDeepSeekModel = model.StartsWith("deepseek", StringComparison.OrdinalIgnoreCase);
        var isDeepSeek = isDeepSeekProvider || (isDeepSeekHost && isDeepSeekModel);

        return isDeepSeek ? new ThinkingOption { type = "disabled" } : null;
    }

    private static bool IsDeepSeekHost(string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return false;
        }

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var host = uri.Host ?? string.Empty;
        return host.Equals("api.deepseek.com", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".deepseek.com", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildThinkingCacheKey(string baseUrl, string model)
    {
        return $"{(baseUrl ?? string.Empty).TrimEnd('/')}|{(model ?? string.Empty).Trim()}";
    }

    private static bool IsThinkingUnsupportedError(TranslationServiceException ex)
    {
        var message = (ex.Message ?? string.Empty).ToLowerInvariant();
        if (!message.Contains("thinking"))
        {
            return false;
        }

        return message.Contains("unsupported")
            || message.Contains("not supported")
            || message.Contains("unknown")
            || message.Contains("invalid")
            || message.Contains("unrecognized")
            || message.Contains("extra inputs are not permitted")
            || message.Contains("additional properties are not allowed");
    }

    private static string BuildSystemContent(TranslationBatchRequest request)
    {
        var glossarySection = BatchSelfHealingTranslator.BuildGlossarySection(request.Glossary);
        if (string.IsNullOrWhiteSpace(glossarySection))
        {
            return "You are a professional subtitle translator.";
        }

        return "You are a professional subtitle translator.\n"
            + "Glossary of proper nouns (names, places, organizations, products, etc. - use these translations consistently):\n"
            + glossarySection;
    }

    private static string BuildExtractGlossaryInstruction(int maxEntries)
    {
        var limit = Math.Max(1, maxEntries);
        return "\n\n"
            + $"Additionally, identify NEW proper nouns (person names, place names, organization names, product names, etc.) that appear in the subtitles above "
            + $"and are NOT already listed in the glossary above. "
            + $"Append them at the end of your response in this format:\n"
            + $"---SUBZ_GLOSSARY---\n"
            + $"Original Name: Translated Name\n"
            + $"Limit to {limit} entries total. Only include proper nouns actually appearing in the subtitles.\n"
            + $"If no new names found, omit the ---SUBZ_GLOSSARY--- section.";
    }

    private static double GetSafeTemperature(TranslationApiProfile profile)
    {
        var value = profile.Temperature;
        if (value <= 0 || value > 2)
        {
            return 0.1;
        }

        return value;
    }

    private static string BuildPrompt(
        string targetLanguage,
        string sep,
        int expectedCount,
        string joined,
        TranslationPromptMode mode,
        string retryReasonHint)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Task: translate each subtitle line into the target language.");
        sb.AppendLine("Critical rules:");
        sb.AppendLine("1) Keep markers exactly: [[SUBZ_TAG_xxx]], [[SUBZ_LB]], and [[SUBZ_TAG_LINE_0001]] style line IDs.");
        sb.AppendLine("2) Keep each line ID in the same output line. Do not reorder, drop, merge, or duplicate lines.");
        sb.AppendLine("3) Translate natural language text. Do not copy source text unchanged unless it is a proper noun, code, URL, or explicit no-translate token.");
        sb.AppendLine("4) Keep subtitle style concise. No explanations. No extra notes. No surrounding quotes.");
        sb.AppendLine("5) Preserve punctuation intensity and sentence meaning. Avoid paraphrasing or expansion.");
        if (mode == TranslationPromptMode.RetryUnchanged)
        {
            sb.AppendLine("Retry context: previous attempt returned unchanged source text on some lines.");
            sb.AppendLine("For this retry, you MUST translate every translatable line and avoid source-language copy-through.");
            if (!string.IsNullOrWhiteSpace(retryReasonHint))
            {
                sb.AppendLine($"Retry reason hint: {retryReasonHint}");
            }
        }

        sb.AppendLine($"Join translated lines using this separator exactly: {sep}");
        sb.AppendLine($"Expected line count: {expectedCount}");
        sb.AppendLine($"Target language code: {targetLanguage}");
        sb.AppendLine("Input:");
        sb.AppendLine(joined);
        return sb.ToString();
    }

    private static List<string> SplitOutput(string output, string sep, int expectedCount, IReadOnlyList<string> fallbackInputs)
    {
        var parts = (output ?? string.Empty)
            .Split(new[] { sep }, StringSplitOptions.None);

        var list = new List<string>(expectedCount);
        for (var i = 0; i < expectedCount; i++)
        {
            if (i < parts.Length && !string.IsNullOrWhiteSpace(parts[i]))
            {
                list.Add(parts[i].Trim());
            }
            else
            {
                list.Add(fallbackInputs[i]);
            }
        }

        return list;
    }

    private static async Task<string> SendAsync(
        string url,
        string apiKey,
        string payload,
        int timeoutSeconds,
        CancellationToken cancellationToken,
        bool debug)
    {
        if (debug)
        {
            InMemoryTranslationJobDispatcher.AppendRuntimeLog(
                "Debug",
                $"HTTP request | Url={url} | TimeoutSec={timeoutSeconds} | HasApiKey={!string.IsNullOrWhiteSpace(apiKey)}");
            InMemoryTranslationJobDispatcher.AppendRuntimeLog(
                "Debug",
                $"HTTP request payload: {SanitizeJsonForLog(payload)}");
        }

        var effectiveTimeoutSeconds = Math.Max(10, timeoutSeconds);
        using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        using (var request = new HttpRequestMessage(HttpMethod.Post, url))
        {
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(effectiveTimeoutSeconds));

            request.Headers.Accept.Clear();
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
            }

            request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

            HttpResponseMessage? response = null;
            string content = string.Empty;
            try
            {
                response = await HttpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeoutCts.Token).ConfigureAwait(false);
                content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                if (debug)
                {
                    InMemoryTranslationJobDispatcher.AppendRuntimeLog(
                        "Debug",
                        $"HTTP response | Status={(int)response.StatusCode}");
                    InMemoryTranslationJobDispatcher.AppendRuntimeLog(
                        "Debug",
                        $"HTTP response body: {SanitizeJsonForLog(content)}");
                }

                if ((int)response.StatusCode >= 400)
                {
                    throw CreateHttpException(content, (int)response.StatusCode);
                }

                return content;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeoutCts.IsCancellationRequested)
            {
                InMemoryTranslationJobDispatcher.AppendRuntimeLog(
                    "Debug",
                    $"HTTP timeout | Url={url} | TimeoutSec={effectiveTimeoutSeconds}");
                throw new TranslationServiceException(
                    $"Timeout while calling translation API after {effectiveTimeoutSeconds}s.",
                    null,
                    null,
                    isRetryable: true);
            }
            catch (HttpRequestException ex)
            {
                if (debug)
                {
                    InMemoryTranslationJobDispatcher.AppendRuntimeLog(
                        "Debug",
                        $"HTTP request exception | Message={TrimForDebug(ex.Message)}");
                }

                throw new TranslationServiceException(
                    "Network error while calling translation API.",
                    ex,
                    null,
                    isRetryable: true);
            }
            finally
            {
                response?.Dispose();
            }
        }
    }

    private static async Task<ChatCompletionResponse?> SendAndDeserializeAsync(
        string url,
        string apiKey,
        ChatCompletionRequest payloadObj,
        int timeoutSeconds,
        CancellationToken cancellationToken,
        bool debug)
    {
        var payload = Serialize(payloadObj);
        var content = await SendAsync(url, apiKey, payload, timeoutSeconds, cancellationToken, debug).ConfigureAwait(false);
        return Deserialize<ChatCompletionResponse>(content);
    }

    private static string SanitizeJsonForLog(string text)
    {
        var value = text ?? string.Empty;
        if (value.Length > 20000)
        {
            value = value.Substring(0, 20000) + "...(truncated)";
        }

        value = Regex.Replace(value, "\"api[_-]?key\"\\s*:\\s*\"[^\"]*\"", "\"api_key\":\"***\"", RegexOptions.IgnoreCase);
        value = Regex.Replace(value, "\"Authorization\"\\s*:\\s*\"[^\"]*\"", "\"Authorization\":\"***\"", RegexOptions.IgnoreCase);
        return value;
    }

    private static Exception CreateHttpException(string responseBody, int statusCode, Exception? inner = null)
    {
        var retryable = statusCode == 429 || statusCode >= 500;
        var message = $"Translation API HTTP {statusCode}.";

        if (!string.IsNullOrWhiteSpace(responseBody))
        {
            var trimmed = responseBody.Trim();
            if (trimmed.Length > 400)
            {
                trimmed = trimmed.Substring(0, 400);
            }

            message += " " + trimmed;
        }

        return new TranslationServiceException(
            message,
            inner,
            statusCode,
            isRetryable: retryable);
    }

    private static string TrimForDebug(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var value = text.Trim();
        if (value.Length > 800)
        {
            value = value.Substring(0, 800) + "...(truncated)";
        }

        return value;
    }

    private static HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };

        var client = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };

        return client;
    }

    private static string Serialize<T>(T value)
    {
        using (var ms = new MemoryStream())
        {
            var ser = new DataContractJsonSerializer(typeof(T));
            ser.WriteObject(ms, value);
            return Encoding.UTF8.GetString(ms.ToArray());
        }
    }

    private static T? Deserialize<T>(string json)
    {
        using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json ?? string.Empty)))
        {
            var ser = new DataContractJsonSerializer(typeof(T));
            return (T?)ser.ReadObject(ms);
        }
    }

    [DataContract]
    private sealed class ChatCompletionRequest
    {
        [DataMember] public string model = string.Empty;
        [DataMember] public double temperature = 0.2;
        [DataMember] public ThinkingOption? thinking;
        [DataMember] public ChatMessage[] messages = Array.Empty<ChatMessage>();
    }

    [DataContract]
    private sealed class ThinkingOption
    {
        [DataMember] public string type = "disabled";
    }

    [DataContract]
    private sealed class ChatMessage
    {
        [DataMember] public string role = string.Empty;
        [DataMember] public string content = string.Empty;
    }

    [DataContract]
    private sealed class ChatCompletionResponse
    {
        [DataMember] public Choice[]? choices { get; set; }
        [DataMember] public Usage? usage { get; set; }
    }

    [DataContract]
    private sealed class Choice
    {
        [DataMember] public ChatMessage? message { get; set; }
    }

    [DataContract]
    private sealed class Usage
    {
        [DataMember] public int prompt_tokens { get; set; }
        [DataMember] public int completion_tokens { get; set; }
        [DataMember] public int total_tokens { get; set; }
    }
}
