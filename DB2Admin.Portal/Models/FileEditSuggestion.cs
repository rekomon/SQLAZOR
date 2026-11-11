namespace SQLAZOR.Models;

/// <summary>A complete replacement file proposed by the model, plus a one-line explanation.</summary>
public sealed class FileEditSuggestion
{
    public required string Summary { get; init; }
    public required string Content { get; init; }
}
