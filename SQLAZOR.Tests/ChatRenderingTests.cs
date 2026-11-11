using SQLAZOR.Models;
using SQLAZOR.Services;
using Xunit;

namespace SQLAZOR.Tests;

public sealed class ChatMarkdownRendererTests
{
    [Fact]
    public void Renders_gfm_table_with_column_alignment()
    {
        var html = ChatMarkdownRenderer.ToHtml("| Name | Total |\n| --- | ---: |\n| A | 10 |");

        Assert.Contains("<table class=\"md-table\">", html);
        Assert.Contains("<th>Name</th>", html);
        Assert.Contains("<th style=\"text-align:right\">Total</th>", html);
        Assert.Contains("<td style=\"text-align:right\">10</td>", html);
    }

    [Fact]
    public void Escapes_raw_html()
    {
        var html = ChatMarkdownRenderer.ToHtml("<script>alert('x')</script>");

        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public void Renders_headings_lists_quotes_and_rules()
    {
        var html = ChatMarkdownRenderer.ToHtml("# Title\n\n- one\n- **two**\n\n> quoted\n\n---");

        Assert.Contains("<h1>Title</h1>", html);
        Assert.Contains("<ul><li>one</li><li><strong>two</strong></li></ul>", html);
        Assert.Contains("<blockquote>quoted</blockquote>", html);
        Assert.Contains("<hr/>", html);
    }

    [Fact]
    public void Renders_numbered_lists()
    {
        var html = ChatMarkdownRenderer.ToHtml("1. first\n2. second");

        Assert.Contains("<ol><li>first</li><li>second</li></ol>", html);
    }

    [Fact]
    public void Renders_inline_emphasis_code_and_links()
    {
        var html = ChatMarkdownRenderer.ToHtml(
            "Use **bold**, *italic*, `a < b`, and [docs](https://example.com/a?b=1&c=2).");

        Assert.Contains("<strong>bold</strong>", html);
        Assert.Contains("<em>italic</em>", html);
        Assert.Contains("<code>a &lt; b</code>", html);
        Assert.Contains(
            "<a href=\"https://example.com/a?b=1&amp;c=2\" target=\"_blank\" rel=\"noopener noreferrer\">docs</a>",
            html);
    }

    [Fact]
    public void Rejects_unsafe_link_schemes()
    {
        var html = ChatMarkdownRenderer.ToHtml("[click](javascript:alert(1))");

        Assert.DoesNotContain("<a ", html);
        Assert.Contains("[click](javascript:alert(1))", html);
    }

    [Fact]
    public void Autolinks_bare_urls_and_keeps_trailing_punctuation()
    {
        var html = ChatMarkdownRenderer.ToHtml("See https://example.com/x.");

        Assert.Contains("<a href=\"https://example.com/x\" target=\"_blank\"", html);
        Assert.Contains("</a>.</p>", html);
    }

    [Fact]
    public void Inline_code_is_not_processed_as_markdown()
    {
        var html = ChatMarkdownRenderer.ToHtml("`**not bold**`");

        Assert.Contains("<code>**not bold**</code>", html);
        Assert.DoesNotContain("<strong>", html);
    }

    [Fact]
    public void Prose_with_a_pipe_is_not_a_table()
    {
        var html = ChatMarkdownRenderer.ToHtml("a | b without a divider");

        Assert.DoesNotContain("<table", html);
        Assert.Contains("a | b without a divider", html);
    }

    [Fact]
    public void Stray_fence_still_renders_as_code()
    {
        var html = ChatMarkdownRenderer.ToHtml("```sql\nSELECT 1;\n```");

        Assert.Contains("<pre class=\"md-pre\"><code>SELECT 1;</code></pre>", html);
    }

    [Fact]
    public void Single_newlines_become_line_breaks_inside_a_paragraph()
    {
        var html = ChatMarkdownRenderer.ToHtml("line one\nline two");

        Assert.Equal("<p>line one<br/>line two</p>", html);
    }

    [Fact]
    public void Rtl_mode_mirrors_table_alignment_but_keeps_centered_columns()
    {
        var html = ChatMarkdownRenderer.ToHtml(
            "| Name | Total | Note |\n| :--- | ---: | :---: |\n| A | 10 | x |",
            rightToLeft: true);

        Assert.Contains("<th style=\"text-align:right\">Name</th>", html);
        Assert.Contains("<th style=\"text-align:left\">Total</th>", html);
        Assert.Contains("<th style=\"text-align:center\">Note</th>", html);
    }
}

public sealed class ChatTextDirectionTests
{
    [Theory]
    [InlineData("ازاي أعمل كويري يجيب أعلى ١٠ عملاء؟", true)]
    [InlineData("SELECT * FROM dbo.Customers", false)]
    [InlineData("", false)]
    [InlineData("Use the Orders table, but note أن المشتريات", false)]
    [InlineData("١٢٣", false)]
    public void Detects_arabic_prose(string text, bool expected) =>
        Assert.Equal(expected, ChatTextDirection.IsRtl(text));

