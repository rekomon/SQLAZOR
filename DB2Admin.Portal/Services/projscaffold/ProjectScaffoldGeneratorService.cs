using SQLAZOR.Models;
using System.Reflection.PortableExecutable;
using System.Text;

namespace SQLAZOR.Services;

public class ProjectScaffoldGeneratorService : IProjectScaffoldGeneratorService
{
    // ----------------------------------------------------------------------
    // Full project scaffold (csproj, Program.cs, Components shell, admin layout)
    // ----------------------------------------------------------------------

    public List<GeneratedFile> GenerateProjectScaffold(
        DatabaseSchema schema,
        IEnumerable<string> selectedTableKeys,
        string rootNamespace,
        string applicationName,
        string dbContextName,
        string connectionString,
        bool includeControllers,
        bool includeCrudServices,
        bool includeBlazorPages,
        PageStyle pageStyle,
        List<DashboardInsightCandidate> acceptedInsights)
    {
        var selected = selectedTableKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var tables = schema.Tables.Where(t => selected.Contains(t.FullyQualifiedName) && !t.IsView).ToList();
        var tablesWithPk = tables.Where(t => t.PrimaryKeyColumns.Count > 0).OrderBy(t => t.ClassName).ToList();

        // Nav links / "manage data" cards only make sense if list/create/edit pages actually exist.
        var navTables = includeBlazorPages ? tablesWithPk : [];
        // Stat cards + charts only need Dapper (CRUD services), independent of whether Blazor pages exist.
        var statTables = includeCrudServices ? tablesWithPk : [];
        var validInsights = includeCrudServices ? acceptedInsights.Where(i => i.IsValid).ToList() : [];
        var hasDashboardData = statTables.Count > 0 || validInsights.Count > 0;

        var files = new List<GeneratedFile>
        {
            GenerateCsproj(applicationName, rootNamespace, includeCrudServices, pageStyle),
            GenerateProgramCs(rootNamespace, dbContextName, includeControllers, includeCrudServices, pageStyle),
            GenerateAppSettings(connectionString),
            GenerateLaunchSettings(),
            GenerateComponentsImports(rootNamespace, pageStyle),
            GenerateAppRazor(applicationName, pageStyle, validInsights.Count > 0),
            GenerateRoutesRazor(),
            GenerateMainLayout(applicationName, pageStyle),
            GenerateNavMenu(navTables, pageStyle),
            GenerateDashboardPage(applicationName, navTables, statTables, validInsights, pageStyle)
        };

        if (pageStyle == PageStyle.Plain)
        {
            files.Add(GenerateAdminCss());
        }

        if (hasDashboardData)
        {
            files.Add(GenerateDashboardStatsService(rootNamespace, statTables, validInsights));

            if (validInsights.Count > 0 && pageStyle != PageStyle.MudBlazor)
            {
                files.Add(GenerateChartsJs());
            }
        }

        return files;
    }

