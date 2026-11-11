using SQLAZOR.Models;
using SQLAZOR.Services;
using Xunit;

namespace SQLAZOR.Tests;

public sealed class GenerationProfileMapperTests
{
    [Fact]
    public void Capture_then_apply_round_trips_selections_names_and_options()
    {
        var source = NewState();
        source.Conn.Server = "localhost";
        source.Conn.Database = "ShopDb";
        source.SelectedTableKeys = ["dbo.Orders", "dbo.Customers"];
        source.SelectedViewKeys = ["dbo.vwOrderTotals"];
        source.SelectedProcedureKeys = ["dbo.GetCustomerOrders"];
        source.RootNamespace = "Shop.Api";
        source.DbContextName = "ShopContext";
        source.ApplicationName = "ShopApp";
        source.Options = new GenerationOptions
        {
            GenerateCrudServices = true,
            GenerateApiEndpoints = true,
            GenerateHttpClientServices = true,
            GenerateBlazorPages = true,
            PageStyle = PageStyle.Tabler,
            GenerateProjectScaffold = true,
            GenerateDocumentation = true,
            UseCleanArchitecture = true
        };
        source.DashboardInsights = TestSchema.Insights();
        source.AcceptedInsightTitles = ["OrdersByStatus"];
        source.LookupSuggestions =
        [
            new LookupSuggestion
            {
                TableKey = "dbo.Orders", ColumnName = "Status",
                Options = ["New", "Shipped"], Reason = "categorical"
            }
        ];
        source.AcceptedLookupKeys = ["dbo.Orders.Status", "dbo.Orders.Gone"];

        var profile = GenerationProfileMapper.Capture(source, "Nightly build");

        Assert.Equal("Nightly build", profile.Name);
        Assert.Equal(source.Conn.DatabaseKey, profile.DatabaseKey);
        Assert.Equal("ShopDb", profile.DatabaseName);
        Assert.Equal(["dbo.Customers", "dbo.Orders"], profile.TableKeys);
        Assert.Equal(["dbo.vwOrderTotals"], profile.ViewKeys);

        var target = NewState();
        target.DashboardInsights = TestSchema.Insights();
        var result = GenerationProfileMapper.Apply(profile, target);

        Assert.Equal(0, result.MissingTotal);
        Assert.Equal(["dbo.Customers", "dbo.Orders"], target.SelectedTableKeys.OrderBy(k => k).ToList());
        Assert.Equal(["dbo.vwOrderTotals"], target.SelectedViewKeys);
        Assert.Equal(["dbo.GetCustomerOrders"], target.SelectedProcedureKeys);
        Assert.Equal("Shop.Api", target.RootNamespace);
        Assert.Equal("ShopContext", target.DbContextName);
        Assert.Equal("ShopApp", target.ApplicationName);
        Assert.True(target.Options.GenerateCrudServices);
        Assert.True(target.Options.GenerateApiEndpoints);
        Assert.True(target.Options.GenerateHttpClientServices);
        Assert.True(target.Options.GenerateBlazorPages);
        Assert.Equal(PageStyle.Tabler, target.Options.PageStyle);
        Assert.True(target.Options.GenerateProjectScaffold);
        Assert.True(target.Options.GenerateDocumentation);
        Assert.True(target.Options.UseCleanArchitecture);
        Assert.Equal(1, result.AppliedInsightTitleCount);
        Assert.Contains("OrdersByStatus", target.AcceptedInsightTitles);
        Assert.Equal("categorical", Assert.Single(target.LookupSuggestions).Reason);
        Assert.Contains("dbo.Orders.Status", target.AcceptedLookupKeys);
        Assert.DoesNotContain("dbo.Orders.Gone", target.AcceptedLookupKeys);
    }

    [Fact]
    public void Apply_drops_and_reports_keys_that_no_longer_exist()
    {
        var state = NewState();
        var profile = new GenerationProfile
        {
            TableKeys = ["dbo.Customers", "dbo.Gone"],
            ViewKeys = ["dbo.vwOrderTotals", "dbo.vwGone"],
            ProcedureKeys = ["dbo.GetCustomerOrders", "dbo.GoneProc"]
        };

        var result = GenerationProfileMapper.Apply(profile, state);

        Assert.Equal(["dbo.Customers"], state.SelectedTableKeys);
        Assert.Equal(["dbo.vwOrderTotals"], state.SelectedViewKeys);
        Assert.Equal(["dbo.GetCustomerOrders"], state.SelectedProcedureKeys);
        Assert.Equal(["dbo.Gone"], result.MissingTableKeys);
        Assert.Equal(["dbo.vwGone"], result.MissingViewKeys);
        Assert.Equal(["dbo.GoneProc"], result.MissingProcedureKeys);
        Assert.Equal(3, result.MissingTotal);
    }

