using System.Text.RegularExpressions;
using SQLAZOR.Models;
using Xunit;
using Xunit.Sdk;

namespace SQLAZOR.Tests;

/// <summary>
/// File-based snapshot comparison. Expected files live in <c>SQLAZOR.Tests/Snapshots/{suite}/</c>
/// and are approved by copying a <c>.received</c> file over its expected counterpart.
/// </summary>
internal static class SnapshotAssert
{
    private static readonly Regex GuidPattern = new(
        @"[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}",
        RegexOptions.Compiled);

    private static readonly Regex DatePattern = new(@"\b\d{4}-\d{2}-\d{2}\b", RegexOptions.Compiled);

    private static readonly string SnapshotRoot = FindSnapshotRoot();

    public static void MatchFiles(string suite, IEnumerable<GeneratedFile> files)
    {
        var created = new List<string>();
        var mismatched = new List<string>();

        foreach (var file in files)
        {
            var expectedPath = Path.Combine(
                SnapshotRoot,
                suite,
                file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(expectedPath)!);

            var actual = Normalize(file.Content);

            if (!File.Exists(expectedPath))
            {
                File.WriteAllText(expectedPath, actual);
                created.Add(file.RelativePath);
                continue;
            }

            var expected = Normalize(File.ReadAllText(expectedPath));
            if (string.Equals(expected, actual, StringComparison.Ordinal))
                continue;

            var receivedPath = expectedPath + ".received";
            File.WriteAllText(receivedPath, actual);
            mismatched.Add(file.RelativePath);
        }

        if (created.Count == 0 && mismatched.Count == 0)
            return;

        var message = BuildMessage(created, mismatched);
        throw new XunitException(message);
    }

    private static string BuildMessage(IReadOnlyCollection<string> created, IReadOnlyCollection<string> mismatched)
    {
        var lines = new List<string>();

        if (created.Count > 0)
        {
            lines.Add($"{created.Count} snapshot(s) did not exist and were written. Review and re-run:");
            lines.AddRange(created.Select(p => "  created " + p));
        }

        if (mismatched.Count > 0)
        {
            lines.Add($"{mismatched.Count} snapshot(s) differ. Compare the .received files and approve the intended changes:");
            lines.AddRange(mismatched.Select(p => "  changed " + p));
        }

        lines.Add("Snapshot root: " + SnapshotRoot);
        return string.Join(Environment.NewLine, lines);
    }

    private static string Normalize(string text) =>
        DatePattern
            .Replace(GuidPattern.Replace(text, "<guid>"), "<date>")
            .Replace("\r\n", "\n");

    private static string FindSnapshotRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SQLAZOR.slnx")))
                return Path.Combine(directory.FullName, "SQLAZOR.Tests", "Snapshots");

            directory = directory.Parent;
        }

        return Path.Combine(AppContext.BaseDirectory, "Snapshots");
    }
}
