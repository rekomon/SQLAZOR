namespace SQLAZOR.Models;

public sealed class ChatMessage
{
    /// <summary>Stable identity so saved conversations keep their per-block query results and exports line up.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    // Make properties mutable so callers can append streaming text and set error flags
    public string Role { get; set; } = string.Empty;   // "user" or "assistant"
    public string Content { get; set; } = string.Empty;
    public bool IsError { get; set; } = false;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public List<ChatAttachment> Attachments { get; set; } = [];
}
