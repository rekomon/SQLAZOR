using SQLAZOR.Models;

namespace SQLAZOR.Services
{
    public interface IGenerateDocumentationService
    {
        List<GeneratedFile> GenerateDocumentation(
        DatabaseSchema schema,
        IEnumerable<string> selectedTableKeys,
        List<StoredProcedureDetail> proceduresIncluded,
        List<GeneratedFile> allGeneratedFiles,
        string applicationName,
        string rootNamespace,
        string dbContextName,
        bool includeCrudServices,
        bool includeApiEndpoints,
        bool includeHttpClientServices,
        bool includeBlazorPages,
        PageStyle pageStyle,
        bool useCleanArchitecture);
    }
}
