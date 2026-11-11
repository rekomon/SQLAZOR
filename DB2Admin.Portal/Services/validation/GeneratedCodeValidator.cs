using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SQLAZOR.Models;

namespace SQLAZOR.Services;

/// <summary>
/// Offline syntax check for generated output. C# is parsed with Roslyn; Razor has no public
/// runtime parser, so its <c>@code</c>/<c>@functions</c> blocks are extracted and parsed as a
/// class body — markup itself and component semantics are out of scope.
/// </summary>
public sealed class GeneratedCodeValidator : IGeneratedCodeValidator
{
    private static readonly CSharpParseOptions ParseOptions =
        new(LanguageVersion.Latest, DocumentationMode.None, SourceCodeKind.Regular);

    private static readonly Regex RazorCodeBlockDirective =
        new(@"(?m)^[ \t]*@(?:code|functions)\b", RegexOptions.Compiled);

    private const string RazorWrapperPrefix = "internal partial class __SQLAZOR_RazorValidation\n{\n";

    public GeneratedCodeValidationReport Validate(IEnumerable<GeneratedFile> files)
    {
        var diagnostics = new List<ValidationDiagnostic>();
        var checkedFiles = 0;
        var skippedFiles = 0;

        foreach (var file in files)
        {
            switch (Path.GetExtension(file.RelativePath).ToLowerInvariant())
            {
                case ".cs":
                    checkedFiles++;
                    ValidateCSharp(file, diagnostics);
                    break;
                case ".razor":
                    checkedFiles++;
                    ValidateRazor(file, diagnostics);
                    break;
                case ".json":
                    checkedFiles++;
                    ValidateJson(file, diagnostics);
                    break;
                case ".csproj":
                case ".props":
                case ".targets":
                case ".xml":
                case ".slnx":
                case ".svg":
                case ".resx":
                    checkedFiles++;
                    ValidateXml(file, diagnostics);
                    break;
                default:
                    skippedFiles++;
                    break;
            }
        }

        return new GeneratedCodeValidationReport
        {
            FilesChecked = checkedFiles,
            FilesSkipped = skippedFiles,
            Diagnostics = diagnostics
                .OrderBy(d => d.FilePath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(d => d.Line)
                .ThenBy(d => d.Column)
                .ToList()
        };
    }

    private static void ValidateCSharp(GeneratedFile file, List<ValidationDiagnostic> diagnostics)
    {
        var tree = CSharpSyntaxTree.ParseText(file.Content, ParseOptions, file.RelativePath);
        foreach (var diagnostic in tree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error))
            diagnostics.Add(ToDiagnostic(file.RelativePath, diagnostic));
    }

