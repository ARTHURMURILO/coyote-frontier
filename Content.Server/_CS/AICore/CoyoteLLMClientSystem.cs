using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Content.Shared._CS.AICore;

namespace Content.Server._CS.AICore;

/// <summary>
///     HTTP client for the LLM API (OpenAI-compatible chat completions endpoint).
///     Sends system+user messages, deserializes the JSON response into <see cref="LLMResponse"/>.
///     Supports Bearer token auth, configurable model/temperature/max_tokens/reasoning_effort,
///     and a 30-second timeout.
///     The response is parsed by stripping markdown fences and extracting the first JSON object.
/// </summary>
public sealed class CoyoteLLMClientSystem : EntitySystem
{
    private readonly HttpClient _http = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        IncludeFields = true,
        WriteIndented = false
    };

    public async Task<LLMResponse?> CallAsync(CoyoteAICoreComponent core, string systemPrompt, string userPrompt)
    {
        if (string.IsNullOrEmpty(core.ApiEndpoint))
            return null;

        try
        {
            var messagesList = new List<object>
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            };

            var bodyDict = new Dictionary<string, object?>
            {
                { "model", string.IsNullOrEmpty(core.ModelName) ? null : core.ModelName },
                { "messages", messagesList },
                { "temperature", core.Temperature },
                { "max_tokens", core.MaxTokens }
            };

            if (core.ReasoningLevel != ReasoningLevel.Off)
            {
                bodyDict["reasoning_effort"] = core.ReasoningLevel switch
                {
                    ReasoningLevel.Low => "low",
                    ReasoningLevel.Medium => "medium",
                    ReasoningLevel.High => "high",
                    ReasoningLevel.Max => "high",
                    _ => "medium"
                };
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, core.ApiEndpoint)
            {
                Content = JsonContent.Create(bodyDict, options: JsonOptions)
            };

            if (!string.IsNullOrEmpty(core.ApiKey))
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", core.ApiKey);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var response = await _http.SendAsync(request, cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cts.Token);
                Log.Error($"CoyoteAI: LLM returned {(int)response.StatusCode} for core {core.CoreId}: {errorBody[..Math.Min(errorBody.Length, 500)]}");
                return null;
            }

            var apiResponse = await response.Content.ReadFromJsonAsync<OpenAiApiResponse>(JsonOptions, cts.Token);

            if (apiResponse?.Choices == null || apiResponse.Choices.Length == 0)
            {
                Log.Warning($"CoyoteAI: Empty response from LLM for core {core.CoreId}");
                return null;
            }

            var content = apiResponse.Choices[0].Message.Content;
            if (string.IsNullOrEmpty(content))
            {
                Log.Warning($"CoyoteAI: Empty content from LLM for core {core.CoreId}");
                return null;
            }

            var reasoningContent = apiResponse.Choices[0].Message.ReasoningContent;
            if (!string.IsNullOrEmpty(reasoningContent))
            {
                Log.Debug($"CoyoteAI: Model reasoning: {reasoningContent[..Math.Min(reasoningContent.Length, 200)]}");
            }

            content = StripMarkdownFence(content);
            var json = ExtractJsonObject(content);
            if (json == null)
            {
                Log.Error($"CoyoteAI: No JSON object found in LLM response for core {core.CoreId}: {content[..Math.Min(content.Length, 200)]}");
                return null;
            }

            Log.Debug($"CoyoteAI: Extracted JSON: {json[..Math.Min(json.Length, 300)]}");

            var llmResponse = JsonSerializer.Deserialize<LLMResponse>(json, JsonOptions);
            Log.Debug($"CoyoteAI: Parsed response - shouldRespond={llmResponse?.ShouldRespond}, channel={llmResponse?.Channel}, message={llmResponse?.Message?[..Math.Min((llmResponse?.Message?.Length).GetValueOrDefault(), 100)]}");
            return llmResponse;
        }
        catch (TaskCanceledException)
        {
            Log.Warning($"CoyoteAI: LLM request timed out for core {core.CoreId}");
            return null;
        }
        catch (HttpRequestException ex)
        {
            Log.Error($"CoyoteAI: HTTP error for core {core.CoreId}: {ex.Message}");
            return null;
        }
        catch (JsonException ex)
        {
            Log.Error($"CoyoteAI: JSON parse error for core {core.CoreId}: {ex.Message}");
            return null;
        }
    }

    private static string StripMarkdownFence(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.StartsWith("```"))
        {
            var firstNewline = trimmed.IndexOf('\n');
            if (firstNewline > 0)
                trimmed = trimmed.Substring(firstNewline + 1);
            else
                trimmed = trimmed.Substring(3);
        }
        if (trimmed.EndsWith("```"))
            trimmed = trimmed.Substring(0, trimmed.Length - 3);
        return trimmed.Trim();
    }

    private static string? ExtractJsonObject(string text)
    {
        var start = text.IndexOf('{');
        if (start < 0) return null;
        var end = text.LastIndexOf('}');
        if (end < 0 || end <= start) return null;
        return text[start..(end + 1)];
    }

    private sealed class OpenAiApiResponse
    {
        [JsonPropertyName("choices")]
        public Choice[]? Choices { get; set; }
    }

    private sealed class Choice
    {
        [JsonPropertyName("message")]
        public Message Message { get; set; } = new();
    }

    private sealed class Message
    {
        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;

        [JsonPropertyName("reasoning_content")]
        public string? ReasoningContent { get; set; }
    }
}
