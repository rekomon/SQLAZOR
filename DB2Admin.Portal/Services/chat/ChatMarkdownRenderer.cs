using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace SQLAZOR.Services;

/// <summary>
/// Minimal GitHub-flavoured Markdown renderer for assistant prose: paragraphs, headings, lists,
/// blockquotes, rules, tables with alignment, and inline bold / italic / strike / code / links.
/// Everything the model writes is HTML-encoded first, so raw HTML in a reply can never execute;
/// only http(s) and mailto links are emitted as anchors.
/// </summary>
public static class ChatMarkdownRenderer
{
    private const string TokenStart = "\u0000";
    private const string TokenEnd = "\u0000";

    private static readonly Regex TableDivider = new(
        @"^\s*\|?\s*:?-{2,}:?\s*(\|\s*:?-{2,}:?\s*)+\|?\s*$", RegexOptions.Compiled);

    private static readonly Regex HeadingPattern = new(@"^(#{1,6})\s+(.+?)\s*#*\s*$", RegexOptions.Compiled);
    private static readonly Regex RulePattern = new(@"^\s{0,3}([-*_])(\s*\1){2,}\s*$", RegexOptions.Compiled);
    private static readonly Regex BulletPattern = new(@"^\s{0,3}[-*+]\s+(.+)$", RegexOptions.Compiled);
    private static readonly Regex NumberedPattern = new(@"^\s{0,3}\d+[.)]\s+(.+)$", RegexOptions.Compiled);
    private static readonly Regex InlineCodePattern = new("`([^`\n]+)`", RegexOptions.Compiled);
    private static readonly Regex LinkPattern = new(
        @"\[([^\]\n]+)\]\(((?:https?://|mailto:)[^\s)]+)\)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex AutoLinkPattern = new(
        @"(?<![\w""'=/>])(https?://[^\s<>()\[\]""']+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex BoldStarPattern = new(@"\*\*(?!\s)(.+?)(?<!\s)\*\*", RegexOptions.Compiled);
    private static readonly Regex BoldUnderscorePattern = new(@"__(?!\s)(.+?)(?<!\s)__", RegexOptions.Compiled);
    private static readonly Regex ItalicStarPattern = new(@"(?<!\*)\*(?!\s)([^*\n]+?)(?<!\s)\*(?!\*)", RegexOptions.Compiled);
    private static readonly Regex ItalicUnderscorePattern = new(
        @"(?<![\w_])_(?!\s)([^_\n]+?)(?<!\s)_(?![\w_])", RegexOptions.Compiled);
    private static readonly Regex StrikePattern = new(@"~~(?!\s)(.+?)(?<!\s)~~", RegexOptions.Compiled);
    private static readonly Regex TokenPattern = new("\u0000(\\d+)\u0000", RegexOptions.Compiled);
    private static readonly Regex TableCellSplit = new(@"(?<!\\)\|", RegexOptions.Compiled);

    public static string ToHtml(string? markdown, bool rightToLeft = false)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return string.Empty;

        var lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var sb = new StringBuilder();
        var i = 0;

        while (i < lines.Length)
        {
            var line = lines[i];

            if (string.IsNullOrWhiteSpace(line))
            {
                i++;
                continue;
            }

            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                i = RenderFence(lines, i, sb);
                continue;
            }

            if (IsTableStart(lines, i))
            {
                i = RenderTable(lines, i, sb, rightToLeft);
                continue;
            }

            var heading = HeadingPattern.Match(line);
            if (heading.Success)
            {
                var level = heading.Groups[1].Length;
                sb.Append("<h").Append(level).Append('>')
                    .Append(Inline(heading.Groups[2].Value))
                    .Append("</h").Append(level).Append('>');
                i++;
                continue;
            }

            if (RulePattern.IsMatch(line))
            {
                sb.Append("<hr/>");
                i++;
                continue;
            }

            if (line.TrimStart().StartsWith('>'))
            {
                i = RenderQuote(lines, i, sb);
                continue;
            }

            if (BulletPattern.IsMatch(line) || NumberedPattern.IsMatch(line))
            {
                i = RenderList(lines, i, sb);
                continue;
            }

            // Paragraph: everything up to the next blank line or block-level construct.
            sb.Append("<p>");
            var first = true;
            while (i < lines.Length && !string.IsNullOrWhiteSpace(lines[i]) && !IsBlockStart(lines, i))
            {
                if (!first)
                    sb.Append("<br/>");
                sb.Append(Inline(lines[i]));
                first = false;
                i++;
            }
            sb.Append("</p>");

            // Defensive: a malformed stream must never spin on the same line forever.
            if (first)
                i++;
        }

