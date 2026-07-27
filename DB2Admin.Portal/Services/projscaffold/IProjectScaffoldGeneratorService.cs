using SQLAZOR.Models;

namespace SQLAZOR.Services
{
    public interface IProjectScaffoldGeneratorService
    {
        /// <summary>
        /// Generates the files that turn the rest of the output into an actual runnable project:
        /// <c>{applicationName}.csproj</c> (with exactly the package references the other selected
        /// options need), <c>Program.cs</c> (DbContext/Dapper/MudBlazor/Controllers wiring, only for
        /// what was actually generated), <c>appsettings.json</c> (with the connection string used to
        /// read the schema), <c>Components/_Imports.razor</c>, <c>Components/App.razor</c>,
        /// <c>Components/Routes.razor</c>, and a modern admin-dashboard shell
        /// (<c>MainLayout.razor</c> + <c>NavMenu.razor</c> with a link per generated table, plus a
        /// landing dashboard page with row-count stat cards and, when
        /// <paramref name="acceptedInsights"/> is non-empty, one chart per accepted AI-suggested
        /// insight) in plain CSS, MudBlazor, or Tabler markup depending on <paramref name="pageStyle"/>.
        /// </summary>
        List<GeneratedFile> GenerateProjectScaffold(
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
            List<DashboardInsightCandidate> acceptedInsights);
    }
}
