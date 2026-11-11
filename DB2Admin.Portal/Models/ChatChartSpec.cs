namespace SQLAZOR.Models;

/// <summary>A chart the assistant asked for, or one derived from a query result.</summary>
public sealed class ChatChartSpec
{
    public string Type { get; init; } = "bar";
    public string Title { get; init; } = string.Empty;
    public List<string> Labels { get; init; } = [];
    public List<ChatChartSeries> Series { get; init; } = [];
}

public sealed class ChatChartSeries
{
    public string Name { get; init; } = string.Empty;
    public List<double> Values { get; init; } = [];
}