    [Fact]
    public void Apply_without_a_loaded_schema_keeps_every_saved_key()
    {
        var state = new GenerationState();
        var profile = new GenerationProfile
        {
            TableKeys = ["dbo.Customers", "dbo.Whatever"],
            ProcedureKeys = ["dbo.Anything"]
        };

        var result = GenerationProfileMapper.Apply(profile, state);

        Assert.Equal(0, result.MissingTotal);
        Assert.Equal(2, state.SelectedTableKeys.Count);
        Assert.Contains("dbo.Whatever", state.SelectedTableKeys);
        Assert.Contains("dbo.Anything", state.SelectedProcedureKeys);
    }

    [Fact]
    public void Apply_re_establishes_option_dependencies()
    {
        var state = NewState();
        var profile = new GenerationProfile
        {
            Options = new GenerationOptions
            {
                GenerateCrudServices = false,
                GenerateApiEndpoints = true,
                GenerateHttpClientServices = true,
                GenerateBlazorPages = true,
                PageStyle = PageStyle.Tabler,
                GenerateProjectScaffold = true,
                UseCleanArchitecture = true
            }
        };

        GenerationProfileMapper.Apply(profile, state);

        Assert.False(state.Options.GenerateApiEndpoints);
        Assert.False(state.Options.GenerateHttpClientServices);
        Assert.False(state.Options.GenerateBlazorPages);
        Assert.Equal(PageStyle.Plain, state.Options.PageStyle);
        Assert.True(state.Options.GenerateProjectScaffold);
        Assert.True(state.Options.UseCleanArchitecture);
    }

    [Fact]
    public void Apply_normalizes_out_of_range_page_style_and_clean_architecture()
    {
        var state = NewState();
        var profile = new GenerationProfile
        {
            Options = new GenerationOptions
            {
                GenerateCrudServices = true,
                GenerateBlazorPages = true,
                PageStyle = (PageStyle)99,
                GenerateProjectScaffold = false,
                UseCleanArchitecture = true
            }
        };

        GenerationProfileMapper.Apply(profile, state);

        Assert.Equal(PageStyle.Plain, state.Options.PageStyle);
        Assert.False(state.Options.UseCleanArchitecture);
    }

    [Fact]
    public void Apply_only_selects_insights_that_are_still_suggested()
    {
        var state = NewState();
        state.DashboardInsights = TestSchema.Insights();
        state.AcceptedInsightTitles = ["SomethingElse"];

        var profile = new GenerationProfile
        {
            AcceptedInsightTitles = ["OrdersByStatus", "NoLongerSuggested"]
        };

        var result = GenerationProfileMapper.Apply(profile, state);

        Assert.Equal(1, result.AppliedInsightTitleCount);
        Assert.Equal(["OrdersByStatus"], state.AcceptedInsightTitles);
    }

    private static GenerationState NewState()
    {
        var state = new GenerationState { Schema = TestSchema.Create() };
        state.Procedures =
        [
            new StoredProcedureSummary
            {
                Schema = "dbo", Name = "GetCustomerOrders", ClassBaseName = "GetCustomerOrder", ParameterCount = 2
            },
            new StoredProcedureSummary
            {
                Schema = "dbo", Name = "RebuildSearchIndex", ClassBaseName = "RebuildSearchIndex", ParameterCount = 2
            }
        ];
        return state;
    }
}

