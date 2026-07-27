namespace SQLAZOR.Models;

public sealed class ChatMessage
{
    public required string Role { get; init; }   // "user" or "assistant"
    public required string Content { get; init; }
    public bool IsError { get; init; }
}
