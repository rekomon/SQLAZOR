using SQLAZOR.Models;

namespace SQLAZOR.Services
{
    public interface IStoredProcedureGeneratorService
    {
        /// <summary>
        /// Generates a result-set POCO plus a hand-mapped executor class for each procedure.
        /// Procedures where the result set couldn't be described still get a parameters-only
        /// executor that returns rows as a generic reader callback.
        /// </summary>
        List<GeneratedFile> GenerateForProcedures(
            IEnumerable<StoredProcedureDetail> procedures,
            string rootNamespace);
    }
}
