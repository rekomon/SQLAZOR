using SQLAZOR.Models;
using SQLAZOR.Services;
using Xunit;

namespace SQLAZOR.Tests;

public sealed class UnifiedDiffTests
{
    [Fact]
    public void Identical_text_is_all_unchanged()
    {
        var lines = UnifiedDiff.Build("a\nb", "a\nb");

        Assert.All(lines, l => Assert.Equal(DiffKind.Unchanged, l.Kind));
        Assert.Equal(2, lines.Count);
    }

    [Fact]
    public void Added_line_is_reported_once()
    {
        var lines = UnifiedDiff.Build("a\nc", "a\nb\nc");

        var added = lines.Where(l => l.Kind == DiffKind.Added).ToList();
        Assert.Single(added);
        Assert.Equal("b", added[0].Text);
        Assert.Null(added[0].OldLineNumber);
        Assert.Equal(2, added[0].NewLineNumber);
    }

    [Fact]
    public void Removed_line_is_reported_once()
    {
        var lines = UnifiedDiff.Build("a\nb\nc", "a\nc");

        var removed = lines.Where(l => l.Kind == DiffKind.Removed).ToList();
        Assert.Single(removed);
        Assert.Equal("b", removed[0].Text);
        Assert.Equal(2, removed[0].OldLineNumber);
    }

    [Fact]
    public void Crlf_and_lf_produce_the_same_diff()
    {
        var lines = UnifiedDiff.Build("a\r\nb", "a\nb");

        Assert.All(lines, l => Assert.Equal(DiffKind.Unchanged, l.Kind));
    }

    [Fact]
    public void ToText_prefixes_changed_lines()
    {
        var text = UnifiedDiff.ToText("old", "new", "file.cs");

        Assert.Contains("--- file.cs", text);
        Assert.Contains("+++ file.cs", text);
        Assert.Contains("-old", text);
        Assert.Contains("+new", text);
    }
}

public sealed class GenerationMergerTests
{
    [Fact]
    public void First_generation_marks_everything_added()
    {
        var plan = GenerationMerger.Build([File("A.cs", "one")], [], manifest: null);

        Assert.Single(plan.Changes);
        Assert.Equal(GenerationChangeKind.Added, plan.Changes[0].Kind);
        Assert.Single(plan.Apply());
    }

    [Fact]
    public void Identical_output_is_unchanged()
    {
        var current = File("A.cs", "one");
        var plan = GenerationMerger.Build([File("A.cs", "one")], [current], Manifest(("A.cs", "one")));

        Assert.Equal(GenerationChangeKind.Unchanged, plan.Changes[0].Kind);
        Assert.Same(current, plan.Apply().Single());
    }

    [Fact]
    public void Generator_change_without_local_edits_is_an_automatic_update()
    {
        var plan = GenerationMerger.Build([File("A.cs", "two")], [File("A.cs", "one")], Manifest(("A.cs", "one")));

        Assert.Equal(GenerationChangeKind.Updated, plan.Changes[0].Kind);
        Assert.Equal("two", plan.Apply().Single().Content);
    }

    [Fact]
    public void Local_edit_with_unchanged_generator_is_kept_silently()
    {
        var current = File("A.cs", "mine");
        var plan = GenerationMerger.Build([File("A.cs", "one")], [current], Manifest(("A.cs", "one")));

        Assert.Equal(GenerationChangeKind.LocalEdit, plan.Changes[0].Kind);
        Assert.False(plan.HasConflicts);
        Assert.Same(current, plan.Apply().Single());
    }

    [Fact]
    public void Both_changed_is_a_conflict_that_defaults_to_current()
    {
        var current = File("A.cs", "mine");
        var plan = GenerationMerger.Build([File("A.cs", "generated")], [current], Manifest(("A.cs", "one")));

        Assert.Equal(GenerationChangeKind.Conflict, plan.Changes[0].Kind);
        Assert.True(plan.HasConflicts);
        Assert.False(plan.Changes[0].UseGenerator);
        Assert.Same(current, plan.Apply().Single());

        plan.Changes[0].UseGenerator = true;
        Assert.Equal("generated", plan.Apply().Single().Content);
    }

