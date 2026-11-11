namespace SQLAZOR.Models;

/// <summary>
/// A file the user attached to one chat turn. Text files are inlined into that turn's prompt;
/// images keep base64 bytes in <see cref="Content"/> and are sent to vision-capable providers
/// natively. <see cref="MimeType"/> is null for text attachments (and for conversations saved
/// before image support existed).
/// </summary>
public sealed class ChatAttachment
{
    public required string FileName { get; set; }
    public required string Content { get; set; }
    public long SizeBytes { get; set; }
    public string? MimeType { get; set; }

    public bool IsImage =>
        MimeType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true;
}
