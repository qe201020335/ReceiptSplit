using System.Net;
using System.Text.Json;
using ImageMagick;
using ReceiptSplit.Extraction;

namespace ReceiptSplit.Tests;

public class ExtractionErrorsTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(10);

    [Fact]
    public void Keeps_the_server_path_out_of_a_missing_photo_message()
    {
        var message = ExtractionErrors.Describe(
            new FileNotFoundException("Could not find file '/app/data/uploads/0199.jpg'."), Timeout);

        Assert.DoesNotContain("/app/data", message);
        Assert.Contains("missing", message);
    }

    [Fact]
    public void Keeps_the_model_server_body_out_of_an_error_status_message()
    {
        var message = ExtractionErrors.Describe(
            new HttpRequestException("llama-server returned 503: {\"error\":\"slot unavailable\"}", null, HttpStatusCode.ServiceUnavailable),
            Timeout);

        Assert.Equal("The model server returned an error (503). Try running extraction again.", message);
    }

    [Fact]
    public void Describes_the_other_failures_plainly()
    {
        Assert.Equal("Unsupported image format: Svg.", ExtractionErrors.Describe(new InvalidImageException("Unsupported image format: Svg."), Timeout));
        Assert.StartsWith("Couldn't reach the model server", ExtractionErrors.Describe(new HttpRequestException("Connection refused (192.168.1.250:8000)"), Timeout));
        Assert.StartsWith("The model server didn't answer within 10 minutes", ExtractionErrors.Describe(new TaskCanceledException(), Timeout));
        Assert.Equal("The model server sent a response that couldn't be read.", ExtractionErrors.Describe(new JsonException("'<' is an invalid start"), Timeout));
        Assert.Equal("The photo couldn't be decoded.", ExtractionErrors.Describe(new MagickCorruptImageErrorException("broken"), Timeout));
        Assert.StartsWith("Extraction failed unexpectedly", ExtractionErrors.Describe(new InvalidOperationException("Sequence contains no elements"), Timeout));
    }
}
