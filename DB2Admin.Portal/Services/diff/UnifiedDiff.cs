using System.Text;

namespace SQLAZOR.Services;

public enum DiffKind
{
    Unchanged,
    Added,
    Removed
}

public sealed record DiffLine(DiffKind Kind, string Text, int? OldLineNumber, int? NewLineNumber);

/// <summary>
/// Line diff via LCS, used to preview what a regeneration or an AI edit would change.
/// Very large inputs fall back to "all old lines removed, all new lines added" so a huge
/// generated file cannot blow up the render tree with a quadratic table.
/// </summary>
public static class UnifiedDiff
{
    private const long MaxMatrixCells = 4_000_000;

    public static List<DiffLine> Build(string? oldText, string? newText)
    {
        var oldLines = SplitLines(oldText);
        var newLines = SplitLines(newText);

        if ((long)oldLines.Length * newLines.Length > MaxMatrixCells)
        {
            var coarse = new List<DiffLine>(oldLines.Length + newLines.Length);
            for (var i = 0; i < oldLines.Length; i++)
                coarse.Add(new DiffLine(DiffKind.Removed, oldLines[i], i + 1, null));
            for (var i = 0; i < newLines.Length; i++)
                coarse.Add(new DiffLine(DiffKind.Added, newLines[i], null, i + 1));
            return coarse;
        }

        var lcs = BuildLcsTable(oldLines, newLines);
        var result = new List<DiffLine>(Math.Max(oldLines.Length, newLines.Length));

        var oldIndex = 0;
        var newIndex = 0;

        while (oldIndex < oldLines.Length && newIndex < newLines.Length)
        {
            if (string.Equals(oldLines[oldIndex], newLines[newIndex], StringComparison.Ordinal))
            {
                result.Add(new DiffLine(DiffKind.Unchanged, oldLines[oldIndex], oldIndex + 1, newIndex + 1));
                oldIndex++;
                newIndex++;
            }
            else if (lcs[oldIndex + 1, newIndex] >= lcs[oldIndex, newIndex + 1])
            {
                result.Add(new DiffLine(DiffKind.Removed, oldLines[oldIndex], oldIndex + 1, null));
                oldIndex++;
            }
            else
            {
                result.Add(new DiffLine(DiffKind.Added, newLines[newIndex], null, newIndex + 1));
                newIndex++;
            }
        }

        while (oldIndex < oldLines.Length)
        {
            result.Add(new DiffLine(DiffKind.Removed, oldLines[oldIndex], oldIndex + 1, null));
            oldIndex++;
        }

        while (newIndex < newLines.Length)
        {
            result.Add(new DiffLine(DiffKind.Added, newLines[newIndex], null, newIndex + 1));
            newIndex++;
        }

        return result;
    }

    public static string ToText(string? oldText, string? newText, string fileName = "")
    {
        var sb = new StringBuilder();
        sb.AppendLine($"--- {(fileName.Length > 0 ? fileName : "current")}");
        sb.AppendLine($"+++ {(fileName.Length > 0 ? fileName : "proposed")}");

        foreach (var line in Build(oldText, newText))
        {
            var prefix = line.Kind switch
            {
                DiffKind.Added => "+",
                DiffKind.Removed => "-",
                _ => " "
            };
            sb.AppendLine(prefix + line.Text);
        }

        return sb.ToString();
    }

    private static int[,] BuildLcsTable(string[] oldLines, string[] newLines)
    {
        var table = new int[oldLines.Length + 1, newLines.Length + 1];

        for (var i = oldLines.Length - 1; i >= 0; i--)
        {
            for (var j = newLines.Length - 1; j >= 0; j--)
            {
                table[i, j] = string.Equals(oldLines[i], newLines[j], StringComparison.Ordinal)
                    ? table[i + 1, j + 1] + 1
                    : Math.Max(table[i + 1, j], table[i, j + 1]);
            }
        }

        return table;
    }

    private static string[] SplitLines(string? text) =>
        string.IsNullOrEmpty(text)
            ? []
            : text.Replace("\r\n", "\n").Split('\n');
}
