using ImageMagick;
using Microsoft.Extensions.Options;
using ReceiptSplit.Options;

namespace ReceiptSplit.Extraction;

public sealed record ImageDetails(MagickFormat Format, string MimeType, string Extension, uint Width, uint Height);

public sealed record PreparedImage(byte[] Data, string MimeType, int Width, int Height, bool Converted);

/// <summary>
/// Turns an uploaded photo into what gets sent to the model. JPEG/PNG within the model's image budget is sent
/// byte for byte (llama.cpp ignores EXIF rotation, but the model reads sideways receipts fine). Anything larger,
/// or in a format llama.cpp can't decode (HEIC, WebP, ...), is auto-oriented, shrunk to fit, and re-encoded as JPEG.
/// </summary>
public sealed class ImagePreparer(IOptions<LlmOptions> options)
{
    /// <summary>Qwen vision models merge 16 px patches 2×2, so one image token covers 32×32 px.</summary>
    private const uint TokenSize = 32;

    /// <summary>Refuse decompression bombs; a 200 MP photo is far beyond any phone camera.</summary>
    private const long MaxSourcePixels = 200_000_000;

    private static readonly Dictionary<MagickFormat, (string MimeType, string Extension)> SupportedFormats = new()
    {
        [MagickFormat.Jpeg] = ("image/jpeg", ".jpg"),
        [MagickFormat.Png] = ("image/png", ".png"),
        [MagickFormat.Heic] = ("image/heic", ".heic"),
        [MagickFormat.Heif] = ("image/heif", ".heif"),
        [MagickFormat.Avif] = ("image/avif", ".avif"),
        [MagickFormat.WebP] = ("image/webp", ".webp"),
        [MagickFormat.Bmp] = ("image/bmp", ".bmp"),
        [MagickFormat.Bmp3] = ("image/bmp", ".bmp"),
        [MagickFormat.Gif] = ("image/gif", ".gif"),
        [MagickFormat.Tiff] = ("image/tiff", ".tiff"),
    };

    /// <exception cref="InvalidImageException">The data is not an image in a supported format.</exception>
    public static ImageDetails Identify(byte[] data)
    {
        MagickImageInfo info;
        try
        {
            info = new MagickImageInfo(data);
        }
        catch (MagickException)
        {
            throw new InvalidImageException("The file is not a readable image.");
        }

        if (!SupportedFormats.TryGetValue(info.Format, out var type))
        {
            throw new InvalidImageException($"Unsupported image format: {info.Format}.");
        }

        if ((long)info.Width * info.Height > MaxSourcePixels)
        {
            throw new InvalidImageException($"The image is too large ({info.Width}×{info.Height}).");
        }

        return new ImageDetails(info.Format, type.MimeType, type.Extension, info.Width, info.Height);
    }

    public PreparedImage Prepare(byte[] original)
    {
        var details = Identify(original);
        var budget = (long)options.Value.MaxImageTokens * TokenSize * TokenSize;

        if (details.Format is MagickFormat.Jpeg or MagickFormat.Png && (long)details.Width * details.Height <= budget)
        {
            return new PreparedImage(original, details.MimeType, (int)details.Width, (int)details.Height, Converted: false);
        }

        using var image = new MagickImage(original);
        image.AutoOrient();

        if ((long)image.Width * image.Height > budget)
        {
            // Scale both sides evenly, then round down to whole tokens so the result stays within budget.
            var scale = Math.Sqrt((double)budget / ((long)image.Width * image.Height));
            var width = Math.Max(TokenSize, (uint)(image.Width * scale) / TokenSize * TokenSize);
            var height = Math.Max(TokenSize, (uint)(image.Height * scale) / TokenSize * TokenSize);
            image.Resize(new MagickGeometry(width, height) { IgnoreAspectRatio = true });
        }

        image.BackgroundColor = MagickColors.White;
        image.Alpha(AlphaOption.Remove);
        image.Strip();
        image.Quality = 90;
        var data = image.ToByteArray(MagickFormat.Jpeg);
        return new PreparedImage(data, "image/jpeg", (int)image.Width, (int)image.Height, Converted: true);
    }
}
