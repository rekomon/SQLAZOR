using SQLAZOR.Models;
using SQLAZOR.Services;
using Xunit;

namespace SQLAZOR.Tests;

public sealed class AppDataPathTests
{
    [Fact]
    public void BaseDirectory_is_writable_and_never_the_system_profile()
    {
        var dir = AppDataPath.BaseDirectory();

        Assert.False(
            dir.Contains("systemprofile", StringComparison.OrdinalIgnoreCase),
            $"BaseDirectory resolved to the system profile: {dir}");

        Directory.CreateDirectory(dir);
        var probe = Path.Combine(dir, ".write-test");
        File.WriteAllText(probe, "ok");
        Assert.True(File.Exists(probe));
        File.Delete(probe);
    }

    [Fact]
    public async Task ChatSessionStore_saves_and_loads_in_a_missing_directory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sqlazor-tests", Guid.NewGuid().ToString("N"));
        var store = new FileChatSessionStore(dir);

        var session = new ChatSession
        {
            Title = "Persistence roundtrip",
            DatabaseName = "ShopDb",
            Messages =
            [
                new ChatMessage { Role = "user", Content = "hello" },
                new ChatMessage { Role = "assistant", Content = "hi" }
            ]
        };

        await store.SaveAsync(session);
        var loaded = await store.LoadAsync(session.Id);

        Assert.NotNull(loaded);
        Assert.Equal("Persistence roundtrip", loaded.Title);
        Assert.Equal(2, loaded.Messages.Count);
        Assert.Equal("hello", loaded.Messages[0].Content);

        var listed = await store.ListAsync();
        Assert.Contains(listed, s => s.Id == session.Id);

        await store.DeleteAsync(session.Id);
        Assert.Null(await store.LoadAsync(session.Id));
        Directory.Delete(dir, recursive: true);
    }
}
