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
