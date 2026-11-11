using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using SQLAZOR.Models;

namespace SQLAZOR.Services;

/// <summary>
/// Writes the generated files to a throwaway directory and runs `dotnet build` on the solution or
/// project. This needs the .NET SDK and (on first use) access to NuGet packages, so it is always
/// a separate, explicitly-triggered action rather than part of generation.
/// </summary>
public sealed partial class DotnetBuildValidator : IDeepCompileValidator
{
    private static readonly TimeSpan BuildTimeout = TimeSpan.FromMinutes(3);
    private const int MaxDiagnostics = 200;

    public bool CanCompile(IReadOnlyList<GeneratedFile> files) =>
        files.Any(f => f.RelativePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase));

    public async Task<GeneratedCodeValidationReport> CompileAsync(IReadOnlyList<GeneratedFile> files, CancellationToken ct = default)
    {
        if (!CanCompile(files))
        {
            return new GeneratedCodeValidationReport
            {
                Diagnostics =
                [
                    new ValidationDiagnostic
                    {
                        FilePath = "project",
                        Message = "Deep check needs a generated .csproj — enable 'Generate a full runnable project' and regenerate."
                    }
                ]
            };
        }

        var root = Path.Combine(Path.GetTempPath(), "sqlazor-build-" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(root);
            await WriteFilesAsync(root, files, ct);

            var target = FindBuildTarget(root);
            if (target is null)
            {
                return new GeneratedCodeValidationReport
                {
                    Diagnostics = [new ValidationDiagnostic { FilePath = "project", Message = "No .sln or .csproj was written to the build directory." }]
                };
            }

            var (exitCode, output) = await RunBuildAsync(root, target, ct);
            var report = ParseBuildOutput(output, root, files.Count);

            if (exitCode != 0 && report.ErrorCount == 0)
            {
                report.Diagnostics.Add(new ValidationDiagnostic
                {
                    FilePath = "build",
                    Message = $"dotnet build exited with code {exitCode}: {TailLines(output, 8)}"
                });
            }

            return report;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new GeneratedCodeValidationReport
            {
                Diagnostics =
                [
                    new ValidationDiagnostic
                    {
                        FilePath = "build",
                        Message = $"dotnet build could not run: {ex.Message}"
                    }
                ]
            };
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    /// <summary>Parses MSBuild/compiler output. Exposed for tests so the real SDK is not required.</summary>
    public static GeneratedCodeValidationReport ParseBuildOutput(
        string output, string root, int filesChecked = 0)
    {
        var diagnostics = new List<ValidationDiagnostic>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var checkedCount = filesChecked;

        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var match = DiagnosticWithLocation().Match(line);
            string filePath;
            var lineNumber = 0;
            var column = 0;
            string? id;
            string message;
            ValidationSeverity severity;

            if (match.Success)
            {
                filePath = NormalizePath(match.Groups["file"].Value, root);
                int.TryParse(match.Groups["line"].Value, out lineNumber);
                int.TryParse(match.Groups["col"].Value, out column);
                severity = match.Groups["sev"].Value.Equals("warning", StringComparison.OrdinalIgnoreCase)
                    ? ValidationSeverity.Warning
                    : ValidationSeverity.Error;
                id = match.Groups["id"].Success ? match.Groups["id"].Value : null;
                message = StripTrailingProjectTag(match.Groups["msg"].Value);
            }
            else
            {
                var bare = DiagnosticWithoutLocation().Match(line);
                if (!bare.Success)
                    continue;

                filePath = "build";
                severity = bare.Groups["sev"].Value.Equals("warning", StringComparison.OrdinalIgnoreCase)
                    ? ValidationSeverity.Warning
                    : ValidationSeverity.Error;
                id = bare.Groups["id"].Success ? bare.Groups["id"].Value : null;
                message = StripTrailingProjectTag(bare.Groups["msg"].Value);
            }

            var key = $"{filePath}|{lineNumber}|{column}|{id}|{message}";
            if (!seen.Add(key))
                continue;

            diagnostics.Add(new ValidationDiagnostic
            {
                FilePath = filePath,
                Line = lineNumber,
                Column = column,
                Severity = severity,
                Id = id,
                Message = message
            });

            if (diagnostics.Count >= MaxDiagnostics)
                break;
        }

        return new GeneratedCodeValidationReport
        {
            Diagnostics = diagnostics
                .OrderByDescending(d => d.Severity)
                .ThenBy(d => d.FilePath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(d => d.Line)
                .ToList(),
            FilesChecked = checkedCount,
            FilesSkipped = 0
        };
    }

    private static async Task WriteFilesAsync(string root, IReadOnlyList<GeneratedFile> files, CancellationToken ct)
    {
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();

            var relative = file.RelativePath.Replace('\\', '/').TrimStart('/');
            if (string.IsNullOrWhiteSpace(relative))
                continue;

            var segments = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0 || segments.Any(s => s is ".." or ".") || Path.IsPathRooted(relative))
                continue;

            var fullPath = Path.Combine(root, Path.Combine(segments));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            await File.WriteAllTextAsync(fullPath, file.Content, ct);
        }
    }

    private static string? FindBuildTarget(string root)
    {
        var solution = Directory.EnumerateFiles(root, "*.sln", SearchOption.TopDirectoryOnly).FirstOrDefault()
                       ?? Directory.EnumerateFiles(root, "*.slnx", SearchOption.TopDirectoryOnly).FirstOrDefault();

        if (solution is not null)
            return solution;

        return Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories).FirstOrDefault();
    }

    private static async Task<(int ExitCode, string Output)> RunBuildAsync(string root, string target, CancellationToken ct)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("build");
        startInfo.ArgumentList.Add(target);
        startInfo.ArgumentList.Add("--nologo");
        startInfo.ArgumentList.Add("-v:minimal");
        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        startInfo.Environment["DOTNET_NOLOGO"] = "1";
        startInfo.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";

        using var process = new Process { StartInfo = startInfo };
        var output = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                output.AppendLine(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                output.AppendLine(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(BuildTimeout);

        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }

            throw;
        }

        // Flush the async readers before returning the captured text.
        process.WaitForExit();
        return (process.ExitCode, output.ToString());
    }

    private static string NormalizePath(string raw, string root)
    {
        var cleaned = raw.Trim().TrimEnd(':');
        if (cleaned.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            cleaned = Path.GetRelativePath(root, cleaned);

        return cleaned.Replace('\\', '/');
    }

    private static string StripTrailingProjectTag(string message) =>
        Regex.Replace(message, @"\s*\[[^\]]+\]\s*$", string.Empty).Trim();

    private static string TailLines(string output, int count)
    {
        var lines = output
            .Split('\n')
            .Select(l => l.TrimEnd('\r').Trim())
            .Where(l => l.Length > 0)
            .TakeLast(count)
            .ToList();

        return lines.Count == 0 ? "(no output)" : string.Join(" | ", lines);
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Temp files from a failed build can stay locked briefly; the OS will clean %TEMP% eventually.
        }
    }

    [GeneratedRegex(@"^\s*(?<file>.+?)\((?<line>\d+),(?<col>\d+)\):\s*(?<sev>error|warning)\s+(?<id>[A-Za-z]+\d+)?\s*:?\s*(?<msg>.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex DiagnosticWithLocation();

    [GeneratedRegex(@"^\s*(?<sev>error|warning)\s+(?<id>[A-Za-z]+\d+)?\s*:?\s*(?<msg>.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex DiagnosticWithoutLocation();
}
