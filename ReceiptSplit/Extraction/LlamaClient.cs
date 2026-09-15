using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using ReceiptSplit.Options;

namespace ReceiptSplit.Extraction;

public sealed record LlmCompletion(
    string Content,
    string? Model,
    string? FinishReason,
    int? PromptTokens,
    int? CompletionTokens,
    TimeSpan Elapsed);

public interface ILlamaClient
{
    Task<LlmCompletion> ExtractReceiptAsync(PreparedImage image, CancellationToken cancellationToken);
}

/// <summary>Calls llama-server's OpenAI-compatible chat endpoint with the settings validated on real receipts.</summary>
public sealed class LlamaClient(HttpClient http, IOptions<LlmOptions> options) : ILlamaClient
{
    public async Task<LlmCompletion> ExtractReceiptAsync(PreparedImage image, CancellationToken cancellationToken)
    {
        var llm = options.Value;
        var request = new
        {
            model = llm.Model,
            temperature = 0,
            max_tokens = llm.MaxOutputTokens,
            // No response_format json_schema: grammar-constrained output flipped the sign of every amount.
            chat_template_kwargs = new { enable_thinking = false },
            messages = new object[]
            {
                new { role = "system", content = ExtractionPrompt.System },
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new
                        {
                            type = "image_url",
                            image_url = new { url = $"data:{image.MimeType};base64,{Convert.ToBase64String(image.Data)}" },
                        },
                        new { type = "text", text = ExtractionPrompt.User },
                    },
                },
            },
        };

        var stopwatch = Stopwatch.StartNew();
        using var response = await http.PostAsJsonAsync("v1/chat/completions", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"llama-server returned {(int)response.StatusCode}: {body[..Math.Min(body.Length, 500)]}",
                inner: null,
                response.StatusCode);
        }

        var completion = await response.Content.ReadFromJsonAsync<ChatCompletionResponse>(cancellationToken);
        stopwatch.Stop();
        var choice = completion?.Choices?.FirstOrDefault()
            ?? throw new HttpRequestException("llama-server returned no completion choices.");

        return new LlmCompletion(
            choice.Message?.Content ?? "",
            completion.Model,
            choice.FinishReason,
            completion.Usage?.PromptTokens,
            completion.Usage?.CompletionTokens,
            stopwatch.Elapsed);
    }

    private sealed record ChatCompletionResponse(string? Model, List<ChatChoice>? Choices, ChatUsage? Usage);

    private sealed record ChatChoice(
        ChatMessage? Message,
        [property: JsonPropertyName("finish_reason")] string? FinishReason);

    private sealed record ChatMessage(string? Content);

    private sealed record ChatUsage(
        [property: JsonPropertyName("prompt_tokens")] int PromptTokens,
        [property: JsonPropertyName("completion_tokens")] int CompletionTokens);
}
