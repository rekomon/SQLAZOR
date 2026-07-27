using SQLAZOR.Models;

namespace SQLAZOR.Services
{
    public interface ICrudGeneratorService
    {
        /// <summary>
        /// Generates, per selected table: a read DTO, a create DTO, an update DTO, a service
        /// interface, and a Dapper + Mapster-backed service implementation with hand-written SQL for
        /// each CRUD method. Every service method returns <c>ResponseResult&lt;T&gt;</c>. Optionally
        /// also generates an ASP.NET Core controller exposing the service over HTTP, and — only when
        /// endpoints are also requested — an HttpClient-based implementation of the same service
        /// interface that calls those endpoints, for consumers that talk to the API remotely.
        /// Optionally also generates Blazor Server UI pages under one folder per table
        /// (<c>Components/Pages/{Table}/{Table}List.razor</c>, <c>...Create.razor</c>,
        /// <c>...Edit.razor</c>) that consume <c>I{Table}Service</c> directly — since both the Dapper
        /// and HttpClient implementations share that interface, these pages work unmodified against
        /// whichever one is registered. <paramref name="pageStyle"/> controls the markup family used:
        /// plain HTML, MudBlazor components, or Tabler (Bootstrap-based) markup.
        /// </summary>
        List<GeneratedFile> GenerateCrudServices(
            DatabaseSchema schema,
            IEnumerable<string> selectedTableKeys,
            string rootNamespace,
            bool generateApiEndpoints,
            bool generateHttpClientServices,
            bool generateBlazorPages,
            PageStyle pageStyle);
    }
}
