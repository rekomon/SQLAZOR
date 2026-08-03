using SQLAZOR.Models;

namespace SQLAZOR.Services
{
    public interface ISchemaCodeGeneratorService
    {
        /// <summary>
        /// Generates one POCO file and one IEntityTypeConfiguration file per selected table,
        /// plus a single DbContext file, based on the given namespace.
        /// </summary>
        List<GeneratedFile> Generate(
            DatabaseSchema schema,
        IEnumerable<string> selectedTableKeys,
        string rootNamespace,
        string dbContextName,
        ProjectLayers? layers = null);
    }
}
