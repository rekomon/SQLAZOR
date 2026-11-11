namespace SQLAZOR.Models;

/// <summary>A saved AI-assistant conversation, persisted as one JSON file per session.</summary>
public sealed class ChatSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "New conversation";
    public string DatabaseKey { get; set; } = string.Empty;
    public string DatabaseName { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public bool Pinned { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
    public List<ChatMessage> Messages { get; set; } = [];
}

public sealed class ChatSessionSummary
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public string DatabaseKey { get; init; } = string.Empty;
    public string DatabaseName { get; init; } = string.Empty;
    public int MessageCount { get; init; }
    public bool Pinned { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}
