namespace ReceiptSplit.Tests.Support;

internal static class TestPaths
{
    public static string RepoRoot { get; } = FindRepoRoot();

    /// <summary>Real receipt photos and model outputs; gitignored, so tests that need it skip when it's missing.</summary>
    public static string Samples => Path.Combine(RepoRoot, "samples");

    public static bool SamplesAvailable => Directory.Exists(Samples);

    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ReceiptSplit.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Could not find the repository root (ReceiptSplit.slnx).");
    }
}
