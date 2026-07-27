namespace SQLAZOR.Models;

public enum InsightChartType { Bar, Pie, Line }

/// <summary>
/// One AI-suggested dashboard chart: a title, a chart type, and the SQL to compute it. Must
/// resolve to exactly two columns (label, value) once validated. Carries its own validation/
/// preview state so the review UI can show why a candidate was rejected without a second round trip.
/// </summary>
public sealed class DashboardInsightCandidate
{
    public required string Title { get; init; }
    public required InsightChartType ChartType { get; init; }
    public required string Sql { get; init; }
    public string? Description { get; init; }

    public bool IsValid { get; set; }
    public string? ValidationError { get; set; }
    public List<string> PreviewLabels { get; set; } = [];
    public List<double> PreviewValues { get; set; } = [];
}
