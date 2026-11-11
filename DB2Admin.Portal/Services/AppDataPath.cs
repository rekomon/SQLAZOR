namespace SQLAZOR.Services;

/// <summary>
/// Resolves the base directory for SQLAZOR's on-disk stores. When the process
/// runs under an identity whose profile maps to the Windows system profile
/// (services, IIS app pools), LocalApplicationData points at a path that is
/// typically denied, so candidates are probed for writability until one works.
/// A configured override wins; IIS deployments should set one explicitly
/// (SQLAZOR:DataDirectory) because app pool identities can write to neither
/// their virtual profile nor the site folder by default.
/// </summary>
public static class AppDataPath
{
    private static string? _overrideDirectory;
    private static string? _contentRoot;

    /// <summary>
    /// Called once at startup. <paramref name="overrideDirectory"/> comes from
    /// configuration (SQLAZOR:DataDirectory / SQLAZOR__DataDirectory) and, when
    /// set, must be writable - a broken override is reported instead of silently
    /// redirecting data somewhere unexpected.
    /// </summary>
    public static void Configure(string? overrideDirectory, string? contentRoot = null)
    {
        _overrideDirectory = string.IsNullOrWhiteSpace(overrideDirectory) ? null : overrideDirectory;
        _contentRoot = contentRoot;
    }

    public static string BaseDirectory()
    {
        foreach (var candidate in Candidates())
        {
            if (IsWritable(candidate))
                return candidate;
        }

        throw new InvalidOperationException(
            "No writable SQLAZOR data directory was found. The current identity " +
            $"(user '{Environment.UserName}') cannot write to its local application data, " +
            "the application folder, or the temp folder. Set 'SQLAZOR:DataDirectory' in " +
            "appsettings.json (or the SQLAZOR__DataDirectory environment variable) to a " +
            "folder this identity can write to, or enable 'Load User Profile' on the IIS " +
            "application pool.");
    }

    private static IEnumerable<string> Candidates()
    {
        if (_overrideDirectory is not null)
            yield return _overrideDirectory;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData) && !IsSystemProfile(localAppData) && seen.Add(localAppData))
            yield return localAppData;

        var userProfile = Environment.GetEnvironmentVariable("USERPROFILE");
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            var fromUserProfile = Path.Combine(userProfile, "AppData", "Local");
            if (!IsSystemProfile(fromUserProfile) && seen.Add(fromUserProfile))
                yield return fromUserProfile;
        }

        if (!string.IsNullOrWhiteSpace(_contentRoot))
        {
            var appData = Path.Combine(_contentRoot, "App_Data");
            if (seen.Add(appData))
                yield return appData;
        }

        if (seen.Add(AppContext.BaseDirectory))
            yield return AppContext.BaseDirectory;

        var temp = Path.GetTempPath();
        if (!IsSystemProfile(temp))
            yield return Path.Combine(temp, "SQLAZOR");
    }

    private static bool IsSystemProfile(string path) =>
        path.Contains("systemprofile", StringComparison.OrdinalIgnoreCase);

    private static bool IsWritable(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var probe = Path.Combine(directory, ".sqlazor-write-test");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
