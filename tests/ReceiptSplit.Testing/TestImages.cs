using ImageMagick;

namespace ReceiptSplit.Testing;

public static class TestImages
{
    public static byte[] Create(uint width, uint height, MagickFormat format, ushort? exifOrientation = null)
    {
        using var image = new MagickImage(MagickColors.SkyBlue, width, height);
        if (exifOrientation is { } orientation)
        {
            // ImageMagick writes the image's Orientation into the EXIF profile on save, so set both.
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.Orientation, orientation);
            image.SetProfile(exif);
            image.Orientation = (OrientationType)orientation;
        }

        return image.ToByteArray(format);
    }
}