    private static GeneratedFile GenerateCsproj(string applicationName, string rootNamespace, bool includeCrudServices, PageStyle pageStyle)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<Project Sdk=\"Microsoft.NET.Sdk.Web\">");
        sb.AppendLine();
        sb.AppendLine("  <PropertyGroup>");
        sb.AppendLine("    <TargetFramework>net8.0</TargetFramework>");
        sb.AppendLine("    <Nullable>enable</Nullable>");
        sb.AppendLine("    <ImplicitUsings>enable</ImplicitUsings>");
        sb.AppendLine($"    <RootNamespace>{rootNamespace}</RootNamespace>");
        sb.AppendLine($"    <AssemblyName>{applicationName}</AssemblyName>");
        sb.AppendLine($"    <UserSecretsId>{Guid.NewGuid()}</UserSecretsId>");
        sb.AppendLine("  </PropertyGroup>");
        sb.AppendLine();
        sb.AppendLine("  <ItemGroup>");
        sb.AppendLine("    <PackageReference Include=\"Microsoft.EntityFrameworkCore.SqlServer\" Version=\"8.0.11\" />");
        sb.AppendLine("    <PackageReference Include=\"Microsoft.EntityFrameworkCore.Design\" Version=\"8.0.11\">");
        sb.AppendLine("      <PrivateAssets>all</PrivateAssets>");
        sb.AppendLine("      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>");
        sb.AppendLine("    </PackageReference>");

        if (includeCrudServices)
        {
            sb.AppendLine("    <PackageReference Include=\"Microsoft.Data.SqlClient\" Version=\"5.2.2\" />");
            sb.AppendLine("    <PackageReference Include=\"Dapper\" Version=\"2.1.35\" />");
            sb.AppendLine("    <PackageReference Include=\"Mapster\" Version=\"7.4.0\" />");
        }

        if (pageStyle == PageStyle.MudBlazor)
        {
            sb.AppendLine("    <PackageReference Include=\"MudBlazor\" Version=\"7.15.0\" />");
        }
        // Tabler needs no NuGet package - its CSS/JS are loaded from CDN in App.razor.

        sb.AppendLine("  </ItemGroup>");
        sb.AppendLine();
        sb.AppendLine("</Project>");

        return new GeneratedFile { RelativePath = $"{applicationName}.csproj", Content = sb.ToString() };
    }

    private static GeneratedFile GenerateProgramCs(string rootNamespace, string dbContextName, bool includeControllers, bool includeCrudServices, PageStyle pageStyle)
    {
        var sb = new StringBuilder();
        sb.AppendLine("using Microsoft.EntityFrameworkCore;");
        if (includeCrudServices)
        {
            sb.AppendLine("using System.Data;");
            sb.AppendLine("using Microsoft.Data.SqlClient;");
        }
        if (pageStyle == PageStyle.MudBlazor)
        {
            sb.AppendLine("using MudBlazor.Services;");
        }
        sb.AppendLine($"using {rootNamespace};");
        sb.AppendLine($"using {rootNamespace}.Components;");
        if (includeCrudServices)
        {
            sb.AppendLine($"using {rootNamespace}.Services;");
        }
        sb.AppendLine();
        sb.AppendLine("var builder = WebApplication.CreateBuilder(args);");
        sb.AppendLine();
        sb.AppendLine("builder.Services.AddRazorComponents()");
        sb.AppendLine("    .AddInteractiveServerComponents();");
        sb.AppendLine();

        if (includeControllers)
        {
            sb.AppendLine("builder.Services.AddControllers();");
            sb.AppendLine();
        }

        sb.AppendLine("var connectionString = builder.Configuration.GetConnectionString(\"DefaultConnection\")");
        sb.AppendLine("    ?? throw new InvalidOperationException(\"Connection string 'DefaultConnection' not found - check appsettings.json.\");");
        sb.AppendLine();
        sb.AppendLine($"builder.Services.AddDbContext<{dbContextName}>(options => options.UseSqlServer(connectionString));");
        sb.AppendLine();

        if (includeCrudServices)
        {
            sb.AppendLine("// Dapper needs a raw IDbConnection, registered separately from the DbContext above -");
            sb.AppendLine("// the generated {Table}Service classes (and the dashboard stats) use Dapper directly, not EF Core.");
            sb.AppendLine("builder.Services.AddScoped<IDbConnection>(_ => new SqlConnection(connectionString));");
            sb.AppendLine("builder.Services.AddScoped<DashboardStatsService>();");
            sb.AppendLine("builder.Services.AddGeneratedCrudServices();");
            sb.AppendLine();
        }

        if (pageStyle == PageStyle.MudBlazor)
        {
            sb.AppendLine("builder.Services.AddMudServices();");
            sb.AppendLine();
        }

        sb.AppendLine("var app = builder.Build();");
        sb.AppendLine();
        sb.AppendLine("if (!app.Environment.IsDevelopment())");
        sb.AppendLine("{");
        sb.AppendLine("    app.UseExceptionHandler(\"/Error\", createScopeForErrors: true);");
        sb.AppendLine("    app.UseHsts();");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("app.UseHttpsRedirection();");
        sb.AppendLine("app.UseAntiforgery();");
        sb.AppendLine("app.MapStaticAssets();");
        sb.AppendLine();

        if (includeControllers)
        {
            sb.AppendLine("app.MapControllers();");
            sb.AppendLine();
        }

        sb.AppendLine("app.MapRazorComponents<App>()");
        sb.AppendLine("    .AddInteractiveServerRenderMode();");
        sb.AppendLine();
        sb.AppendLine("app.Run();");

        return new GeneratedFile { RelativePath = "Program.cs", Content = sb.ToString() };
    }

    private static GeneratedFile GenerateAppSettings(string connectionString)
    {
        var escaped = connectionString.Replace("\\", "\\\\").Replace("\"", "\\\"");

        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine("  \"_comment\": \"DefaultConnection below was copied from what you used in SQLAZOR to read the schema. Move it to user secrets / environment variables before this leaves your machine.\",");
        sb.AppendLine("  \"ConnectionStrings\": {");
        sb.AppendLine($"    \"DefaultConnection\": \"{escaped}\"");
        sb.AppendLine("  },");
        sb.AppendLine("  \"Logging\": {");
        sb.AppendLine("    \"LogLevel\": {");
        sb.AppendLine("      \"Default\": \"Information\",");
        sb.AppendLine("      \"Microsoft.AspNetCore\": \"Warning\"");
        sb.AppendLine("    }");
        sb.AppendLine("  },");
        sb.AppendLine("  \"AllowedHosts\": \"*\"");
        sb.AppendLine("}");

        return new GeneratedFile { RelativePath = "appsettings.json", Content = sb.ToString() };
    }

    private static GeneratedFile GenerateLaunchSettings()
    {
        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine("  \"$schema\": \"https://json.schemastore.org/launchsettings.json\",");
        sb.AppendLine("  \"profiles\": {");
        sb.AppendLine("    \"http\": {");
        sb.AppendLine("      \"commandName\": \"Project\",");
        sb.AppendLine("      \"dotnetRunMessages\": true,");
        sb.AppendLine("      \"launchBrowser\": true,");
        sb.AppendLine("      \"applicationUrl\": \"http://localhost:5080\",");
        sb.AppendLine("      \"environmentVariables\": { \"ASPNETCORE_ENVIRONMENT\": \"Development\" }");
        sb.AppendLine("    },");
        sb.AppendLine("    \"https\": {");
        sb.AppendLine("      \"commandName\": \"Project\",");
        sb.AppendLine("      \"dotnetRunMessages\": true,");
        sb.AppendLine("      \"launchBrowser\": true,");
        sb.AppendLine("      \"applicationUrl\": \"https://localhost:7080;http://localhost:5080\",");
        sb.AppendLine("      \"environmentVariables\": { \"ASPNETCORE_ENVIRONMENT\": \"Development\" }");
        sb.AppendLine("    }");
        sb.AppendLine("  }");
        sb.AppendLine("}");

        return new GeneratedFile { RelativePath = "Properties/launchSettings.json", Content = sb.ToString() };
    }

    private static GeneratedFile GenerateComponentsImports(string rootNamespace, PageStyle pageStyle)
    {
        var sb = new StringBuilder();
        sb.AppendLine("@using System.Net.Http");
        sb.AppendLine("@using Microsoft.AspNetCore.Components.Forms");
        sb.AppendLine("@using Microsoft.AspNetCore.Components.Routing");
        sb.AppendLine("@using Microsoft.AspNetCore.Components.Web");
        sb.AppendLine("@using Microsoft.JSInterop");
        sb.AppendLine($"@using {rootNamespace}");
        sb.AppendLine($"@using {rootNamespace}.Components");
        sb.AppendLine($"@using {rootNamespace}.Components.Layout");
        sb.AppendLine($"@using {rootNamespace}.Entities");
        sb.AppendLine($"@using {rootNamespace}.Dtos");
        sb.AppendLine($"@using {rootNamespace}.Services");
        sb.AppendLine($"@using {rootNamespace}.Common");
        if (pageStyle == PageStyle.MudBlazor)
        {
            sb.AppendLine("@using MudBlazor");
        }

        return new GeneratedFile { RelativePath = "Components/_Imports.razor", Content = sb.ToString() };
    }

    private static GeneratedFile GenerateAppRazor(string applicationName, PageStyle pageStyle, bool hasCharts)
    {
        var sb = new StringBuilder();
        sb.AppendLine("@using Microsoft.AspNetCore.Components.Web");
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"en\">");
        sb.AppendLine("<head>");
        sb.AppendLine("    <meta charset=\"utf-8\" />");
        sb.AppendLine("    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\" />");
        sb.AppendLine($"    <title>{applicationName}</title>");
        sb.AppendLine("    <base href=\"/\" />");

        switch (pageStyle)
        {
            case PageStyle.MudBlazor:
                sb.AppendLine("    <link href=\"_content/MudBlazor/MudBlazor.min.css\" rel=\"stylesheet\" />");
                break;
            case PageStyle.Tabler:
                // "@@" escapes to a literal "@" once Razor parses this generated file - required for
                // the "@tabler/core" scoped npm package name to not be read as a Razor expression.
                sb.AppendLine("    <link rel=\"stylesheet\" href=\"https://cdn.jsdelivr.net/npm/@@tabler/core@1.0.0-beta20/dist/css/tabler.min.css\" />");
                break;
            default:
                sb.AppendLine("    <link rel=\"stylesheet\" href=\"css/admin.css\" />");
                break;
        }

        sb.AppendLine("    <HeadOutlet />");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine("    <Routes @rendermode=\"InteractiveServer\" />");
        sb.AppendLine("    <script src=\"_framework/blazor.web.js\"></script>");

        if (pageStyle == PageStyle.MudBlazor)
        {
            sb.AppendLine("    <script src=\"_content/MudBlazor/MudBlazor.min.js\"></script>");
        }
        else if (pageStyle == PageStyle.Tabler)
        {
            sb.AppendLine("    <script src=\"https://cdn.jsdelivr.net/npm/@@tabler/core@1.0.0-beta20/dist/js/tabler.min.js\"></script>");
        }

        if (hasCharts && pageStyle != PageStyle.MudBlazor)
        {
            sb.AppendLine("    <script src=\"https://cdn.jsdelivr.net/npm/apexcharts\"></script>");
            sb.AppendLine("    <script src=\"js/charts.js\"></script>");
        }

        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        return new GeneratedFile { RelativePath = "Components/App.razor", Content = sb.ToString() };
    }

    private static GeneratedFile GenerateRoutesRazor()
    {
        var sb = new StringBuilder();
        sb.AppendLine("@using Microsoft.AspNetCore.Components.Routing");
        sb.AppendLine();
        sb.AppendLine("<Router AppAssembly=\"typeof(Program).Assembly\">");
        sb.AppendLine("    <Found Context=\"routeData\">");
        sb.AppendLine("        <RouteView RouteData=\"routeData\" DefaultLayout=\"typeof(Layout.MainLayout)\" />");
        sb.AppendLine("        <FocusOnNavigate RouteData=\"routeData\" Selector=\"h1\" />");
        sb.AppendLine("    </Found>");
        sb.AppendLine("    <NotFound>");
        sb.AppendLine("        <LayoutView Layout=\"typeof(Layout.MainLayout)\">");
        sb.AppendLine("            <p role=\"alert\">Sorry, there's nothing at this address.</p>");
        sb.AppendLine("        </LayoutView>");
        sb.AppendLine("    </NotFound>");
        sb.AppendLine("</Router>");

        return new GeneratedFile { RelativePath = "Components/Routes.razor", Content = sb.ToString() };
    }

    private static GeneratedFile GenerateMainLayout(string applicationName, PageStyle pageStyle)
    {
        var sb = new StringBuilder();
        sb.AppendLine("@inherits LayoutComponentBase");
        sb.AppendLine();

        switch (pageStyle)
        {
            case PageStyle.MudBlazor:
                sb.AppendLine("<MudThemeProvider />");
                sb.AppendLine("<MudPopoverProvider />");
                sb.AppendLine("<MudDialogProvider />");
                sb.AppendLine("<MudSnackbarProvider />");
                sb.AppendLine();
                sb.AppendLine("<MudLayout>");
                sb.AppendLine("    <MudAppBar Elevation=\"1\">");
                sb.AppendLine("        <MudIconButton Icon=\"@Icons.Material.Filled.Menu\" Color=\"Color.Inherit\" Edge=\"Edge.Start\"");
                sb.AppendLine("                       OnClick=\"@(() => _drawerOpen = !_drawerOpen)\" />");
                sb.AppendLine($"        <MudText Typo=\"Typo.h6\" Class=\"ml-3\">{applicationName}</MudText>");
                sb.AppendLine("        <MudSpacer />");
                sb.AppendLine("    </MudAppBar>");
                sb.AppendLine("    <MudDrawer @bind-Open=\"_drawerOpen\" Elevation=\"2\">");
                sb.AppendLine("        <MudDrawerHeader>");
                sb.AppendLine($"            <MudText Typo=\"Typo.h6\">{applicationName}</MudText>");
                sb.AppendLine("        </MudDrawerHeader>");
                sb.AppendLine("        <NavMenu />");
                sb.AppendLine("    </MudDrawer>");
                sb.AppendLine("    <MudMainContent>");
                sb.AppendLine("        <MudContainer MaxWidth=\"MaxWidth.ExtraLarge\" Class=\"my-6\">");
                sb.AppendLine("            @Body");
                sb.AppendLine("        </MudContainer>");
                sb.AppendLine("    </MudMainContent>");
                sb.AppendLine("</MudLayout>");
                sb.AppendLine();
                sb.AppendLine("@code {");
                sb.AppendLine("    private bool _drawerOpen = true;");
                sb.AppendLine("}");
                break;

            case PageStyle.Tabler:
                // Standard Tabler "vertical navbar" layout shell - https://preview.tabler.io conventions.
                sb.AppendLine("<div class=\"page\">");
                sb.AppendLine("    <aside class=\"navbar navbar-vertical navbar-expand-lg navbar-dark\">");
                sb.AppendLine("        <div class=\"container-fluid\">");
                sb.AppendLine("            <button class=\"navbar-toggler\" type=\"button\" data-bs-toggle=\"collapse\" data-bs-target=\"#sidebar-menu\">");
                sb.AppendLine("                <span class=\"navbar-toggler-icon\"></span>");
                sb.AppendLine("            </button>");
                sb.AppendLine("            <h1 class=\"navbar-brand navbar-brand-autodark\">");
                sb.AppendLine($"                <a href=\"/\">{applicationName}</a>");
                sb.AppendLine("            </h1>");
                sb.AppendLine("            <div class=\"collapse navbar-collapse\" id=\"sidebar-menu\">");
                sb.AppendLine("                <NavMenu />");
                sb.AppendLine("            </div>");
                sb.AppendLine("        </div>");
                sb.AppendLine("    </aside>");
                sb.AppendLine("    <div class=\"page-wrapper\">");
                sb.AppendLine("        <div class=\"page-body\">");
                sb.AppendLine("            <div class=\"container-xl\">");
                sb.AppendLine("                @Body");
                sb.AppendLine("            </div>");
                sb.AppendLine("        </div>");
                sb.AppendLine("    </div>");
                sb.AppendLine("</div>");
                break;

            default: // Plain
                sb.AppendLine("<div class=\"admin-shell\">");
                sb.AppendLine("    <aside class=\"admin-sidebar\">");
                sb.AppendLine($"        <div class=\"admin-brand\">{applicationName}</div>");
                sb.AppendLine("        <NavMenu />");
                sb.AppendLine("    </aside>");
                sb.AppendLine("    <div class=\"admin-content-area\">");
                sb.AppendLine("        <main class=\"admin-main\">");
                sb.AppendLine("            @Body");
                sb.AppendLine("        </main>");
                sb.AppendLine("    </div>");
                sb.AppendLine("</div>");
                break;
        }

        return new GeneratedFile { RelativePath = "Components/Layout/MainLayout.razor", Content = sb.ToString() };
    }

    private static GeneratedFile GenerateNavMenu(List<TableInfo> tables, PageStyle pageStyle)
    {
        var sb = new StringBuilder();

        switch (pageStyle)
        {
            case PageStyle.MudBlazor:
                sb.AppendLine("<MudNavMenu>");
                sb.AppendLine("    <MudNavLink Href=\"/\" Match=\"NavLinkMatch.All\" Icon=\"@Icons.Material.Filled.Dashboard\">Dashboard</MudNavLink>");
                if (tables.Count > 0)
                {
                    sb.AppendLine("    <MudDivider Class=\"my-2\" />");
                    foreach (var table in tables)
                    {
                        var plural = NamingHelper.Pluralize(table.ClassName);
                        sb.AppendLine($"    <MudNavLink Href=\"/{plural.ToLowerInvariant()}\" Icon=\"@Icons.Material.Filled.List\">{plural}</MudNavLink>");
                    }
                }
                sb.AppendLine("</MudNavMenu>");
                break;

            case PageStyle.Tabler:
                sb.AppendLine("<ul class=\"navbar-nav pt-lg-3\">");
                sb.AppendLine("    <li class=\"nav-item\">");
                sb.AppendLine("        <NavLink class=\"nav-link\" href=\"/\" Match=\"NavLinkMatch.All\">");
                sb.AppendLine("            <span class=\"nav-link-title\">Dashboard</span>");
                sb.AppendLine("        </NavLink>");
                sb.AppendLine("    </li>");
                foreach (var table in tables)
                {
                    var plural = NamingHelper.Pluralize(table.ClassName);
                    sb.AppendLine("    <li class=\"nav-item\">");
                    sb.AppendLine($"        <NavLink class=\"nav-link\" href=\"/{plural.ToLowerInvariant()}\">");
                    sb.AppendLine($"            <span class=\"nav-link-title\">{plural}</span>");
                    sb.AppendLine("        </NavLink>");
                    sb.AppendLine("    </li>");
                }
                sb.AppendLine("</ul>");
                break;

            default: // Plain
                sb.AppendLine("<nav class=\"admin-nav\">");
                sb.AppendLine("    <NavLink class=\"admin-nav-link\" href=\"/\" Match=\"NavLinkMatch.All\">Dashboard</NavLink>");
                foreach (var table in tables)
                {
                    var plural = NamingHelper.Pluralize(table.ClassName);
                    sb.AppendLine($"    <NavLink class=\"admin-nav-link\" href=\"/{plural.ToLowerInvariant()}\">{plural}</NavLink>");
                }
                sb.AppendLine("</nav>");
                break;
        }

        return new GeneratedFile { RelativePath = "Components/Layout/NavMenu.razor", Content = sb.ToString() };
    }

    private static GeneratedFile GenerateDashboardPage(
        string applicationName,
        List<TableInfo> navTables,
        List<TableInfo> statTables,
        List<DashboardInsightCandidate> insights,
        PageStyle pageStyle)
    {
        var hasStats = statTables.Count > 0;
        var hasCharts = insights.Count > 0;

        var sb = new StringBuilder();
        sb.AppendLine("@page \"/\"");
        if (hasStats || hasCharts)
        {
            sb.AppendLine("@inject DashboardStatsService Stats");
        }
        if (hasCharts && pageStyle != PageStyle.MudBlazor)
        {
            sb.AppendLine("@inject IJSRuntime JS");
        }
        sb.AppendLine();
        sb.AppendLine($"<PageTitle>{applicationName}</PageTitle>");
        sb.AppendLine();

        // ---- header ----
        switch (pageStyle)
        {
            case PageStyle.MudBlazor:
                sb.AppendLine($"<MudText Typo=\"Typo.h4\" Class=\"mb-6\">Welcome to {applicationName}</MudText>");
                break;
            case PageStyle.Tabler:
                sb.AppendLine("<div class=\"page-header d-print-none\">");
                sb.AppendLine("    <div class=\"row align-items-center\">");
                sb.AppendLine("        <div class=\"col\">");
                sb.AppendLine($"            <h2 class=\"page-title\">Welcome to {applicationName}</h2>");
                sb.AppendLine("        </div>");
                sb.AppendLine("    </div>");
                sb.AppendLine("</div>");
                break;
            default:
                sb.AppendLine($"<h3>Welcome to {applicationName}</h3>");
                break;
        }
        sb.AppendLine();

        // ---- stat cards (row counts) ----
        if (hasStats)
        {
            switch (pageStyle)
            {
                case PageStyle.MudBlazor:
                    sb.AppendLine("<MudGrid Class=\"mb-6\">");
                    foreach (var table in statTables)
                    {
                        var plural = NamingHelper.Pluralize(table.ClassName);
                        sb.AppendLine("    <MudItem xs=\"12\" sm=\"6\" md=\"3\">");
                        sb.AppendLine("        <MudPaper Class=\"pa-4\">");
                        sb.AppendLine($"            <MudText Typo=\"Typo.subtitle2\">{plural}</MudText>");
                        sb.AppendLine($"            <MudText Typo=\"Typo.h4\">@_{Constant.LowerFirst(plural)}Count</MudText>");
                        sb.AppendLine("        </MudPaper>");
                        sb.AppendLine("    </MudItem>");
                    }
                    sb.AppendLine("</MudGrid>");
                    break;
                case PageStyle.Tabler:
                    sb.AppendLine("<div class=\"row row-deck row-cards mb-4\">");
                    foreach (var table in statTables)
                    {
                        var plural = NamingHelper.Pluralize(table.ClassName);
                        sb.AppendLine("    <div class=\"col-sm-6 col-lg-3\">");
                        sb.AppendLine("        <div class=\"card\">");
                        sb.AppendLine("            <div class=\"card-body\">");
                        sb.AppendLine($"                <div class=\"subheader\">{plural}</div>");
                        sb.AppendLine($"                <div class=\"h1 mb-0\">@_{Constant.LowerFirst(plural)}Count</div>");
                        sb.AppendLine("            </div>");
                        sb.AppendLine("        </div>");
                        sb.AppendLine("    </div>");
                    }
                    sb.AppendLine("</div>");
                    break;
                default:
                    sb.AppendLine("<div class=\"dashboard-grid mb-4\">");
                    foreach (var table in statTables)
                    {
                        var plural = NamingHelper.Pluralize(table.ClassName);
                        sb.AppendLine("    <div class=\"dashboard-card\">");
                        sb.AppendLine($"        <h4>{plural}</h4>");
                        sb.AppendLine($"        <span class=\"stat-number\">@_{Constant.LowerFirst(plural)}Count</span>");
                        sb.AppendLine("    </div>");
                    }
                    sb.AppendLine("</div>");
                    break;
            }
            sb.AppendLine();
        }

        // ---- AI-suggested charts ----
        if (hasCharts)
        {
            if (pageStyle == PageStyle.MudBlazor)
            {
                sb.AppendLine("<MudGrid Class=\"mb-6\">");
                for (var i = 0; i < insights.Count; i++)
                {
                    var insight = insights[i];
                    sb.AppendLine("    <MudItem xs=\"12\" md=\"6\">");
                    sb.AppendLine("        <MudPaper Class=\"pa-4\">");
                    sb.AppendLine($"            <MudText Typo=\"Typo.subtitle1\" Class=\"mb-2\">{insight.Title}</MudText>");
                    if (insight.ChartType == InsightChartType.Pie)
                    {
                        sb.AppendLine($"            <MudChart Type=\"ChartType.Pie\" InputData=\"@_data{i}\" InputLabels=\"@_labels{i}\" Width=\"100%\" Height=\"300px\" />");
                    }
                    else
                    {
                        var mudType = insight.ChartType == InsightChartType.Line ? "ChartType.Line" : "ChartType.Bar";
                        sb.AppendLine($"            <MudChart Type=\"{mudType}\" ChartSeries=\"@_series{i}\" XAxisLabels=\"@_labels{i}\" Width=\"100%\" Height=\"300px\" />");
                    }
                    sb.AppendLine("        </MudPaper>");
                    sb.AppendLine("    </MudItem>");
                }
                sb.AppendLine("</MudGrid>");
            }
            else if (pageStyle == PageStyle.Tabler)
            {
                sb.AppendLine("<div class=\"row row-deck row-cards mb-4\">");
                for (var i = 0; i < insights.Count; i++)
                {
                    var insight = insights[i];
                    sb.AppendLine("    <div class=\"col-md-6\">");
                    sb.AppendLine("        <div class=\"card\">");
                    sb.AppendLine("            <div class=\"card-body\">");
                    sb.AppendLine($"                <h3 class=\"card-title\">{insight.Title}</h3>");
                    sb.AppendLine($"                <div id=\"chart-{i}\" style=\"height: 300px;\"></div>");
                    sb.AppendLine("            </div>");
                    sb.AppendLine("        </div>");
                    sb.AppendLine("    </div>");
                }
                sb.AppendLine("</div>");
            }
            else // Plain
            {
                sb.AppendLine("<div class=\"chart-grid mb-4\">");
                for (var i = 0; i < insights.Count; i++)
                {
                    var insight = insights[i];
                    sb.AppendLine("    <div class=\"dashboard-card\" style=\"cursor: default;\">");
                    sb.AppendLine($"        <h4>{insight.Title}</h4>");
                    sb.AppendLine($"        <div id=\"chart-{i}\" style=\"height: 260px;\"></div>");
                    sb.AppendLine("    </div>");
                }
                sb.AppendLine("</div>");
            }
            sb.AppendLine();
        }

        // ---- "manage data" links ----
        if (navTables.Count > 0)
        {
            switch (pageStyle)
            {
                case PageStyle.MudBlazor:
                    sb.AppendLine("<MudText Typo=\"Typo.h6\" Class=\"mb-2\">Manage data</MudText>");
                    sb.AppendLine("<MudGrid>");
                    foreach (var table in navTables)
                    {
                        var plural = NamingHelper.Pluralize(table.ClassName);
                        sb.AppendLine("    <MudItem xs=\"12\" sm=\"6\" md=\"4\">");
                        sb.AppendLine("        <MudCard>");
                        sb.AppendLine("            <MudCardContent>");
                        sb.AppendLine($"                <MudText Typo=\"Typo.h6\">{plural}</MudText>");
                        sb.AppendLine("            </MudCardContent>");
                        sb.AppendLine("            <MudCardActions>");
                        sb.AppendLine($"                <MudButton Variant=\"Variant.Text\" Color=\"Color.Primary\" Href=\"/{plural.ToLowerInvariant()}\">View {plural}</MudButton>");
                        sb.AppendLine("            </MudCardActions>");
                        sb.AppendLine("        </MudCard>");
                        sb.AppendLine("    </MudItem>");
                    }
                    sb.AppendLine("</MudGrid>");
                    break;
                case PageStyle.Tabler:
                    sb.AppendLine("<h3 class=\"mb-3\">Manage data</h3>");
                    sb.AppendLine("<div class=\"row row-cards\">");
                    foreach (var table in navTables)
                    {
                        var plural = NamingHelper.Pluralize(table.ClassName);
                        sb.AppendLine("    <div class=\"col-sm-6 col-lg-4\">");
                        sb.AppendLine($"        <a href=\"/{plural.ToLowerInvariant()}\" class=\"card card-link\">");
                        sb.AppendLine("            <div class=\"card-body\">");
                        sb.AppendLine($"                <div class=\"card-title\">{plural}</div>");
                        sb.AppendLine("                <div class=\"text-secondary\">View &amp; manage &rarr;</div>");
                        sb.AppendLine("            </div>");
                        sb.AppendLine("        </a>");
                        sb.AppendLine("    </div>");
                    }
                    sb.AppendLine("</div>");
                    break;
                default:
                    sb.AppendLine("<h4>Manage data</h4>");
                    sb.AppendLine("<div class=\"dashboard-grid\">");
                    foreach (var table in navTables)
                    {
                        var plural = NamingHelper.Pluralize(table.ClassName);
                        sb.AppendLine($"    <a class=\"dashboard-card\" href=\"/{plural.ToLowerInvariant()}\">");
                        sb.AppendLine($"        <h4>{plural}</h4>");
                        sb.AppendLine("        <span>View &amp; manage &rarr;</span>");
                        sb.AppendLine("    </a>");
                    }
                    sb.AppendLine("</div>");
                    break;
            }
        }
        else if (!hasStats && !hasCharts)
        {
            sb.AppendLine(pageStyle == PageStyle.MudBlazor
                ? "<MudText>No table pages were generated yet.</MudText>"
                : "<p>No table pages were generated yet.</p>");
        }

        sb.AppendLine();
        sb.AppendLine("@code {");

        foreach (var table in statTables)
        {
            var plural = NamingHelper.Pluralize(table.ClassName);
            sb.AppendLine($"    private int _{Constant.LowerFirst(plural)}Count;");
        }

        for (var i = 0; i < insights.Count; i++)
        {
            var insight = insights[i];
            sb.AppendLine($"    private List<ChartDataPoint> _points{i} = [];");
            if (pageStyle == PageStyle.MudBlazor)
            {
                if (insight.ChartType == InsightChartType.Pie)
                {
                    sb.AppendLine($"    private double[] _data{i} = [];");
                    sb.AppendLine($"    private string[] _labels{i} = [];");
                }
                else
                {
                    sb.AppendLine($"    private List<ChartSeries> _series{i} = [];");
                    sb.AppendLine($"    private string[] _labels{i} = [];");
                }
            }
        }

        if (hasStats || hasCharts)
        {
            sb.AppendLine();
            sb.AppendLine("    protected override async Task OnInitializedAsync()");
            sb.AppendLine("    {");
            foreach (var table in statTables)
            {
                var plural = NamingHelper.Pluralize(table.ClassName);
                sb.AppendLine($"        _{Constant.LowerFirst(plural)}Count = await Stats.Get{plural}CountAsync();");
            }
            for (var i = 0; i < insights.Count; i++)
            {
                var insight = insights[i];
                var methodName = NamingHelper.ToPascalCase(insight.Title);
                sb.AppendLine($"        _points{i} = await Stats.Get{methodName}Async();");
                if (pageStyle == PageStyle.MudBlazor)
                {
                    if (insight.ChartType == InsightChartType.Pie)
                    {
                        sb.AppendLine($"        _data{i} = _points{i}.Select(p => p.Value).ToArray();");
                        sb.AppendLine($"        _labels{i} = _points{i}.Select(p => p.Label).ToArray();");
                    }
                    else
                    {
                        sb.AppendLine($"        _series{i} = [new ChartSeries {{ Name = \"{Constant.EscapeXmlDoc(insight.Title)}\", Data = _points{i}.Select(p => p.Value).ToArray() }}];");
                        sb.AppendLine($"        _labels{i} = _points{i}.Select(p => p.Label).ToArray();");
                    }
                }
            }
            sb.AppendLine("    }");
        }

        if (hasCharts && pageStyle != PageStyle.MudBlazor)
        {
            sb.AppendLine();
            sb.AppendLine("    protected override async Task OnAfterRenderAsync(bool firstRender)");
            sb.AppendLine("    {");
            sb.AppendLine("        if (!firstRender) return;");
            sb.AppendLine();
            for (var i = 0; i < insights.Count; i++)
            {
                var insight = insights[i];
                var jsFn = insight.ChartType switch
                {
                    InsightChartType.Pie => "charts.renderPie",
                    InsightChartType.Line => "charts.renderLine",
                    _ => "charts.renderBar"
                };
                sb.AppendLine($"        await JS.InvokeVoidAsync(\"{jsFn}\", \"chart-{i}\",");
                sb.AppendLine($"            _points{i}.Select(p => p.Label).ToArray(), _points{i}.Select(p => p.Value).ToArray());");
            }
            sb.AppendLine("    }");
        }

        sb.AppendLine("}");

        return new GeneratedFile { RelativePath = "Components/Pages/Dashboard.razor", Content = sb.ToString() };
    }

    private static GeneratedFile GenerateDashboardStatsService(string rootNamespace, List<TableInfo> statTables, List<DashboardInsightCandidate> insights)
    {
        var sb = new StringBuilder();
        sb.Append(Constant.GeneratedHeader);
        sb.AppendLine("using System.Data;");
        sb.AppendLine("using Dapper;");
        sb.AppendLine();
        sb.AppendLine($"namespace {rootNamespace}.Services;");
        sb.AppendLine();
        sb.AppendLine("/// <summary>");
        sb.AppendLine("/// Dashboard summary data: row counts per table, plus one method per AI-accepted chart insight.");
        sb.AppendLine("/// Every insight query here was validated (shape-checked and test-executed) as a read-only");
        sb.AppendLine("/// SELECT before being baked in - see the AI dashboard insights step in SQLAZOR.");
        sb.AppendLine("/// </summary>");
        sb.AppendLine("public class DashboardStatsService");
        sb.AppendLine("{");
        sb.AppendLine("    private readonly IDbConnection _db;");
        sb.AppendLine();
        sb.AppendLine("    public DashboardStatsService(IDbConnection db)");
        sb.AppendLine("    {");
        sb.AppendLine("        _db = db;");
        sb.AppendLine("    }");

        foreach (var table in statTables)
        {
            var tableRef = Constant.QualifiedTableName(table);
            var plural = NamingHelper.Pluralize(table.ClassName);
            sb.AppendLine();
            sb.AppendLine($"    public Task<int> Get{plural}CountAsync() =>");
            sb.AppendLine($"        _db.ExecuteScalarAsync<int>(\"SELECT COUNT(*) FROM {tableRef}\");");
        }

        foreach (var insight in insights)
        {
            var methodName = NamingHelper.ToPascalCase(insight.Title);
            var verbatimSql = insight.Sql.Replace("\"", "\"\"");

            sb.AppendLine();
            sb.AppendLine($"    /// <summary>{Constant.EscapeXmlDoc(insight.Description ?? insight.Title)}</summary>");
            sb.AppendLine($"    public async Task<List<ChartDataPoint>> Get{methodName}Async()");
            sb.AppendLine("    {");
            sb.AppendLine("        const string sql = @\"" + verbatimSql + "\";");
            sb.AppendLine("        var rows = await _db.QueryAsync(sql);");
            sb.AppendLine();
            sb.AppendLine("        return rows.Select(row =>");
            sb.AppendLine("        {");
            sb.AppendLine("            var values = ((IDictionary<string, object>)row).Values.ToList();");
            sb.AppendLine("            return new ChartDataPoint");
            sb.AppendLine("            {");
            sb.AppendLine("                Label = values.Count > 0 ? values[0]?.ToString() ?? \"\" : \"\",");
            sb.AppendLine("                Value = values.Count > 1 && values[1] is not null ? Convert.ToDouble(values[1]) : 0");
            sb.AppendLine("            };");
            sb.AppendLine("        }).ToList();");
            sb.AppendLine("    }");
        }

        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("/// <summary>One (label, value) point for a dashboard chart.</summary>");
        sb.AppendLine("public sealed class ChartDataPoint");
        sb.AppendLine("{");
        sb.AppendLine("    public string Label { get; set; } = string.Empty;");
        sb.AppendLine("    public double Value { get; set; }");
        sb.AppendLine("}");

        return new GeneratedFile { RelativePath = "Services/DashboardStatsService.cs", Content = sb.ToString() };
    }



    private static GeneratedFile GenerateChartsJs()
    {
        const string content = """
// Minimal ApexCharts wrapper called via IJSRuntime from Dashboard.razor. Used for the Plain and
// Tabler page styles only - MudBlazor mode renders charts with its own native <MudChart /> instead.
window.charts = {
    renderBar: function (elementId, labels, values) {
        renderApexChart(elementId, 'bar', labels, values);
    },
    renderPie: function (elementId, labels, values) {
        renderApexChart(elementId, 'pie', labels, values);
    },
    renderLine: function (elementId, labels, values) {
        renderApexChart(elementId, 'line', labels, values);
    }
};

function renderApexChart(elementId, type, labels, values) {
    var el = document.querySelector('#' + elementId);
    if (!el || typeof ApexCharts === 'undefined') return;

    var isPie = type === 'pie';
    var options = {
        chart: { type: type, height: '100%', toolbar: { show: false } },
        series: isPie ? values : [{ name: 'Value', data: values }],
        labels: isPie ? labels : undefined,
        xaxis: isPie ? undefined : { categories: labels }
    };

    new ApexCharts(el, options).render();
}
""";

        return new GeneratedFile { RelativePath = "wwwroot/js/charts.js", Content = content };
    }

    private static GeneratedFile GenerateAdminCss()
    {
        const string content = """
:root {
  --sidebar-bg: #111827;
  --sidebar-text: #9ca3af;
  --sidebar-text-active: #ffffff;
  --sidebar-accent: #3b82f6;
  --content-bg: #f3f4f6;
  --card-bg: #ffffff;
  --border: #e5e7eb;
  --text: #111827;
  font-family: "Segoe UI", -apple-system, BlinkMacSystemFont, sans-serif;
}

* { box-sizing: border-box; }

body {
  margin: 0;
  background: var(--content-bg);
  color: var(--text);
}

.admin-shell {
  display: flex;
  min-height: 100vh;
}

.admin-sidebar {
  width: 240px;
  flex-shrink: 0;
  background: var(--sidebar-bg);
  color: var(--sidebar-text);
  display: flex;
  flex-direction: column;
  padding: 20px 0;
}

.admin-brand {
  font-size: 18px;
  font-weight: 600;
  color: var(--sidebar-text-active);
  padding: 0 20px 20px;
  border-bottom: 1px solid rgba(255,255,255,0.08);
  margin-bottom: 12px;
}

.admin-nav {
  display: flex;
  flex-direction: column;
  gap: 2px;
  padding: 0 12px;
}

.admin-nav-link {
  display: block;
  padding: 10px 14px;
  border-radius: 6px;
  color: var(--sidebar-text);
  text-decoration: none;
  font-size: 14px;
}

.admin-nav-link:hover {
  background: rgba(255,255,255,0.06);
  color: var(--sidebar-text-active);
}

.admin-nav-link.active {
  background: var(--sidebar-accent);
  color: white;
}

.admin-content-area {
  flex: 1;
  display: flex;
  flex-direction: column;
  min-width: 0;
}

.admin-main {
  padding: 28px 32px;
  flex: 1;
}

.admin-main h3 {
  margin-top: 0;
}

.dashboard-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(220px, 1fr));
  gap: 16px;
  margin-top: 20px;
}

.chart-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(360px, 1fr));
  gap: 16px;
}

.dashboard-card {
  background: var(--card-bg);
  border: 1px solid var(--border);
  border-radius: 10px;
  padding: 18px 20px;
  text-decoration: none;
  color: var(--text);
  display: flex;
  flex-direction: column;
  gap: 6px;
  transition: box-shadow 0.15s, transform 0.15s;
}

a.dashboard-card:hover {
  box-shadow: 0 4px 14px rgba(0,0,0,0.08);
  transform: translateY(-2px);
}

.dashboard-card h4 {
  margin: 0;
  font-size: 16px;
}

.dashboard-card span {
  font-size: 13px;
  color: #6b7280;
}

.dashboard-card .stat-number {
  font-size: 26px;
  font-weight: 700;
  color: var(--text);
}

table.table {
  width: 100%;
  border-collapse: collapse;
  background: var(--card-bg);
  border: 1px solid var(--border);
  border-radius: 8px;
  overflow: hidden;
}

table.table th,
table.table td {
  padding: 10px 14px;
  text-align: left;
  border-bottom: 1px solid var(--border);
  font-size: 14px;
}

table.table th {
  background: #f9fafb;
  font-weight: 600;
  font-size: 12.5px;
  text-transform: uppercase;
  letter-spacing: 0.4px;
  color: #6b7280;
}

table.table-striped tbody tr:nth-child(even) {
  background: #fafafa;
}

.btn {
  display: inline-block;
  padding: 7px 14px;
  border-radius: 6px;
  font-size: 13.5px;
  text-decoration: none;
  border: 1px solid transparent;
  cursor: pointer;
}

.btn-primary { background: var(--sidebar-accent); color: white; }
.btn-primary:hover { background: #2563eb; }
.btn-secondary { background: #e5e7eb; color: var(--text); }
.btn-secondary:hover { background: #d1d5db; }
.btn-danger { background: #ef4444; color: white; }
.btn-danger:hover { background: #dc2626; }
.btn-sm { padding: 4px 10px; font-size: 12.5px; }

.mb-3 { margin-bottom: 14px; }
.mb-4 { margin-bottom: 18px; }

.form-control {
  width: 100%;
  padding: 8px 12px;
  border: 1px solid var(--border);
  border-radius: 6px;
  font-size: 14px;
}

.form-label {
  display: block;
  font-size: 13px;
  font-weight: 500;
  margin-bottom: 4px;
  color: #374151;
}

.form-check {
  display: flex;
  align-items: center;
  gap: 8px;
}

.alert {
  padding: 12px 16px;
  border-radius: 8px;
  font-size: 14px;
  margin-bottom: 16px;
}

.alert-danger {
  background: #fee2e2;
  color: #991b1b;
  border: 1px solid #fecaca;
}
""";

        return new GeneratedFile { RelativePath = "wwwroot/css/admin.css", Content = content };
    }
}