    [Fact]
    public void Fenced_code_does_not_outweigh_arabic_prose()
    {
        const string content = """
            الفكرة أن الجدول الأساسي هو الطلبات، ثم التفاصيل.
            ```sql
            SELECT c.CustomerId, c.Email, SUM(l.UnitPrice * l.Quantity) AS Revenue
            FROM dbo.Customers c
            JOIN dbo.Orders o ON o.CustomerId = c.CustomerId
            JOIN dbo.OrderLines l ON l.OrderId = o.OrderId
            GROUP BY c.CustomerId, c.Email;
            ```
            أي أن الإيراد يبدأ من سطور الطلب.
            """;

        Assert.True(ChatTextDirection.ForMessage(content));
    }

    [Fact]
    public void English_message_with_one_arabic_word_stays_ltr()
    {
        const string content =
            "The Orders table is the core table here — its Arabic name is جدول الطلبات but the query is standard SQL.";

        Assert.False(ChatTextDirection.ForMessage(content));
    }
}

public sealed class ChatChartParserTests
{
    [Fact]
    public void Parses_the_documented_chart_shape()
    {
        const string json = """
            {"type":"line","title":"Orders","labels":["Jan","Feb"],
             "series":[{"name":"Orders","values":[120,150]},{"name":"Returns","values":["5","7"]}]}
            """;

        Assert.True(ChatChartParser.TryParse(json, out var spec));

        Assert.Equal("line", spec.Type);
        Assert.Equal("Orders", spec.Title);
        Assert.Equal(["Jan", "Feb"], spec.Labels);
        Assert.Equal(2, spec.Series.Count);
        Assert.Equal("Orders", spec.Series[0].Name);
        Assert.Equal([120d, 150d], spec.Series[0].Values);
        Assert.Equal("Returns", spec.Series[1].Name);
        Assert.Equal([5d, 7d], spec.Series[1].Values);
    }

    [Fact]
    public void Parses_single_series_shorthand()
    {
        const string json = """{"title":"Sales","labels":["A","B"],"values":[1,2]}""";

        Assert.True(ChatChartParser.TryParse(json, out var spec));

        Assert.Equal("bar", spec.Type);
        Assert.Equal("Sales", spec.Series.Single().Name);
        Assert.Equal([1d, 2d], spec.Series.Single().Values);
    }

    [Fact]
    public void Parses_chart_js_datasets()
    {
        const string json = """{"type":"pie","labels":["A","B"],"datasets":[{"label":"Count","data":[3,4]}]}""";

        Assert.True(ChatChartParser.TryParse(json, out var spec));

        Assert.Equal("pie", spec.Type);
        Assert.Equal("Count", spec.Series.Single().Name);
        Assert.Equal([3d, 4d], spec.Series.Single().Values);
    }

    [Fact]
    public void Generates_labels_when_none_are_given()
    {
        Assert.True(ChatChartParser.TryParse("""{"values":[5,6]}""", out var spec));

        Assert.Equal(["1", "2"], spec.Labels);
        Assert.Equal("Value", spec.Series.Single().Name);
    }

    [Theory]
    [InlineData("doughnut", "pie")]
    [InlineData("area", "line")]
    [InlineData("scatter", "bar")]
    public void Normalizes_chart_types(string input, string expected)
    {
        var json = $$"""{"type":"{{input}}","labels":["A"],"values":[1]}""";

        Assert.True(ChatChartParser.TryParse(json, out var spec));
        Assert.Equal(expected, spec.Type);
    }

    [Fact]
    public void Coerces_bad_values_to_zero_and_keeps_numeric_rows()
    {
        const string json = """{"labels":["A","B","C"],"values":[1,"2","n/a"]}""";

        Assert.True(ChatChartParser.TryParse(json, out var spec));

        Assert.Equal([1d, 2d, 0d], spec.Series.Single().Values);
    }

    [Fact]
    public void Drops_series_without_any_number()
    {
        const string json = """
            {"labels":["A","B"],"series":[{"name":"Text","values":["x","y"]},{"name":"Count","values":[1,2]}]}
            """;

        Assert.True(ChatChartParser.TryParse(json, out var spec));

        Assert.Equal("Count", spec.Series.Single().Name);
    }

