namespace ReceiptSplit.Data;

/// <summary>Where the SQLite database and uploaded receipt photos live.</summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>
    /// Resolved against the content root (the project folder when run from source) when relative. Development points
    /// it at data/ in the repository root, which on a case-insensitive file system would otherwise collide with the
    /// project's Data/ source folder.
    /// </summary>
    public string Root { get; set; } = "data";

    public string DatabasePath => Path.Combine(Root, "receiptsplit.db");

    public string UploadsPath => Path.Combine(Root, "uploads");

    public string GetUploadPath(string storedFileName) => Path.Combine(UploadsPath, storedFileName);

    /// <summary>JPEG copy of an upload in a format browsers can't show (HEIC, TIFF), made the first time it's viewed.</summary>
    public string GetDisplayCopyPath(string storedFileName) =>
        Path.Combine(UploadsPath, Path.GetFileNameWithoutExtension(storedFileName) + ".display.jpg");
}