    [Fact]
    public void Existing_local_file_without_baseline_is_a_conflict()
    {
        var current = File("A.cs", "manual");
        var plan = GenerationMerger.Build([File("A.cs", "generated")], [current], manifest: null);

        Assert.Equal(GenerationChangeKind.Conflict, plan.Changes[0].Kind);
        Assert.Same(current, plan.Apply().Single());
    }

    [Fact]
    public void Missing_from_generation_is_kept_by_default_and_can_be_removed()
    {
        var current = File("Old.cs", "old");
        var plan = GenerationMerger.Build([], [current], Manifest(("Old.cs", "old")));

        Assert.Equal(GenerationChangeKind.Removed, plan.Changes[0].Kind);
        Assert.True(plan.HasConflicts);
        Assert.Same(current, plan.Apply().Single());

        plan.Changes[0].UseGenerator = true;
        Assert.Empty(plan.Apply());
    }

    [Fact]
    public void Files_not_in_manifest_and_not_regenerated_are_preserved()
    {
        var manual = File("Manual.cs", "hand written");
        var plan = GenerationMerger.Build([File("A.cs", "one")], [manual], manifest: null);

        Assert.Single(plan.UserOnlyFiles);
        Assert.Contains(plan.Apply(), f => f.RelativePath == "Manual.cs" && f.Content == "hand written");
    }

    [Fact]
    public void Manifest_hashes_ignore_line_ending_style()
    {
        var manifest = GenerationMerger.BuildManifest("db", "Shop", [File("A.cs", "a\r\nb")]);

        Assert.Equal(ContentHash.Compute("a\nb"), manifest.Files.Single().Sha256);
    }

    private static GeneratedFile File(string path, string content) => new() { RelativePath = path, Content = content };

    private static GenerationManifest Manifest(params (string Path, string Content)[] files) => new()
    {
        DatabaseKey = "db",
        Files = files.Select(f => new GeneratedFileSnapshot
        {
            RelativePath = f.Path,
            Sha256 = ContentHash.Compute(f.Content)
        }).ToList()
    };
}

public sealed class FileGenerationManifestStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "sqlazor-manifest-tests-" + Guid.NewGuid().ToString("N"));

    private FileGenerationManifestStore Store => new(_directory);

    [Fact]
    public async Task Save_then_load_round_trips()
    {
        var manifest = new GenerationManifest
        {
            DatabaseKey = "server|shop",
            DatabaseName = "Shop",
            Files = [new GeneratedFileSnapshot { RelativePath = "A.cs", Sha256 = "hash" }]
        };

        await Store.SaveAsync(manifest);
        var loaded = await Store.LoadAsync("server|shop");

        Assert.NotNull(loaded);
        Assert.Equal("Shop", loaded!.DatabaseName);
        Assert.Single(loaded.Files);
    }

    [Fact]
    public async Task Load_for_a_different_database_returns_null()
    {
        await Store.SaveAsync(new GenerationManifest { DatabaseKey = "server|shop" });

        Assert.Null(await Store.LoadAsync("server|other"));
    }

    [Fact]
    public async Task Delete_removes_the_manifest()
    {
        await Store.SaveAsync(new GenerationManifest { DatabaseKey = "server|shop" });

        await Store.DeleteAsync("server|shop");

        Assert.Null(await Store.LoadAsync("server|shop"));
    }

    [Fact]
    public async Task Malformed_manifest_is_ignored()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, ContentHash.Compute("server|shop").ToLowerInvariant() + ".json");
        await System.IO.File.WriteAllTextAsync(path, "{ broken");

        Assert.Null(await Store.LoadAsync("server|shop"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
