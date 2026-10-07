namespace ReceiptSplit.Extraction;

/// <summary>Connection and request settings for the OpenAI-compatible llama-server.</summary>
public sealed class LlmOptions
{
    public const string SectionName = "Llm";

    public string BaseUrl { get; set; } = "http://localhost:8000";

    public string? ApiKey { get; set; }

    public string Model { get; set; } = "qwen3.8-27b-q4-mtp";

    /// <summary>llama.cpp's default image token cap for Qwen vision models; one token covers 32×32 px.</summary>
    public int MaxImageTokens { get; set; } = 4096;

    public int MaxOutputTokens { get; set; } = 4096;

    /// <summary>Covers a cold model load plus waiting behind other requests on the shared server.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(10);
}
