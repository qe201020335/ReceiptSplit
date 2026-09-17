using System.Text;
using ImageMagick;
using ReceiptSplit.Extraction;
using ReceiptSplit.Options;
using ReceiptSplit.Tests.Support;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace ReceiptSplit.Tests;

public class ImagePreparerTests
{
    // 16 tokens × 32 × 32 = 16,384 px keeps the test images tiny.
    private readonly ImagePreparer _preparer = new(MsOptions.Create(new LlmOptions { MaxImageTokens = 16 }));

    [Fact]
    public void Sends_a_jpeg_within_budget_unchanged_even_if_exif_says_rotated()
    {
        var original = TestImages.Create(120, 80, MagickFormat.Jpeg, exifOrientation: 6);
        using (var check = new MagickImage(original))
        {
            Assert.Equal(OrientationType.RightTop, check.Orientation);
        }

        var prepared = _preparer.Prepare(original);

        Assert.False(prepared.Converted);
        Assert.Equal(original, prepared.Data);
        Assert.Equal(("image/jpeg", 120, 80), (prepared.MimeType, prepared.Width, prepared.Height));
    }

    [Fact]
    public void Sends_a_png_within_budget_unchanged()
    {
        var original = TestImages.Create(100, 100, MagickFormat.Png);

        var prepared = _preparer.Prepare(original);

        Assert.False(prepared.Converted);
        Assert.Equal(original, prepared.Data);
        Assert.Equal("image/png", prepared.MimeType);
    }

    [Fact]
    public void Shrinks_an_oversized_image_to_whole_tokens_within_budget()
    {
        var prepared = _preparer.Prepare(TestImages.Create(400, 300, MagickFormat.Png));

        Assert.True(prepared.Converted);
        Assert.Equal("image/jpeg", prepared.MimeType);
        Assert.Equal((128, 96), (prepared.Width, prepared.Height));
        AssertJpeg(prepared, 128, 96);
    }

    [Fact]
    public void Auto_orients_an_oversized_image_before_shrinking()
    {
        var prepared = _preparer.Prepare(TestImages.Create(400, 300, MagickFormat.Jpeg, exifOrientation: 6));

        Assert.Equal((96, 128), (prepared.Width, prepared.Height));
        AssertJpeg(prepared, 96, 128);
    }

    [Fact]
    public void Converts_formats_llama_cpp_cannot_decode()
    {
        var prepared = _preparer.Prepare(TestImages.Create(64, 48, MagickFormat.WebP));

        Assert.True(prepared.Converted);
        AssertJpeg(prepared, 64, 48);
    }

    [Fact]
    public void Identify_detects_the_real_format()
    {
        var details = ImagePreparer.Identify(TestImages.Create(64, 48, MagickFormat.Png));

        Assert.Equal(("image/png", ".png", 64u, 48u), (details.MimeType, details.Extension, details.Width, details.Height));
    }

    [Theory]
    [InlineData(MagickFormat.Jpeg)]
    [InlineData(MagickFormat.Png)]
    [InlineData(MagickFormat.Gif)]
    [InlineData(MagickFormat.Bmp)]
    [InlineData(MagickFormat.Tiff)]
    [InlineData(MagickFormat.WebP)]
    [InlineData(MagickFormat.Avif)]
    public void Recognises_supported_formats_by_their_signature(MagickFormat format)
    {
        var data = TestImages.Create(64, 48, format);

        Assert.Equal(format, ImagePreparer.DetectFormat(data));
        Assert.Equal((64u, 48u), (ImagePreparer.Identify(data).Width, ImagePreparer.Identify(data).Height));
    }

    [Theory]
    // Magick.NET can't write HEIC to build a real one, so these are the leading "ftyp" boxes of iPhone and camera files.
    [InlineData("heic", "mif1", MagickFormat.Heic)]
    [InlineData("mif1", "heic", MagickFormat.Heic)]
    [InlineData("mif1", "miaf", MagickFormat.Heif)]
    [InlineData("mif1", "avif", MagickFormat.Avif)]
    [InlineData("isom", "mp42", null)]
    public void Recognises_iso_media_photos_by_their_brands(string majorBrand, string compatibleBrand, MagickFormat? expected)
    {
        byte[] header = [0, 0, 0, 24, .. "ftyp"u8, .. Encoding.ASCII.GetBytes(majorBrand), 0, 0, 0, 0, .. Encoding.ASCII.GetBytes(compatibleBrand), 0, 0, 0, 0];

        Assert.Equal(expected, ImagePreparer.DetectFormat(header));
    }

    [Theory]
    // ImageMagick scripts and vector formats that can read local files or fetch URLs when decoded.
    [InlineData("push graphic-context\nviewbox 0 0 64 64\nimage over 0,0 0,0 'text:/etc/passwd'\npop graphic-context")]
    [InlineData("""<?xml version="1.0"?><image><read filename="/etc/passwd"/></image>""")]
    [InlineData("""<svg xmlns="http://www.w3.org/2000/svg"><image href="file:///etc/passwd"/></svg>""")]
    public void Never_hands_unrecognised_content_to_image_magick(string content)
    {
        var data = Encoding.UTF8.GetBytes(content);

        Assert.Null(ImagePreparer.DetectFormat(data));
        Assert.Throws<InvalidImageException>(() => ImagePreparer.Identify(data));
    }

    [Fact]
    public void Rejects_a_known_signature_that_is_not_really_that_image()
    {
        byte[] fakePng = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, .. "<svg xmlns='http://www.w3.org/2000/svg'/>"u8];

        Assert.Equal(MagickFormat.Png, ImagePreparer.DetectFormat(fakePng));
        Assert.Throws<InvalidImageException>(() => ImagePreparer.Identify(fakePng));
    }

    [Theory]
    [InlineData("definitely not an image")]
    [InlineData("""<svg xmlns="http://www.w3.org/2000/svg" width="10" height="10"></svg>""")]
    public void Identify_rejects_non_photos(string content)
    {
        Assert.Throws<InvalidImageException>(() => ImagePreparer.Identify(Encoding.UTF8.GetBytes(content)));
    }

    private static void AssertJpeg(PreparedImage prepared, uint width, uint height)
    {
        var info = new MagickImageInfo(prepared.Data);
        Assert.Equal((MagickFormat.Jpeg, width, height), (info.Format, info.Width, info.Height));
    }
}