    [Fact]
    public void Caps_labels_and_series()
    {
        var labels = string.Join(',', Enumerable.Range(1, 60).Select(i => $"\"L{i}\""));
        var values = string.Join(',', Enumerable.Range(1, 60));
        var series = string.Join(',', Enumerable.Range(1, 6).Select(i => $$"""{"name":"S{{i}}","values":[{{values}}]}"""));

        var json = $$"""{"labels":[{{labels}}],"series":[{{series}}]}""";

        Assert.True(ChatChartParser.TryParse(json, out var spec));

        Assert.Equal(ChatChartParser.MaxLabels, spec.Labels.Count);
        Assert.Equal(ChatChartParser.MaxLabels, spec.Series[0].Values.Count);
        Assert.Equal(ChatChartParser.MaxSeries, spec.Series.Count);
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("[]")]
    [InlineData("42")]
    [InlineData("""{"labels":["A"],"values":[]}""")]
    [InlineData("""{"labels":["A"],"series":[{"name":"X","values":["abc"]}]}""")]
    public void Rejects_unusable_payloads(string json) =>
        Assert.False(ChatChartParser.TryParse(json, out _));

    [Fact]
    public void Builds_a_chart_from_query_results()
    {
        var result = new SqlQueryRunResult
        {
            Success = true,
            Columns = ["Month", "Orders", "Revenue", "Note"],
            Rows =
            [
                ["Jan", "10", "1,200", "x"],
                ["Feb", "12", "1,500", "y"]
            ]
        };

        var spec = ChatChartParser.FromQueryResult(result);

        Assert.NotNull(spec);
        Assert.Equal(["Jan", "Feb"], spec!.Labels);
        Assert.Equal(2, spec.Series.Count);
        Assert.Equal("Orders", spec.Series[0].Name);
        Assert.Equal([10d, 12d], spec.Series[0].Values);
        Assert.Equal("Revenue", spec.Series[1].Name);
        Assert.Equal([1200d, 1500d], spec.Series[1].Values);
        Assert.True(ChatChartParser.CanChart(result));
    }

    [Fact]
    public void Query_results_need_two_columns_and_a_numeric_column()
    {
        Assert.Null(ChatChartParser.FromQueryResult(new SqlQueryRunResult
        {
            Success = true,
            Columns = ["Name"],
            Rows = [["A"]]
        }));

        Assert.Null(ChatChartParser.FromQueryResult(new SqlQueryRunResult
        {
            Success = true,
            Columns = ["Name", "Email"],
            Rows = [["A", "a@example.com"]]
        }));

        Assert.Null(ChatChartParser.FromQueryResult(new SqlQueryRunResult
        {
            Success = false,
            Columns = ["Month", "Orders"],
            Rows = [["Jan", "10"]]
        }));
    }

    [Fact]
    public void Query_result_chart_caps_rows()
    {
        var rows = Enumerable.Range(1, 50).Select(i => new List<string?> { $"R{i}", i.ToString() }).ToList();

        var spec = ChatChartParser.FromQueryResult(new SqlQueryRunResult
        {
            Success = true,
            Columns = ["Row", "Value"],
            Rows = rows
        });

        Assert.NotNull(spec);
        Assert.Equal(ChatChartParser.MaxLabels, spec!.Labels.Count);
    }
}

public sealed class ChatExportRenderingTests
{
    private static ChatSession SessionWith(string content) => new()
    {
        Title = "Rendering",
        DatabaseName = "Demo",
        Model = "test-model",
        Messages = [new ChatMessage { Role = "assistant", Content = content }]
    };

    private const string MarkdownWithChart = """
        | Table | Rows |
        | --- | ---: |
        | Customers | 42 |

        ```chart
        {"title":"Rows","labels":["Customers"],"values":[42]}
        ```
        """;

    [Fact]
    public void Html_export_renders_markdown_tables_and_chart_data()
    {
        var html = ChatExporter.ToHtml(SessionWith(MarkdownWithChart));

        Assert.Contains("<table class=\"md-table\">", html);
        Assert.Contains("<div class=\"chart\">", html);
        Assert.Contains("<td>Customers</td>", html);
        Assert.DoesNotContain("<pre><code>{&quot;title&quot;", html);
    }

    [Fact]
    public void Markdown_export_turns_chart_blocks_into_tables()
    {
        var markdown = ChatExporter.ToMarkdown(SessionWith(MarkdownWithChart));

        Assert.Contains("| Label | Rows |", markdown);
        Assert.Contains("| Customers | 42 |", markdown);
        Assert.DoesNotContain("```chart", markdown);
    }

    [Fact]
    public void Html_export_marks_arabic_messages_as_right_to_left()
    {
        const string content =
            "الإيراد يبدأ من جدول الطلبات ثم سطور الطلب.\n\n| الجدول | الوصف |\n| --- | --- |\n| dbo.Orders | الطلبات |";

        var html = ChatExporter.ToHtml(SessionWith(content));

        Assert.Contains("<div class=\"msg assistant\" dir=\"rtl\">", html);
        Assert.Contains("<td>dbo.Orders</td>", html);
    }

    [Fact]
    public void Html_export_leaves_english_messages_ltr()
    {
        var html = ChatExporter.ToHtml(SessionWith("This is an English answer."));

        Assert.DoesNotContain("<div class=\"msg assistant\" dir=\"rtl\">", html);
    }
}
