namespace McServerManager.Tests.TestInfrastructure;

internal sealed class TemporaryDirectoryScope : IDisposable
{
    private static readonly string TestRootPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "McServerManagerTests");

    public TemporaryDirectoryScope()
    {
        CleanupStaleDirectories();
        Path = System.IO.Path.Combine(TestRootPath, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }

            if (Directory.Exists(TestRootPath))
            {
                using var entries = Directory.EnumerateFileSystemEntries(TestRootPath).GetEnumerator();
                if (!entries.MoveNext())
                {
                    Directory.Delete(TestRootPath);
                }
            }
        }
        catch
        {
            // Best effort cleanup for test temp files.
        }
    }

    private static void CleanupStaleDirectories()
    {
        try
        {
            if (!Directory.Exists(TestRootPath))
            {
                return;
            }

            var cutoff = DateTime.UtcNow.AddDays(-2);
            foreach (var directory in Directory.EnumerateDirectories(TestRootPath))
            {
                try
                {
                    if (Directory.GetLastWriteTimeUtc(directory) < cutoff)
                    {
                        Directory.Delete(directory, recursive: true);
                    }
                }
                catch
                {
                    // Ignore and continue; best effort only.
                }
            }
        }
        catch
        {
            // Ignore and continue; best effort only.
        }
    }
}
