using System.Text.Json;
using SQLAZOR.Models;

namespace SQLAZOR.Services;

/// <summary>
/// Prompt/parse pair for "edit this generated file" requests. The model must return the whole
/// file rather than a patch, so the caller can show a normal two-way diff and apply verbatim.
/// </summary>
public static class FileEditAssistant
{
    private const string PromptTemplate = @"You are editing a single file in a generated ASP.NET Core / Blazor project.
Apply the requested change to the file below and return the COMPLETE updated file.

Rules:
- Return the whole file, not a diff or a fragment.
- Preserve the existing style, namespaces, and public API unless the instruction explicitly changes them.
- Do not invent database columns/properties that are not in the schema context.
- Do not add commentary outside the JSON.

File path: {0}

Instruction:
{1}

Schema context:
{2}

Current file content:
--- BEGIN FILE ---
{3}
--- END FILE ---

Respond with ONLY valid JSON matching exactly this shape:
{{""summary"": ""one line describing the change"", ""content"": ""complete updated file text""}}";

    public static string BuildPrompt(string filePath, string currentContent, string instruction, string schemaContext) =>
        string.Format(PromptTemplate, filePath, instruction, schemaContext, currentContent);

    public static FileEditSuggestion? TryParse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(StripFences(json));
            if (!doc.RootElement.TryGetProperty("content", out var contentElement) ||
                contentElement.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var content = contentElement.GetString();
            if (string.IsNullOrWhiteSpace(content))
                return null;

            var summary = doc.RootElement.TryGetProperty("summary", out var summaryElement) &&
                          summaryElement.ValueKind == JsonValueKind.String
                ? summaryElement.GetString()
                : null;

            return new FileEditSuggestion
            {
                Summary = string.IsNullOrWhiteSpace(summary) ? "Proposed update" : summary.Trim(),
                Content = content
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string StripFences(string json)
    {
        var trimmed = (json ?? string.Empty).Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
            return trimmed;

        var firstNewline = trimmed.IndexOf('\n');
        if (firstNewline < 0)
            return trimmed;

        var text = trimmed[(firstNewline + 1)..];
        var closing = text.LastIndexOf("```", StringComparison.Ordinal);
        return closing >= 0 ? text[..closing] : text;
    }
}