        return sb.ToString();
    }

    private static bool IsBlockStart(string[] lines, int index) =>
        lines[index].TrimStart().StartsWith("```", StringComparison.Ordinal) ||
        HeadingPattern.IsMatch(lines[index]) ||
        RulePattern.IsMatch(lines[index]) ||
        lines[index].TrimStart().StartsWith('>') ||
        BulletPattern.IsMatch(lines[index]) ||
        NumberedPattern.IsMatch(lines[index]) ||
        IsTableStart(lines, index);

    private static bool IsTableStart(string[] lines, int index) =>
        index + 1 < lines.Length &&
        lines[index].Contains('|') &&
        TableDivider.IsMatch(lines[index + 1]);

    private static int RenderFence(string[] lines, int index, StringBuilder sb)
    {
        var fence = lines[index].TrimStart();
        var body = new StringBuilder();
        var i = index + 1;

        while (i < lines.Length && !lines[i].TrimStart().StartsWith("```", StringComparison.Ordinal))
        {
            body.AppendLine(lines[i]);
            i++;
        }

        if (i < lines.Length)
            i++;

        sb.Append("<pre class=\"md-pre\"><code>")
            .Append(WebUtility.HtmlEncode(body.ToString().TrimEnd('\r', '\n')))
            .Append("</code></pre>");

        return i;
    }

    private static int RenderQuote(string[] lines, int index, StringBuilder sb)
    {
        var quoted = new List<string>();

        while (index < lines.Length && lines[index].TrimStart().StartsWith('>'))
        {
            var text = lines[index].TrimStart()[1..];
            if (text.StartsWith(' '))
                text = text[1..];
            quoted.Add(Inline(text));
            index++;
        }

        sb.Append("<blockquote>").Append(string.Join("<br/>", quoted)).Append("</blockquote>");
        return index;
    }

    private static int RenderList(string[] lines, int index, StringBuilder sb)
    {
        var ordered = NumberedPattern.IsMatch(lines[index]);
        var pattern = ordered ? NumberedPattern : BulletPattern;

        sb.Append(ordered ? "<ol>" : "<ul>");

        while (index < lines.Length)
        {
            var match = pattern.Match(lines[index]);
            if (!match.Success)
                break;

            sb.Append("<li>").Append(Inline(match.Groups[1].Value)).Append("</li>");
            index++;
        }

        sb.Append(ordered ? "</ol>" : "</ul>");
        return index;
    }

    private static int RenderTable(string[] lines, int index, StringBuilder sb, bool rightToLeft)
    {
        var headers = SplitRow(lines[index]);
        var alignments = SplitRow(lines[index + 1]).Select(AlignmentOf).ToList();
        var columnCount = Math.Max(headers.Count, 1);

        sb.Append("<div class=\"md-table-wrap\"><table class=\"md-table\"><thead><tr>");
        for (var c = 0; c < columnCount; c++)
        {
            var text = c < headers.Count ? headers[c] : string.Empty;
            sb.Append("<th").Append(AlignmentStyle(c, alignments, rightToLeft)).Append('>')
                .Append(Inline(text))
                .Append("</th>");
        }
        sb.Append("</tr></thead><tbody>");

        var row = index + 2;
        while (row < lines.Length && !string.IsNullOrWhiteSpace(lines[row]) && lines[row].Contains('|'))
        {
            var cells = SplitRow(lines[row]);
            sb.Append("<tr>");
            for (var c = 0; c < columnCount; c++)
            {
                var text = c < cells.Count ? cells[c] : string.Empty;
                sb.Append("<td").Append(AlignmentStyle(c, alignments, rightToLeft)).Append('>')
                    .Append(Inline(text))
                    .Append("</td>");
            }
            sb.Append("</tr>");
            row++;
        }

        sb.Append("</tbody></table></div>");
        return row;
    }

    private static List<string> SplitRow(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.StartsWith('|'))
            trimmed = trimmed[1..];
        if (trimmed.EndsWith('|'))
            trimmed = trimmed[..^1];

        return TableCellSplit.Split(trimmed)
            .Select(cell => cell.Trim().Replace("\\|", "|"))
            .ToList();
    }

    private static string? AlignmentOf(string dividerCell)
    {
        var cell = dividerCell.Trim();
        var left = cell.StartsWith(':');
        var right = cell.EndsWith(':');

        return (left, right) switch
        {
            (true, true) => "center",
            (false, true) => "right",
            (true, false) => "left",
            _ => null
        };
    }

    private static string AlignmentStyle(int column, List<string?> alignments, bool rightToLeft)
    {
        if (column >= alignments.Count || alignments[column] is not { } align)
            return string.Empty;

        if (rightToLeft)
            align = align switch { "left" => "right", "right" => "left", _ => align };

        return $" style=\"text-align:{align}\"";
    }

    private static string Inline(string text)
    {
        var encoded = WebUtility.HtmlEncode(text.Replace("\0", string.Empty));
        var tokens = new List<string>();

        string Stash(string html)
        {
            tokens.Add(html);
            return $"{TokenStart}{tokens.Count - 1}{TokenEnd}";
        }

        encoded = InlineCodePattern.Replace(encoded, m => Stash($"<code>{m.Groups[1].Value}</code>"));

        encoded = LinkPattern.Replace(encoded, m => Stash(Anchor(m.Groups[2].Value, m.Groups[1].Value)));

        encoded = AutoLinkPattern.Replace(encoded, m =>
        {
            var url = m.Groups[1].Value;
            var trimmed = url.TrimEnd('.', ',', ';', ':', '!', '?');
            if (trimmed.Length == 0)
                return m.Value;

            var trailing = url[trimmed.Length..];
            return Stash(Anchor(trimmed, trimmed)) + trailing;
        });

        encoded = BoldStarPattern.Replace(encoded, "<strong>$1</strong>");
        encoded = BoldUnderscorePattern.Replace(encoded, "<strong>$1</strong>");
        encoded = StrikePattern.Replace(encoded, "<del>$1</del>");
        encoded = ItalicStarPattern.Replace(encoded, "<em>$1</em>");
        encoded = ItalicUnderscorePattern.Replace(encoded, "<em>$1</em>");

        return TokenPattern.Replace(encoded, m => tokens[int.Parse(m.Groups[1].Value)]);
    }

    private static string Anchor(string url, string label) =>
        $"<a href=\"{url}\" target=\"_blank\" rel=\"noopener noreferrer\">{label}</a>";
}
