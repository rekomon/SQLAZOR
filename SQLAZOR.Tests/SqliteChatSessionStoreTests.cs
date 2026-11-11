using SQLAZOR.Models;
using SQLAZOR.Services;
using Xunit;

namespace SQLAZOR.Tests;

public sealed class SqliteChatSessionStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sqlazor-sqlite-tests-" + Guid.NewGuid().ToString("N"));
    private readonly string _dbPath;
    private readonly SqliteChatSessionStore _store;

    public SqliteChatSessionStoreTests()
    {
        _dbPath = Path.Combine(_dir, "chat.db");
        _store = new SqliteChatSessionStore(_dbPath);
    }

    [Fact]
    public async Task Save_then_load_round_trips_the_session()
    {
        var session = NewSession("First chat");
        session.Messages.Add(new ChatMessage
        {
            Role = "assistant",
            Content = "Here:\n```sql\nSELECT 1;\n```",
            Attachments = [new ChatAttachment { FileName = "Report.cs", Content = "class Report { }", SizeBytes = 18 }]
        });

        await _store.SaveAsync(session);
        var loaded = await _store.LoadAsync(session.Id);

        Assert.NotNull(loaded);
        Assert.Equal("First chat", loaded!.Title);
        Assert.Equal(2, loaded.Messages.Count);
        Assert.Equal("Report.cs", loaded.Messages[1].Attachments[0].FileName);
        Assert.Equal("ShopDb", loaded.DatabaseName);
    }

    [Fact]
    public async Task Save_replaces_an_existing_row()
    {
        var session = NewSession("First chat");
        await _store.SaveAsync(session);

        session.Title = "Renamed";
        await _store.SaveAsync(session);

        var summaries = await _store.ListAsync();
        Assert.Single(summaries);
        Assert.Equal("Renamed", summaries[0].Title);
    }

    [Fact]
    public async Task List_orders_pinned_first_then_newest()
    {
        var older = NewSession("Older");
        await _store.SaveAsync(older);
        await Task.Delay(30);

        var newer = NewSession("Newer");
        await _store.SaveAsync(newer);
        await Task.Delay(30);

        older.Pinned = true;
        await _store.SaveAsync(older);

        var summaries = await _store.ListAsync();

        Assert.Equal(2, summaries.Count);
        Assert.Equal("Older", summaries[0].Title);
        Assert.True(summaries[0].Pinned);
        Assert.Equal("Newer", summaries[1].Title);
    }

    [Theory]
    [InlineData("orders query")]      // message content
    [InlineData("first chat")]        // title
    [InlineData("shopdb")]            // database name
    public async Task Search_matches_title_database_and_message_text(string term)
    {
        await _store.SaveAsync(NewSession("First chat"));

        var summaries = await _store.ListAsync(term);

        Assert.Single(summaries);
    }

    [Fact]
    public async Task Search_escapes_like_wildcards()
    {
        var session = NewSession("100X coverage");
        await _store.SaveAsync(session);

        // "%" in the term must match literally, not as a wildcard.
        Assert.Empty(await _store.ListAsync("100% cov"));
        Assert.Single(await _store.ListAsync("100X cov"));
    }

    [Fact]
    public async Task Delete_removes_the_row()
    {
        var session = NewSession("Bye");
        await _store.SaveAsync(session);

        await _store.DeleteAsync(session.Id);

        Assert.Null(await _store.LoadAsync(session.Id));
        Assert.Empty(await _store.ListAsync());
    }

    [Fact]
    public async Task Load_rejects_non_guid_ids()
    {
        Assert.Null(await _store.LoadAsync("../../secrets"));
        Assert.Null(await _store.LoadAsync("not-a-guid"));
    }

    [Fact]
    public async Task Save_assigns_an_id_and_default_title()
    {
        var session = new ChatSession { Id = "bad-id", Title = "" };

        await _store.SaveAsync(session);

        Assert.Equal(32, session.Id.Length);
        Assert.Equal("New conversation", session.Title);
        Assert.NotNull(await _store.LoadAsync(session.Id));
    }

    [Fact]
    public async Task First_use_imports_legacy_json_files_into_backup_folder()
    {
        var legacyDir = Path.Combine(_dir, "legacy");
        Directory.CreateDirectory(legacyDir);
        var legacy = new FileChatSessionStore(legacyDir);
        var session = NewSession("From json files");
        await legacy.SaveAsync(session);

        var store = new SqliteChatSessionStore(_dbPath, legacyDir);
        var summaries = await store.ListAsync();

        var summary = Assert.Single(summaries);
        Assert.Equal("From json files", summary.Title);
        Assert.Equal(session.Id, summary.Id);
        var loaded = await store.LoadAsync(session.Id);
        Assert.NotNull(loaded);
        Assert.Equal("Show me the orders query", loaded!.Messages[0].Content);
        Assert.Empty(Directory.GetFiles(legacyDir, "*.json"));
        Assert.Single(Directory.GetFiles(legacyDir + ".imported", "*.json"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private static ChatSession NewSession(string title) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Title = title,
        DatabaseName = "ShopDb",
        Model = "test-model",
        Messages =
        [
            new ChatMessage { Role = "user", Content = "Show me the orders query" }
        ]
    };
}
