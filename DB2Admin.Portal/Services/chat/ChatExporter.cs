using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SQLAZOR.Models;

namespace SQLAZOR.Services;

/// <summary>
/// Renders a saved conversation as Markdown / standalone HTML / JSON, or as a zip of every code
/// block and attachment. File names are inferred from the snippet (class name, CREATE statement,
/// @page route, HTML title) and always sanitized before use in a download or path.
/// </summary>
public static class ChatExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private static readonly HashSet<string> ReservedFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public static string ToJson(ChatSession session) => JsonSerializer.Serialize(session, JsonOptions);

    public static ChatSession? FromJson(string json)
    {
        try
        {
            var session = JsonSerializer.Deserialize<ChatSession>(json, JsonOptions);
            if (session is null || session.Messages is null)
                return null;

            if (string.IsNullOrWhiteSpace(session.Title))
                session.Title = "Imported conversation";

            foreach (var message in session.Messages)
            {
                if (string.IsNullOrWhiteSpace(message.Id))
                    message.Id = Guid.NewGuid().ToString("N");
                message.Attachments ??= [];
            }

            return session;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string ToMarkdown(ChatSession session)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {session.Title}");
        sb.AppendLine();
        sb.AppendLine($"- Database: {session.DatabaseName}");
        if (!string.IsNullOrWhiteSpace(session.Provider))
            sb.AppendLine($"- Provider: {session.Provider}");
        sb.AppendLine($"- Model: {session.Model}");
        sb.AppendLine($"- Exported: {DateTimeOffset.Now:yyyy-MM-dd HH:mm}");
        sb.AppendLine();

        foreach (var message in session.Messages)
        {
            var who = message.Role == "user" ? "You" : "Assistant";
            sb.AppendLine($"## {who} — {message.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm}");
            sb.AppendLine();

            if (message.IsError)
                sb.AppendLine("> Error response");

            foreach (var segment in ChatContentParser.Extract(message.Content))
            {
                if (!segment.IsCode)
                {
                    sb.AppendLine(segment.Text.Trim());
                    sb.AppendLine();
                    continue;
                }

                if (segment.Language.Equals("chart", StringComparison.OrdinalIgnoreCase) &&
                    ChatChartParser.TryParse(segment.Text, out var chart))
                {
                    sb.AppendLine(ChartToMarkdown(chart));
                    sb.AppendLine();
                    continue;
                }

                sb.AppendLine("```" + segment.Language);
                sb.AppendLine(segment.Text.TrimEnd());
                sb.AppendLine("```");
                sb.AppendLine();
            }

            foreach (var attachment in message.Attachments)
            {
                if (attachment.IsImage)
                {
                    sb.AppendLine($"*[image attachment: {attachment.FileName}, {FormatBytes(attachment.SizeBytes)} — use the .zip export to get the file]*");
                    sb.AppendLine();
                    continue;
                }

                sb.AppendLine($"<details><summary>Attachment: {attachment.FileName}</summary>");
                sb.AppendLine();
                sb.AppendLine("```" + ChatContentParser.LanguageTagForFileName(attachment.FileName));
                sb.AppendLine(attachment.Content.TrimEnd());
                sb.AppendLine("```");
                sb.AppendLine("</details>");
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    public static string ToHtml(ChatSession session)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\"/>");
        sb.AppendLine($"<title>{Enc(session.Title)}</title>");
        sb.AppendLine("<style>body{font-family:system-ui,'Segoe UI',sans-serif;max-width:900px;margin:2rem auto;padding:0 1rem;color:#1f2430;background:#f6f7fb}.msg{border:1px solid #dde1ec;border-radius:10px;padding:1rem;margin:1rem 0;background:#fff}.who{font-weight:700;font-size:.8rem;text-transform:uppercase;letter-spacing:.06em;color:#5b6478}.user{border-left:4px solid #4f7cff}.assistant{border-left:4px solid #17b890}pre{background:#0f172a;color:#e6edf3;padding:1rem;border-radius:8px;overflow:auto}code{font-family:ui-monospace,Consolas,monospace;font-size:.85rem}details{margin-top:.5rem;color:#5b6478}.meta{font-size:.8rem;color:#5b6478}table{border-collapse:collapse;width:100%;margin:.6rem 0}th,td{border:1px solid #dde1ec;padding:.4rem .6rem;text-align:left;vertical-align:top}th{background:#f0f3fa}.md h1,.md h2,.md h3,.md h4{margin:.9rem 0 .4rem}.md ul,.md ol{margin:.4rem 0 .6rem 1.2rem}.md blockquote{border-left:3px solid #c8d0e2;margin:.6rem 0;padding:.2rem .9rem;color:#5b6478}.md code{background:#eef1f8;padding:0 .25rem;border-radius:4px}[dir=\"rtl\"] blockquote{border-left:none;border-right:3px solid #c8d0e2}[dir=\"rtl\"] ul,[dir=\"rtl\"] ol{margin:.4rem 1.2rem .6rem 0}[dir=\"rtl\"] pre,[dir=\"rtl\"] pre code{direction:ltr;text-align:left}</style>");
        sb.AppendLine("</head><body>");
        sb.AppendLine($"<h1>{Enc(session.Title)}</h1>");
        var provider = string.IsNullOrWhiteSpace(session.Provider) ? "" : $" | Provider: {Enc(session.Provider)}";
        sb.AppendLine($"<p class=\"meta\">Database: {Enc(session.DatabaseName)}{provider} | Model: {Enc(session.Model)} | Exported: {DateTimeOffset.Now:yyyy-MM-dd HH:mm}</p>");

        foreach (var message in session.Messages)
        {
            var isUser = message.Role == "user";
            var isRtl = ChatTextDirection.ForMessage(message.Content);
            sb.AppendLine($"<div class=\"msg {(isUser ? "user" : "assistant")}\"{(isRtl ? " dir=\"rtl\"" : "")}>");
            sb.AppendLine($"<div class=\"who\">{(isUser ? "You" : "Assistant")} | {message.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm}</div>");

            foreach (var segment in ChatContentParser.Extract(message.Content))
            {
                if (!segment.IsCode)
                {
                    sb.AppendLine($"<div class=\"md\">{ChatMarkdownRenderer.ToHtml(segment.Text, isRtl)}</div>");
                }
                else if (segment.Language.Equals("chart", StringComparison.OrdinalIgnoreCase) &&
                         ChatChartParser.TryParse(segment.Text, out var chart))
                {
                    sb.AppendLine(ChartToHtml(chart));
                }
                else
                {
                    sb.AppendLine($"<pre><code>{Enc(segment.Text)}</code></pre>");
                }
            }

            foreach (var attachment in message.Attachments)
            {
                if (attachment.IsImage)
                {
                    sb.AppendLine($"<figure><img src=\"data:{Enc(attachment.MimeType ?? "image/png")};base64,{attachment.Content}\" alt=\"{Enc(attachment.FileName)}\" style=\"max-width:100%;border-radius:8px\"/><figcaption class=\"meta\">{Enc(attachment.FileName)}</figcaption></figure>");
                    continue;
                }

                sb.AppendLine($"<details><summary>Attachment: {Enc(attachment.FileName)}</summary><pre><code>{Enc(attachment.Content)}</code></pre></details>");
            }

            sb.AppendLine("</div>");
        }

        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    /// <summary>Zip containing the Markdown transcript, every code block, and every attachment.</summary>
    public static byte[] BuildZip(ChatSession session)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddEntry(archive, SanitizeFileName(session.Title, "conversation") + ".md", ToMarkdown(session));

            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var ordinal = 0;

            foreach (var message in session.Messages)
            {
                foreach (var segment in ChatContentParser.Extract(message.Content).Where(s => s.IsCode))
                {
                    ordinal++;
                    var name = UniqueName("code/" + SuggestedFileName(segment, ordinal), used);
                    AddEntry(archive, name, segment.Text);
                }

                foreach (var attachment in message.Attachments)
                {
                    var name = UniqueName("attachments/" + SanitizeFileName(attachment.FileName, $"attachment-{ordinal}"), used);
                    if (attachment.IsImage && TryDecodeBase64(attachment.Content, out var imageBytes))
                    {
                        AddEntry(archive, name, imageBytes);
                        continue;
                    }

                    AddEntry(archive, name, attachment.Content);
                }
            }
        }

        return buffer.ToArray();
    }

    public static string SuggestedGeneratedPath(ChatContentSegment segment, int ordinal)
    {
        var fileName = SuggestedFileName(segment, ordinal);
        return Path.GetExtension(fileName).ToLowerInvariant() == ".razor"
            ? $"Components/Pages/AiGenerated/{fileName}"
            : $"AiGenerated/{fileName}";
    }

    public static string SuggestedFileName(ChatContentSegment segment, int ordinal)
    {
        var extension = ExtensionFor(segment.Language, segment.Text);
        return BaseNameFor(extension, segment.Text, ordinal) + extension;
    }

    public static string ExtensionFor(string language, string code = "")
    {
        var normalized = language.Trim().TrimStart('.').ToLowerInvariant();
        return normalized switch
        {
            "csharp" or "cs" or "c#" => ".cs",
            "razor" or "razorcomponent" => ".razor",
            "sql" or "tsql" or "mssql" => ".sql",
            "json" => ".json",
            "html" or "htm" => ".html",
            "css" => ".css",
            "javascript" or "js" => ".js",
            "typescript" or "ts" => ".ts",
            "xml" or "xaml" => ".xml",
            "yaml" or "yml" => ".yml",
            "bash" or "sh" or "shell" => ".sh",
            "powershell" or "ps1" => ".ps1",
            "markdown" or "md" => ".md",
            "http" => ".http",
            _ => SniffExtension(code)
        };
    }

    public static string MimeFor(string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".html" => "text/html;charset=utf-8",
            ".json" => "application/json;charset=utf-8",
            ".md" => "text/markdown;charset=utf-8",
            ".zip" => "application/zip",
            ".sql" => "application/sql;charset=utf-8",
            _ => "text/plain;charset=utf-8"
        };

    public static string SanitizeFileName(string? value, string fallback = "file")
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        var chars = value.Trim()
            .Select(c => char.IsLetterOrDigit(c) || c is '_' or '-' or '.' ? c : '-')
            .ToArray();
        var cleaned = CollapseDashes(new string(chars)).Trim(' ', '-', '.');

        if (cleaned.Length > 80)
            cleaned = cleaned[..80].TrimEnd(' ', '-', '.');
        if (string.IsNullOrWhiteSpace(cleaned) || cleaned is "." or "..")
            return fallback;

        return ReservedFileNames.Contains(Path.GetFileNameWithoutExtension(cleaned))
            ? "_" + cleaned
            : cleaned;
    }

    private static string BaseNameFor(string extension, string code, int ordinal) =>
        extension switch
        {
            ".cs" => Sanitize(
                Regex.Match(code, @"\b(?:class|record|struct|interface|enum)\s+([A-Za-z_]\w*)").Groups[1].Value,
                $"Snippet{ordinal}"),
            ".razor" => Sanitize(RouteName(code), $"Page{ordinal}"),
            ".sql" => Sanitize(SqlObjectName(code), $"Query{ordinal}"),
            ".html" => Sanitize(HtmlTitle(code), $"Page{ordinal}"),
            _ => $"Snippet{ordinal}"
        };

    private static string RouteName(string code) =>
        Regex.Match(code, "@page\\s+\"([^\"]+)\"").Groups[1].Value;

    private static string SqlObjectName(string code)
    {
        var match = Regex.Match(
            code,
            @"CREATE\s+(?:OR\s+ALTER\s+)?(?:PROCEDURE|PROC|TABLE|VIEW|FUNCTION|TRIGGER)\s+([\[\]\w.]+)",
            RegexOptions.IgnoreCase);
        var name = match.Groups[1].Value.Replace("[", "").Replace("]", "");
        var dot = name.LastIndexOf('.');
        return dot >= 0 ? name[(dot + 1)..] : name;
    }

    private static string HtmlTitle(string code) =>
        Regex.Match(code, "<title>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline).Groups[1].Value;

    private static string SniffExtension(string code)
    {
        var trimmed = code.TrimStart();
        if (trimmed.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("WITH", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("CREATE", StringComparison.OrdinalIgnoreCase))
        {
            return ".sql";
        }

        if (trimmed.StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("<html", StringComparison.OrdinalIgnoreCase))
        {
            return ".html";
        }

        if (trimmed.Contains("class ", StringComparison.Ordinal) && trimmed.Contains('{'))
            return ".cs";

        if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
            return ".json";

        return ".txt";
    }

    private static string Sanitize(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        var chars = value.Trim()
            .Select(c => char.IsLetterOrDigit(c) || c is '_' or '-' ? c : '-')
            .ToArray();
        var cleaned = CollapseDashes(new string(chars)).Trim('-');

        if (cleaned.Length > 48)
            cleaned = cleaned[..48].TrimEnd('-');

        return string.IsNullOrWhiteSpace(cleaned) ? fallback : cleaned;
    }

    private static string CollapseDashes(string value) => Regex.Replace(value, "-{2,}", "-");

    private static string UniqueName(string fileName, HashSet<string> used)
    {
        var directory = Path.GetDirectoryName(fileName)?.Replace('\\', '/');
        var name = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);

        var candidate = fileName;
        var counter = 2;
        while (!used.Add(candidate))
        {
            var suffixed = $"{name}-{counter}{extension}";
            candidate = string.IsNullOrEmpty(directory) ? suffixed : $"{directory}/{suffixed}";
            counter++;
        }

        return candidate;
    }

    private static void AddEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream);
        writer.Write(content);
    }

    private static void AddEntry(ZipArchive archive, string name, byte[] content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        stream.Write(content);
    }

    private static bool TryDecodeBase64(string value, out byte[] bytes)
    {
        try
        {
            bytes = Convert.FromBase64String(value);
            return true;
        }
        catch (FormatException)
        {
            bytes = [];
            return false;
        }
    }

    private static string FormatBytes(long bytes) =>
        bytes < 1024 ? $"{bytes} B" : $"{bytes / 1024.0:0.#} KB";

    private static string Enc(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    private static string ChartToMarkdown(ChatChartSpec chart)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(chart.Title))
            sb.AppendLine($"**{chart.Title}**").AppendLine();

        sb.Append('|').Append(" Label |");
        foreach (var series in chart.Series)
            sb.Append(' ').Append(series.Name).Append(" |");
        sb.AppendLine();

        sb.Append("| --- |");
        foreach (var _ in chart.Series)
            sb.Append(" ---: |");
        sb.AppendLine();

        for (var row = 0; row < chart.Labels.Count; row++)
        {
            sb.Append("| ").Append(chart.Labels[row]).Append(" |");
            foreach (var series in chart.Series)
            {
                sb.Append(' ')
                    .Append(row < series.Values.Count
                        ? series.Values[row].ToString("0.###", CultureInfo.InvariantCulture)
                        : string.Empty)
                    .Append(" |");
            }
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    private static string ChartToHtml(ChatChartSpec chart)
    {
        var sb = new StringBuilder("<div class=\"chart\">");
        if (!string.IsNullOrWhiteSpace(chart.Title))
            sb.Append("<h3>").Append(Enc(chart.Title)).Append("</h3>");

        sb.Append("<table><thead><tr><th></th>");
        foreach (var series in chart.Series)
            sb.Append("<th>").Append(Enc(series.Name)).Append("</th>");
        sb.Append("</tr></thead><tbody>");

        for (var row = 0; row < chart.Labels.Count; row++)
        {
            sb.Append("<tr><td>").Append(Enc(chart.Labels[row])).Append("</td>");
            foreach (var series in chart.Series)
            {
                sb.Append("<td>")
                    .Append(row < series.Values.Count
                        ? series.Values[row].ToString("0.###", CultureInfo.InvariantCulture)
                        : string.Empty)
                    .Append("</td>");
            }
            sb.Append("</tr>");
        }

        sb.Append("</tbody></table></div>");
        return sb.ToString();
    }
}