public sealed class GenerationProfileJsonTests
{
    [Fact]
    public void Round_trip_preserves_content_and_assigns_a_new_id()
    {
        var profile = new GenerationProfile
        {
            Name = "Reporting",
            DatabaseKey = "localhost|shopdb",
            DatabaseName = "ShopDb",
            TableKeys = ["dbo.Orders", "dbo.Customers"],
            ViewKeys = ["dbo.vwOrderTotals"],
            ProcedureKeys = ["dbo.GetCustomerOrders"],
            AcceptedInsightTitles = ["OrdersByStatus"],
            LookupSuggestions =
            [
                new LookupSuggestion
                {
                    TableKey = "dbo.Orders", ColumnName = "Status",
                    Options = ["New", "Shipped"], Reason = "categorical"
                },
                new LookupSuggestion
                {
                    TableKey = "dbo.Orders", ColumnName = "CustomerName",
                    ReferencedTableKey = "dbo.Customers", LabelColumns = ["FirstName", "LastName"]
                }
            ],
            AcceptedLookupKeys = ["dbo.Orders.Status"],
            Options = new GenerationOptions { GenerateCrudServices = true, GenerateApiEndpoints = true }
        };

        var parsed = GenerationProfileJson.TryParse(GenerationProfileJson.Serialize(profile));

        Assert.NotNull(parsed);
        Assert.NotEqual(profile.Id, parsed.Id);
        Assert.Equal("Reporting", parsed.Name);
        Assert.Equal("localhost|shopdb", parsed.DatabaseKey);
        Assert.Equal(["dbo.Orders", "dbo.Customers"], parsed.TableKeys);
        Assert.True(parsed.Options.GenerateCrudServices);
        Assert.True(parsed.Options.GenerateApiEndpoints);
        Assert.Equal(2, parsed.LookupSuggestions.Count);
        Assert.Equal(["New", "Shipped"], parsed.LookupSuggestions[0].Options);
        Assert.Equal(["FirstName", "LastName"], parsed.LookupSuggestions[1].LabelColumns);
        Assert.Equal(["dbo.Orders.Status"], parsed.AcceptedLookupKeys);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("[1,2,3]")]
    public void Rejects_content_that_is_not_a_profile(string json)
    {
        Assert.Null(GenerationProfileJson.TryParse(json));
    }

    [Fact]
    public void Missing_name_falls_back_to_a_readable_default()
    {
        var parsed = GenerationProfileJson.TryParse("{ \"databaseName\": \"ShopDb\" }");

        Assert.NotNull(parsed);
        Assert.Equal("Imported profile", parsed.Name);
    }
}

public sealed class FileGenerationProfileStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "sqlazor-profile-tests-" + Guid.NewGuid().ToString("N"));

    private FileGenerationProfileStore Store => new(_directory);

    [Fact]
    public async Task Save_list_load_and_delete_round_trip()
    {
        var profile = new GenerationProfile
        {
            Name = "Nightly",
            DatabaseKey = "localhost|shopdb",
            DatabaseName = "ShopDb",
            TableKeys = ["dbo.Customers", "dbo.Orders"]
        };

        await Store.SaveAsync(profile);

        var summaries = await Store.ListAsync("localhost|shopdb");
        var summary = Assert.Single(summaries);
        Assert.Equal(profile.Id, summary.Id);
        Assert.Equal("Nightly", summary.Name);
        Assert.Equal(2, summary.TableCount);

        var loaded = await Store.LoadAsync(profile.Id);
        Assert.NotNull(loaded);
        Assert.Equal(profile.TableKeys, loaded.TableKeys);

        await Store.DeleteAsync(profile.Id);
        Assert.Empty(await Store.ListAsync());
        Assert.Null(await Store.LoadAsync(profile.Id));
    }

    [Fact]
    public async Task List_filters_by_database_and_sorts_by_name()
    {
        await Store.SaveAsync(new GenerationProfile { Name = "Zeta", DatabaseKey = "a|one" });
        await Store.SaveAsync(new GenerationProfile { Name = "Alpha", DatabaseKey = "a|one" });
        await Store.SaveAsync(new GenerationProfile { Name = "Other", DatabaseKey = "b|two" });

        var names = (await Store.ListAsync("A|ONE")).Select(p => p.Name).ToList();

        Assert.Equal(["Alpha", "Zeta"], names);
        Assert.Equal(3, (await Store.ListAsync()).Count);
    }

    [Fact]
    public async Task Malformed_files_are_skipped_instead_of_breaking_the_list()
    {
        await Store.SaveAsync(new GenerationProfile { Name = "Good", DatabaseKey = "a|one" });
        await File.WriteAllTextAsync(Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".json"), "{ broken");

        var summaries = await Store.ListAsync();

        Assert.Single(summaries);
        Assert.Equal("Good", summaries[0].Name);
    }

    [Theory]
    [InlineData("..")]
    [InlineData("../escape")]
    [InlineData("C:\\Windows\\system32")]
    public async Task Ids_outside_the_generated_hex_shape_are_rejected(string id)
    {
        Assert.Null(await Store.LoadAsync(id));
        await Store.DeleteAsync(id);
        Assert.False(Directory.Exists(Path.Combine(_directory, "..", "escape")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
