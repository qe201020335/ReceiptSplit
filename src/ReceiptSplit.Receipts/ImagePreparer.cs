using System.Buffers.Binary;
using System.Text;
using ImageMagick;
using Microsoft.Extensions.Options;
using ReceiptSplit.Extraction;

namespace ReceiptSplit.Receipts;

internal sealed record ImageDetails(MagickFormat Format, string MimeType, string Extension, uint Width, uint Height);

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

    // The upload box offers the same formats: keep receiptsplit.client/src/uploadTypes.ts in step.
    private static readonly Dictionary<MagickFormat, (string MimeType, string Extension)> SupportedFormats = new()
    {
        [MagickFormat.Jpeg] = ("image/jpeg", ".jpg"),
        [MagickFormat.Png] = ("image/png", ".png"),
        [MagickFormat.Heic] = ("image/heic", ".heic"),
        [MagickFormat.Heif] = ("image/heif", ".heif"),
        [MagickFormat.Avif] = ("image/avif", ".avif"),
        [MagickFormat.WebP] = ("image/webp", ".webp"),
        [MagickFormat.Bmp] = ("image/bmp", ".bmp"),
        [MagickFormat.Gif] = ("image/gif", ".gif"),
        [MagickFormat.Tiff] = ("image/tiff", ".tiff"),
    };

    /// <summary>Formats every current browser can display; the rest (HEIC, HEIF, TIFF) are shown as a JPEG copy.</summary>
    private static readonly HashSet<string> BrowserMimeTypes =
        ["image/jpeg", "image/png", "image/gif", "image/webp", "image/avif", "image/bmp"];

    public static bool BrowsersCanShow(string mimeType) => BrowserMimeTypes.Contains(mimeType);

    /// <summary>Re-encodes a photo as a full size JPEG for viewing, turned upright.</summary>
    public static byte[] ToDisplayJpeg(byte[] original)
    {
        using var image = new MagickImage(original, ReadAs(Identify(original).Format));
        image.AutoOrient();
        image.BackgroundColor = MagickColors.White;
        image.Alpha(AlphaOption.Remove);
        image.Strip();
        image.Quality = 90;
        return image.ToByteArray(MagickFormat.Jpeg);
    }

    private static readonly byte[] JpegSignature = [0xFF, 0xD8, 0xFF];

    private static readonly byte[] PngSignature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>
    /// Recognises the supported formats from their leading bytes. ImageMagick is only ever given a file this
    /// accepts, and told which decoder to use, so it never guesses a format for untrusted bytes: its script and
    /// vector coders (MVG, MSL, SVG, ...) can read local files or fetch URLs.
    /// </summary>
    public static MagickFormat? DetectFormat(ReadOnlySpan<byte> data)
    {
        if (data.StartsWith(JpegSignature))
        {
            return MagickFormat.Jpeg;
        }

        if (data.StartsWith(PngSignature))
        {
            return MagickFormat.Png;
        }

        if (data.StartsWith("GIF87a"u8) || data.StartsWith("GIF89a"u8))
        {
            return MagickFormat.Gif;
        }

        if (data.StartsWith("BM"u8) && data.Length >= 26)
        {
            return MagickFormat.Bmp;
        }

        if (data.StartsWith("II*\0"u8) || data.StartsWith("MM\0*"u8))
        {
            return MagickFormat.Tiff;
        }

        if (data.Length >= 12 && data.StartsWith("RIFF"u8) && data[8..12].SequenceEqual("WEBP"u8))
        {
            return MagickFormat.WebP;
        }

        return DetectIsoMediaFormat(data);
    }

    /// <summary>HEIC, HEIF and AVIF are ISO media files named by the brands in their leading "ftyp" box.</summary>
    private static MagickFormat? DetectIsoMediaFormat(ReadOnlySpan<byte> data)
    {
        if (data.Length < 16 || !data[4..8].SequenceEqual("ftyp"u8))
        {
            return null;
        }

        var boxEnd = Math.Min(data.Length, (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(data), 1024));
        var brands = new List<string> { Encoding.ASCII.GetString(data[8..12]) };
        for (var offset = 16; offset + 4 <= boxEnd; offset += 4)
        {
            brands.Add(Encoding.ASCII.GetString(data[offset..(offset + 4)]));
        }

        if (brands.Any(brand => brand is "avif" or "avis"))
        {
            return MagickFormat.Avif;
        }

        if (brands.Any(brand => brand is "heic" or "heix" or "heim" or "heis" or "hevc" or "hevx" or "hevm" or "hevs"))
        {
            return MagickFormat.Heic;
        }

        return brands.Any(brand => brand is "mif1" or "msf1") ? MagickFormat.Heif : null;
    }

    /// <exception cref="InvalidImageException">The data is not an image in a supported format.</exception>
    internal static ImageDetails Identify(byte[] data)
    {
        var format = DetectFormat(data)
            ?? throw new InvalidImageException("The file is not a supported photo (JPEG, PNG, HEIC, WebP, AVIF, GIF, BMP or TIFF).");

        MagickImageInfo info;
        try
        {
            info = new MagickImageInfo(data, ReadAs(format));
        }
        catch (MagickException)
        {
            throw new InvalidImageException("The file is not a readable image.");
        }

        var type = SupportedFormats[format];
        if ((long)info.Width * info.Height > MaxSourcePixels)
        {
            throw new InvalidImageException($"The image is too large ({info.Width}×{info.Height}).");
        }

        return new ImageDetails(format, type.MimeType, type.Extension, info.Width, info.Height);
    }

    /// <summary>Read settings that pin ImageMagick to the decoder for the format the file was recognised as.</summary>
    private static MagickReadSettings ReadAs(MagickFormat format) => new() { Format = format };

    public PreparedImage Prepare(byte[] original)
    {
        var details = Identify(original);
        var budget = (long)options.Value.MaxImageTokens * TokenSize * TokenSize;

        if (details.Format is MagickFormat.Jpeg or MagickFormat.Png && (long)details.Width * details.Height <= budget)
        {
            return new PreparedImage(original, details.MimeType, (int)details.Width, (int)details.Height, Converted: false);
        }

        using var image = new MagickImage(original, ReadAs(details.Format));
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
