using System.Globalization;
using System.Text.Json;
using SQLAZOR.Models;

namespace SQLAZOR.Services;

/// <summary>
/// Reads chart blocks the assistant emits (```chart + JSON) and builds chart specs from real
/// query result rows. Parsing is tolerant: unknown keys are ignored, bad values become zero,
/// and anything unusable returns no chart instead of throwing.
/// </summary>
public static class ChatChartParser
{
    public const int MaxLabels = 40;
    public const int MaxSeries = 4;

    public static bool TryParse(string? json, out ChatChartSpec spec)
    {
        spec = null!;

        if (string.IsNullOrWhiteSpace(json))
            return false;

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return false;

            var root = document.RootElement;
            var title = GetString(root, "title") ?? GetString(root, "label") ?? string.Empty;
            var labels = ReadLabels(root);
            var series = ReadSeries(root, title)
                .Where(s => s.Values.Count > 0)
                .Take(MaxSeries)
                .ToList();

            if (series.Count == 0)
                return false;

            var pointCount = Math.Min(Math.Max(labels.Count, series.Max(s => s.Values.Count)), MaxLabels);
            if (pointCount == 0)
                return false;

            var normalizedSeries = series
                .Select((s, i) => new ChatChartSeries
                {
                    Name = Truncate(string.IsNullOrWhiteSpace(s.Name) ? $"Series {i + 1}" : s.Name, 60),
                    Values = Resize(s.Values, pointCount)
                })
                .ToList();

            var normalizedLabels = Enumerable.Range(0, pointCount)
                .Select(i => i < labels.Count && !string.IsNullOrWhiteSpace(labels[i])
                    ? Truncate(labels[i], 40)
                    : (i + 1).ToString(CultureInfo.InvariantCulture))
                .ToList();

            spec = new ChatChartSpec
            {
                Type = NormalizeType(GetString(root, "type")),
                Title = Truncate(title.Trim(), 120),
                Labels = normalizedLabels,
                Series = normalizedSeries
            };

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static bool CanChart(SqlQueryRunResult? result) => FromQueryResult(result) is not null;

    /// <summary>
    /// Builds a chart from a query result: first column is the label, up to four following
    /// columns whose cells parse as numbers become series. Returns null when it can't chart.
    /// </summary>
    public static ChatChartSpec? FromQueryResult(SqlQueryRunResult? result)
    {
        if (result is null || !result.Success || result.Columns.Count < 2 || result.Rows.Count == 0)
            return null;

        var rows = result.Rows.Take(MaxLabels).ToList();
        var series = new List<ChatChartSeries>();

        for (var column = 1; column < result.Columns.Count && series.Count < MaxSeries; column++)
        {
            var values = new List<double>(rows.Count);
            var numericCount = 0;

            foreach (var row in rows)
            {
                var raw = column < row.Count ? row[column] : null;
                if (TryNumber(raw, out var value))
                {
                    values.Add(value);
                    numericCount++;
                }
                else
                {
                    values.Add(0);
                }
            }

            if (numericCount == 0)
                continue;

            series.Add(new ChatChartSeries { Name = result.Columns[column], Values = values });
        }

        if (series.Count == 0)
            return null;

        var labels = rows
            .Select((row, i) =>
            {
                var label = row.Count > 0 ? row[0] : null;
                return string.IsNullOrWhiteSpace(label) ? (i + 1).ToString(CultureInfo.InvariantCulture) : label.Trim();
            })
            .Select(label => Truncate(label, 40))
            .ToList();

        return new ChatChartSpec
        {
            Type = "bar",
            Labels = labels,
            Series = series
        };
    }

    private static List<string> ReadLabels(JsonElement root)
    {
        foreach (var key in new[] { "labels", "categories", "x" })
        {
            if (!TryGetArray(root, key, out var array))
                continue;

            var labels = new List<string>();
            foreach (var item in array.EnumerateArray())
                labels.Add(LabelText(item, labels.Count));

            return labels;
        }

        return [];
    }

    private static List<ChatChartSeries> ReadSeries(JsonElement root, string title)
    {
        var result = new List<ChatChartSeries>();

        if (TryGetArray(root, "series", out var seriesArray))
        {
            foreach (var item in seriesArray.EnumerateArray())
            {
                if (result.Count >= MaxSeries)
                    break;

                var series = ReadSeriesItem(item, title, result.Count);
                if (series is not null)
                    result.Add(series);
            }

            return result;
        }

        if (TryGetArray(root, "datasets", out var datasetArray))
        {
            foreach (var item in datasetArray.EnumerateArray())
            {
                if (result.Count >= MaxSeries)
                    break;

                if (item.ValueKind != JsonValueKind.Object)
                    continue;

                var (values, numeric) = ReadValuesArray(item, "data");
                if (numeric == 0)
                    continue;

                result.Add(new ChatChartSeries
                {
                    Name = GetString(item, "label") ?? GetString(item, "name") ?? string.Empty,
                    Values = values
                });
            }

            return result;
        }

        foreach (var key in new[] { "values", "data" })
        {
            if (!TryGetArray(root, key, out var flatArray))
                continue;

            var (values, numeric) = ReadValues(flatArray);
            if (numeric > 0)
            {
                result.Add(new ChatChartSeries
                {
                    Name = string.IsNullOrWhiteSpace(title) ? "Value" : title,
                    Values = values
                });
            }

            break;
        }

        return result;
    }

    private static ChatChartSeries? ReadSeriesItem(JsonElement item, string title, int index)
    {
        if (item.ValueKind == JsonValueKind.Array)
        {
            var (flatValues, flatNumeric) = ReadValues(item);
            return flatNumeric == 0
                ? null
                : new ChatChartSeries { Name = $"Series {index + 1}", Values = flatValues };
        }

        if (item.ValueKind != JsonValueKind.Object)
            return null;

        var (values, numeric) = ReadValuesArray(item, "values");
        if (numeric == 0)
            (values, numeric) = ReadValuesArray(item, "data");
        if (numeric == 0)
            return null;

        var name = GetString(item, "name") ?? GetString(item, "label") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(title))
            name = title;

        return new ChatChartSeries { Name = name, Values = values };
    }

