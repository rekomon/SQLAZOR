using SQLAZOR.Models;

namespace SQLAZOR.Services;

public enum GenerationChangeKind
{
    Unchanged,
    Added,
    Updated,
    Conflict,
    Removed,
    LocalEdit
}

/// <summary>One path that differs between the previous generation, the current files, and the new run.</summary>
public sealed class GenerationFileChange
{
    public required string RelativePath { get; init; }
    public required GenerationChangeKind Kind { get; init; }
    public GeneratedFile? GeneratorFile { get; init; }
    public GeneratedFile? CurrentFile { get; init; }

    /// <summary>True = take the newly generated file; false = keep what is on disk now.</summary>
    public bool UseGenerator { get; set; }
}

public sealed class GenerationMergePlan
{
    public List<GenerationFileChange> Changes { get; init; } = [];
    public List<GeneratedFile> UserOnlyFiles { get; init; } = [];

    public bool HasConflicts => Changes.Any(c => c.Kind is GenerationChangeKind.Conflict or GenerationChangeKind.Removed);
    public int AddedCount => Changes.Count(c => c.Kind == GenerationChangeKind.Added);
    public int UpdatedCount => Changes.Count(c => c.Kind == GenerationChangeKind.Updated);
    public int ConflictCount => Changes.Count(c => c.Kind == GenerationChangeKind.Conflict);
    public int RemovedCount => Changes.Count(c => c.Kind == GenerationChangeKind.Removed);
    public int LocalEditCount => Changes.Count(c => c.Kind == GenerationChangeKind.LocalEdit);

    /// <summary>Files to show in the Results tab after the user decides.</summary>
    public List<GeneratedFile> Apply()
    {
        var result = new List<GeneratedFile>(Changes.Count + UserOnlyFiles.Count);

        foreach (var change in Changes)
        {
            var file = change.Kind switch
            {
                GenerationChangeKind.Added => change.GeneratorFile,
                GenerationChangeKind.Unchanged => change.CurrentFile ?? change.GeneratorFile,
                GenerationChangeKind.Updated => change.GeneratorFile,
                GenerationChangeKind.LocalEdit => change.CurrentFile ?? change.GeneratorFile,
                GenerationChangeKind.Conflict => change.UseGenerator ? change.GeneratorFile : change.CurrentFile,
                GenerationChangeKind.Removed => change.UseGenerator ? null : change.CurrentFile,
                _ => change.CurrentFile ?? change.GeneratorFile
            };

            if (file is not null)
                result.Add(file);
        }

        result.AddRange(UserOnlyFiles);
        return result;
    }
}

/// <summary>
/// Three-way merge between the last generator output (manifest), the files currently in the
/// workspace, and a fresh generation. The manifest is what lets us distinguish a schema-driven
/// change from a hand edit instead of guessing from timestamps.
/// </summary>
public static class GenerationMerger
{
    public static GenerationMergePlan Build(
        IEnumerable<GeneratedFile> generatedFiles,
        IEnumerable<GeneratedFile> currentFiles,
        GenerationManifest? manifest)
    {
        var current = currentFiles
            .GroupBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);

        var baseline = manifest?.Files
            .GroupBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last().Sha256, StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var plan = new GenerationMergePlan();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in generatedFiles)
        {
            if (!seen.Add(file.RelativePath))
                continue;

            var generatedHash = ContentHash.Compute(file.Content);

            if (!current.TryGetValue(file.RelativePath, out var existing))
            {
                plan.Changes.Add(new GenerationFileChange
                {
                    RelativePath = file.RelativePath,
                    Kind = GenerationChangeKind.Added,
                    GeneratorFile = file,
                    UseGenerator = true
                });
                continue;
            }

            var currentHash = ContentHash.Compute(existing.Content);
            if (currentHash == generatedHash)
            {
                plan.Changes.Add(new GenerationFileChange
                {
                    RelativePath = file.RelativePath,
                    Kind = GenerationChangeKind.Unchanged,
                    GeneratorFile = file,
                    CurrentFile = existing,
                    UseGenerator = true
                });
                continue;
            }

            if (baseline.TryGetValue(file.RelativePath, out var baselineHash))
            {
                if (baselineHash == currentHash)
                {
                    // Only the generator changed - safe to overwrite without asking.
                    plan.Changes.Add(new GenerationFileChange
                    {
                        RelativePath = file.RelativePath,
                        Kind = GenerationChangeKind.Updated,
                        GeneratorFile = file,
                        CurrentFile = existing,
                        UseGenerator = true
                    });
                }
                else if (baselineHash == generatedHash)
                {
                    // Only the file on disk changed - keep the user's edit, no conflict.
                    plan.Changes.Add(new GenerationFileChange
                    {
                        RelativePath = file.RelativePath,
                        Kind = GenerationChangeKind.LocalEdit,
                        GeneratorFile = file,
                        CurrentFile = existing,
                        UseGenerator = false
                    });
                }
                else
                {
                    // Both sides changed since the last run.
                    plan.Changes.Add(new GenerationFileChange
                    {
                        RelativePath = file.RelativePath,
                        Kind = GenerationChangeKind.Conflict,
                        GeneratorFile = file,
                        CurrentFile = existing,
                        UseGenerator = false
                    });
                }
            }
            else
            {
                // The generator now produces a path that already existed locally (e.g. added from chat).
                plan.Changes.Add(new GenerationFileChange
                {
                    RelativePath = file.RelativePath,
                    Kind = GenerationChangeKind.Conflict,
                    GeneratorFile = file,
                    CurrentFile = existing,
                    UseGenerator = false
                });
            }
        }

        foreach (var existing in current.Values)
        {
            if (seen.Contains(existing.RelativePath))
                continue;

            if (baseline.ContainsKey(existing.RelativePath))
            {
                plan.Changes.Add(new GenerationFileChange
                {
                    RelativePath = existing.RelativePath,
                    Kind = GenerationChangeKind.Removed,
                    CurrentFile = existing,
                    UseGenerator = false
                });
            }
            else
            {
                plan.UserOnlyFiles.Add(existing);
            }
        }

        plan.Changes.Sort((a, b) => string.Compare(a.RelativePath, b.RelativePath, StringComparison.OrdinalIgnoreCase));
        return plan;
    }

    public static GenerationManifest BuildManifest(string databaseKey, string databaseName, IEnumerable<GeneratedFile> generatedFiles) => new()
    {
        DatabaseKey = databaseKey,
        DatabaseName = databaseName,
        GeneratedAt = DateTimeOffset.Now,
        Files = generatedFiles
            .GroupBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Select(g => new GeneratedFileSnapshot
            {
                RelativePath = g.Key,
                Sha256 = ContentHash.Compute(g.Last().Content)
            })
            .OrderBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList()
    };
}
