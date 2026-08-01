namespace SQLAZOR.Models;

public sealed class ChatMessage
{
    // Make properties mutable so callers can append streaming text and set error flags
    public string Role { get; set; } = string.Empty;   // "user" or "assistant"
    public string Content { get; set; } = string.Empty;
    public bool IsError { get; set; } = false;
}
