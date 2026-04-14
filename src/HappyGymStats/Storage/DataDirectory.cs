using System.Diagnostics;

namespace HappyGymStats.Storage;

public static class DataDirectory
{
    private const string LocalDataDirName = "data";

    /// <summary>
    /// Resolve the base data directory.
    ///
    /// Preference order:
    /// 1) AppContext.BaseDirectory/data (portable deployments when writable)
    /// 2) ./data (developer-local / working dir)
    /// 3) LocalApplicationData/{appName} (user-writable fallback)
    /// </summary>
    public static string ResolveBasePath(string appName)
    {
        // 1) AppContext.BaseDirectory/data
        var baseDirCandidate = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, LocalDataDirName));
        if (TryEnsureWritableDirectory(baseDirCandidate))
            return baseDirCandidate;

        // 2) ./data
        var cwdCandidate = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, LocalDataDirName));
        if (TryEnsureWritableDirectory(cwdCandidate))
            return cwdCandidate;

        // 3) LocalApplicationData
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var fallback = Path.Combine(root, appName);

        if (!TryEnsureWritableDirectory(fallback))
        {
            throw new IOException(
                $"Unable to find a writable data directory. Tried '{baseDirCandidate}', '{cwdCandidate}', and '{fallback}'.");
        }

        return fallback;
    }

    private static bool TryEnsureWritableDirectory(string path)
    {
        try
        {
            Directory.CreateDirectory(path);

            // Make a best-effort write test.
            // Using a deterministic-ish prefix helps debugging without leaking secrets.
            var probePath = Path.Combine(path, $".write-probe-{Process.GetCurrentProcess().Id}-{Environment.CurrentManagedThreadId}.tmp");
            File.WriteAllText(probePath, "probe");
            File.Delete(probePath);

            return true;
        }
        catch
        {
            return false;
        }
    }
}
