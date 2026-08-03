using SQLAZOR.Models;

namespace SQLAZOR.Services
{
    public interface IProjectScaffoldGeneratorService
    {
        /// <summary>
        /// Generates the files that turn the rest of the output into an actual runnable project. In
        /// the default single-project layout: <c>{applicationName}.csproj</c>, <c>Program.cs</c>,
        /// <c>appsettings.json</c>, the Blazor Components shell, and the admin dashboard
        /// (<c>MainLayout.razor</c> + <c>NavMenu.razor</c> + a landing dashboard page with row-count
        /// stat cards and, when <paramref name="acceptedInsights"/> is non-empty, one chart per
        /// accepted AI-suggested insight) in plain CSS, MudBlazor, or Tabler markup depending on
        /// <paramref name="pageStyle"/>. When <paramref name="useCleanArchitecture"/> is set, instead
        /// generates a 4-project solution (<c>{App}.Domain</c> / <c>.Application</c> /
        /// <c>.Infrastructure</c> / <c>.Web</c>, each with its own <c>.csproj</c>, tied together by an
        /// <c>{App}.sln</c>) with an <c>Infrastructure/DependencyInjection.cs</c> extension
        /// (<c>AddInfrastructure(...)</c>) doing all persistence wiring, called from a much smaller
        /// <c>Program.cs</c> — every other generated file (from this method and from
        /// <see cref="Generate"/>/<see cref="GenerateForProcedures"/>/<see cref="GenerateCrudServices"/>)
        /// should be routed to the correct project by passing the same <see cref="ProjectLayers"/>
        /// this method computes internally.
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
            List<DashboardInsightCandidate> acceptedInsights,
            bool useCleanArchitecture = false);
    }
}