    private static (List<double> Values, int NumericCount) ReadValuesArray(JsonElement owner, string key) =>
        TryGetArray(owner, key, out var array) ? ReadValues(array) : ([], 0);

    private static (List<double> Values, int NumericCount) ReadValues(JsonElement array)
    {
        var values = new List<double>();
        var numeric = 0;

        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Number && item.TryGetDouble(out var number))
            {
                values.Add(number);
                numeric++;
                continue;
            }

            if (item.ValueKind == JsonValueKind.String && TryNumber(item.GetString(), out var parsed))
            {
                values.Add(parsed);
                numeric++;
                continue;
            }

            values.Add(0);
        }

        return (values, numeric);
    }

    private static string LabelText(JsonElement item, int index) => item.ValueKind switch
    {
        JsonValueKind.String => item.GetString() ?? (index + 1).ToString(CultureInfo.InvariantCulture),
        JsonValueKind.Number => item.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => (index + 1).ToString(CultureInfo.InvariantCulture)
    };

    private static string NormalizeType(string? type) => type?.Trim().ToLowerInvariant() switch
    {
        "line" or "area" => "line",
        "pie" or "doughnut" or "donut" => "pie",
        _ => "bar"
    };

    private static bool TryNumber(string? raw, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        // Tolerate thousands separators ("1,234") but not locale-specific decimal commas,
        // so a value means the same thing in every deployment.
        var cleaned = raw.Trim().Replace(",", string.Empty);
        return double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
               && double.IsFinite(value);
    }

    private static List<double> Resize(List<double> values, int length)
    {
        var result = new List<double>(length);
        for (var i = 0; i < length; i++)
            result.Add(i < values.Count ? values[i] : 0);
        return result;
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max].TrimEnd() + "…";

    private static bool TryGetArray(JsonElement owner, string key, out JsonElement array)
    {
        array = default;
        return owner.TryGetProperty(key, out array) && array.ValueKind == JsonValueKind.Array;
    }

    private static string? GetString(JsonElement owner, string key)
    {
        if (!owner.TryGetProperty(key, out var element))
            return null;

        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetRawText(),
            _ => null
        };
    }
}