    private static void ValidateRazor(GeneratedFile file, List<ValidationDiagnostic> diagnostics)
    {
        var text = file.Content;

        foreach (Match match in RazorCodeBlockDirective.Matches(text))
        {
            var directiveLine = LineAt(text, match.Index);
            var braceIndex = FindNextNonWhitespace(text, match.Index + match.Length);

            if (braceIndex < 0 || text[braceIndex] != '{')
            {
                diagnostics.Add(new ValidationDiagnostic
                {
                    FilePath = file.RelativePath,
                    Line = directiveLine,
                    Column = 1,
                    Message = "Razor @code/@functions directive is not followed by an opening brace."
                });
                continue;
            }

            if (!TryFindMatchingBrace(text, braceIndex, out var endIndex))
            {
                diagnostics.Add(new ValidationDiagnostic
                {
                    FilePath = file.RelativePath,
                    Line = directiveLine,
                    Column = 1,
                    Message = "Unbalanced braces: this @code/@functions block is never closed."
                });
                continue;
            }

            var block = text[(braceIndex + 1)..endIndex];
            var wrapped = RazorWrapperPrefix + block + "\n}";
            var tree = CSharpSyntaxTree.ParseText(wrapped, ParseOptions, file.RelativePath);

            // First content line after '{'; the wrapper adds two lines before it.
            var contentFirstLine = LineAt(text, braceIndex + 1);

            foreach (var diagnostic in tree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error))
            {
                var position = diagnostic.Location.GetLineSpan().StartLinePosition;
                var mappedLine = position.Line < 2
                    ? directiveLine
                    : contentFirstLine + (position.Line - 2);

                diagnostics.Add(new ValidationDiagnostic
                {
                    FilePath = file.RelativePath,
                    Line = mappedLine,
                    Column = position.Character + 1,
                    Id = diagnostic.Id,
                    Message = diagnostic.GetMessage(CultureInfo.InvariantCulture)
                });
            }
        }
    }

    private static void ValidateJson(GeneratedFile file, List<ValidationDiagnostic> diagnostics)
    {
        try
        {
            using var document = JsonDocument.Parse(file.Content, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });
        }
        catch (JsonException ex)
        {
            diagnostics.Add(new ValidationDiagnostic
            {
                FilePath = file.RelativePath,
                Line = (int)(ex.LineNumber ?? 0) + 1,
                Column = (int)(ex.BytePositionInLine ?? 0) + 1,
                Message = ex.Message
            });
        }
    }

    private static void ValidateXml(GeneratedFile file, List<ValidationDiagnostic> diagnostics)
    {
        try
        {
            XDocument.Parse(file.Content.TrimStart('\uFEFF'));
        }
        catch (XmlException ex)
        {
            diagnostics.Add(new ValidationDiagnostic
            {
                FilePath = file.RelativePath,
                Line = ex.LineNumber,
                Column = ex.LinePosition,
                Message = ex.Message
            });
        }
    }

    private static ValidationDiagnostic ToDiagnostic(string filePath, Diagnostic diagnostic)
    {
        var position = diagnostic.Location.GetLineSpan().StartLinePosition;
        return new ValidationDiagnostic
        {
            FilePath = filePath,
            Line = position.Line + 1,
            Column = position.Character + 1,
            Id = diagnostic.Id,
            Message = diagnostic.GetMessage(CultureInfo.InvariantCulture)
        };
    }

    private static int LineAt(string text, int index)
    {
        var line = 1;
        for (var i = 0; i < index && i < text.Length; i++)
        {
            if (text[i] == '\n') line++;
        }

        return line;
    }

    private static int FindNextNonWhitespace(string text, int index)
    {
        for (var i = index; i < text.Length; i++)
        {
            if (!char.IsWhiteSpace(text[i])) return i;
        }

        return -1;
    }

    private static bool TryFindMatchingBrace(string text, int openIndex, out int closeIndex)
    {
        var depth = 0;

        for (var i = openIndex; i < text.Length; i++)
        {
            var c = text[i];

            if (c == '/' && i + 1 < text.Length)
            {
                if (text[i + 1] == '/')
                {
                    var newline = text.IndexOf('\n', i + 2);
                    i = newline < 0 ? text.Length : newline;
                    continue;
                }

                if (text[i + 1] == '*')
                {
                    var end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    if (end < 0) break;
                    i = end + 1;
                    continue;
                }
            }

            if (c == '\'')
            {
                i = SkipQuoted(text, i, '\'', escapeAware: true);
                continue;
            }

            if (c == '"')
            {
                if (i + 2 < text.Length && text[i + 1] == '"' && text[i + 2] == '"')
                {
                    i = SkipRawString(text, i);
                    continue;
                }

                var verbatim = (i > 0 && text[i - 1] == '@') || (i > 1 && text[i - 2] == '@');
                i = SkipQuoted(text, i, '"', escapeAware: !verbatim, doubledEscape: verbatim);
                continue;
            }

            if (c == '{')
            {
                depth++;
            }
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                {
                    closeIndex = i;
                    return true;
                }
            }
        }

        closeIndex = -1;
        return false;
    }

    private static int SkipQuoted(string text, int quoteIndex, char quote, bool escapeAware, bool doubledEscape = false)
    {
        for (var i = quoteIndex + 1; i < text.Length; i++)
        {
            if (escapeAware && text[i] == '\\')
            {
                i++;
                continue;
            }

            if (text[i] != quote) continue;

            if (doubledEscape && i + 1 < text.Length && text[i + 1] == quote)
            {
                i++;
                continue;
            }

            return i;
        }

        return text.Length - 1;
    }

    private static int SkipRawString(string text, int quoteIndex)
    {
        var quotes = 0;
        while (quoteIndex + quotes < text.Length && text[quoteIndex + quotes] == '"') quotes++;

        var closing = new string('"', quotes);
        var end = text.IndexOf(closing, quoteIndex + quotes, StringComparison.Ordinal);
        return end < 0 ? text.Length - 1 : end + quotes - 1;
    }
}
