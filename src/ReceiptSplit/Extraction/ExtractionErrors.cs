using System.Text.Json;
using ImageMagick;

namespace ReceiptSplit.Extraction;

/// <summary>
/// Turns an extraction failure into the message shown on the receipt. Exception messages can carry server file
/// paths or the model server's raw error body, so those stay in the log and the receipt gets a plain explanation.
/// </summary>
public static class ExtractionErrors
{
    public static string Describe(Exception exception, TimeSpan modelTimeout) => exception switch
    {
        InvalidImageException invalid => invalid.Message,
        FileNotFoundException or DirectoryNotFoundException =>
            "The uploaded photo is missing from storage. Delete this receipt and upload the photo again.",
        MagickException => "The photo couldn't be decoded.",
        HttpRequestException { StatusCode: { } status } =>
            $"The model server returned an error ({(int)status}). Try running extraction again.",
        HttpRequestException =>
            "Couldn't reach the model server. Check that it is running, then run extraction again.",
        // HttpClient reports its own timeout as a cancellation; a real shutdown never gets here.
        TaskCanceledException or TimeoutException =>
            $"The model server didn't answer within {modelTimeout.TotalMinutes:0.#} minutes. Try running extraction again.",
        JsonException => "The model server sent a response that couldn't be read.",
        _ => "Extraction failed unexpectedly. The server log has the details.",
    };
}
