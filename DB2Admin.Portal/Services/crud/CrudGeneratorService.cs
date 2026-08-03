using SQLAZOR.Models;
using System.Reflection.PortableExecutable;
using System.Text;

namespace SQLAZOR.Services
{
    public sealed class CrudGeneratorService : ICrudGeneratorService    
    {

        #region "Generate CRUD services"
        public List<GeneratedFile> GenerateCrudServices(
            DatabaseSchema schema,
            IEnumerable<string> selectedTableKeys,
            string rootNamespace,
            bool generateApiEndpoints,
            bool generateHttpClientServices,
            bool generateBlazorPages,
            PageStyle pageStyle, ProjectLayers? layers = null)
        {
            var selected = selectedTableKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var tables = schema.Tables.Where(t => selected.Contains(t.FullyQualifiedName) && !t.IsView).ToList();
            var validTables = tables.Where(t => t.PrimaryKeyColumns.Count > 0).ToList();

            var files = new List<GeneratedFile>();
            if (validTables.Count == 0)
                return files;

            // HttpClient services only make sense if there are endpoints for them to call.
            var actuallyGenerateHttpServices = generateApiEndpoints && generateHttpClientServices;

            var domainNs = layers?.DomainNamespace ?? rootNamespace;
            var applicationNs = layers?.ApplicationNamespace ?? rootNamespace;
            var infrastructureNs = layers?.InfrastructureNamespace ?? rootNamespace;
            var webNs = layers?.WebNamespace ?? rootNamespace;
            var domainPath = layers?.DomainPath ?? "";
            var applicationPath = layers?.ApplicationPath ?? "";
            var infrastructurePath = layers?.InfrastructurePath ?? "";
            var webPath = layers?.WebPath ?? "";
            var isClean = layers?.IsCleanArchitecture ?? false;

            files.Add(Constant.WithPathPrefix(GenerateResponseResultClass(applicationNs), applicationPath));

            foreach (var table in validTables)
            {
                files.Add(Constant.WithPathPrefix(GenerateReadDto(table, applicationNs), applicationPath));
                files.Add(Constant.WithPathPrefix(GenerateCreateDto(table, applicationNs), applicationPath));
                files.Add(Constant.WithPathPrefix(GenerateUpdateDto(table, applicationNs), applicationPath));
                files.Add(Constant.WithPathPrefix(GenerateServiceInterface(table, applicationNs), applicationPath));
                files.Add(Constant.WithPathPrefix(GenerateServiceImplementationDapper(table, infrastructureNs, applicationNs, domainNs), infrastructurePath));

                if (generateApiEndpoints)
                {
                    files.Add(Constant.WithPathPrefix(GenerateController(table, webNs, applicationNs), webPath));
                }

                if (actuallyGenerateHttpServices)
                {
                    files.Add(Constant.WithPathPrefix(GenerateHttpService(table, webNs, applicationNs), webPath));
                }

                if (generateBlazorPages)
                {
                    files.Add(Constant.WithPathPrefix(GenerateListPage(table, webNs, pageStyle), webPath));
                    files.Add(Constant.WithPathPrefix(GenerateCreatePage(table, webNs, pageStyle), webPath));
                    files.Add(Constant.WithPathPrefix(GenerateEditPage(table, webNs, pageStyle), webPath));
                }
            }

            if (actuallyGenerateHttpServices)
            {
                files.Add(Constant.WithPathPrefix(GenerateApiHttpServiceBase(webNs, applicationNs), webPath));
            }

            if (isClean)
            {
                // Infrastructure must never reference Web-layer HttpService types - split registration
                // into two files, one per owning project, instead of the single combined file below.
                files.Add(Constant.WithPathPrefix(GenerateInfrastructureServiceRegistration(validTables, infrastructureNs, applicationNs), infrastructurePath));
                if (actuallyGenerateHttpServices)
                {
                    files.Add(Constant.WithPathPrefix(GenerateWebHttpServiceRegistration(validTables, webNs, applicationNs), webPath));
                }
            }
            else
            {
                files.Add(Constant.WithPathPrefix(GenerateServiceRegistrationExtensions(validTables, rootNamespace, actuallyGenerateHttpServices), ""));
            }

            return files;
        }

        #endregion

        private static string PageFolder(TableInfo table) => $"Components/Pages/{table.ClassName}";
        private static List<ColumnInfo> GetCreatableColumns(TableInfo table) =>
        table.Columns.Where(c => !c.IsIdentity && !c.IsComputed).OrderBy(c => c.OrdinalPosition).ToList();

        private static List<ColumnInfo> GetUpdatableColumns(TableInfo table) =>
            table.Columns.Where(c => !c.IsPrimaryKey && !c.IsComputed).OrderBy(c => c.OrdinalPosition).ToList();

        private static GeneratedFile GenerateResponseResultClass(string rootNamespace)
        {
            var sb = new StringBuilder();
            sb.Append(Constant.GeneratedHeader);
            sb.AppendLine("using System.Net;");
            sb.AppendLine();
            sb.AppendLine($"namespace {rootNamespace}.Common;");
            sb.AppendLine();
            sb.AppendLine("/// <summary>Uniform envelope every generated service method (and API endpoint) returns.</summary>");
            sb.AppendLine("public class ResponseResult<T>");
            sb.AppendLine("{");
            sb.AppendLine("    public bool IsSuccessful { get; set; } = false;");
            sb.AppendLine("    public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.Created;");
            sb.AppendLine("    public string Message { get; set; } = string.Empty;");
            sb.AppendLine("    public T Data { get; set; } = default!;");
            sb.AppendLine("    public int? TotalCount { get; set; } = 0;");
            sb.AppendLine("}");

            return new GeneratedFile { RelativePath = "Common/ResponseResult.cs", Content = sb.ToString() };
        }


        private static GeneratedFile GenerateReadDto(TableInfo table, string rootNamespace)
        {
            var sb = new StringBuilder();
            sb.Append(Constant.GeneratedHeader);
            sb.AppendLine($"namespace {rootNamespace}.Dtos;");
            sb.AppendLine();
            sb.AppendLine("/// <summary>Read/response shape for " + table.ClassName + " — every column, no navigation properties.</summary>");
            sb.AppendLine($"public class {table.ClassName}Dto");
            sb.AppendLine("{");
            foreach (var col in table.Columns.OrderBy(c => c.OrdinalPosition))
            {
                var needsInit = col.ClrType is "string" && !col.IsNullable;
                sb.AppendLine($"    public {col.ClrType} {col.PropertyName} {{ get; set; }}{(needsInit ? " = string.Empty;" : "")}");
            }
            sb.AppendLine("}");

            return new GeneratedFile { RelativePath = $"Dtos/{table.ClassName}Dto.cs", Content = sb.ToString() };
        }

        private static GeneratedFile GenerateCreateDto(TableInfo table, string rootNamespace)
        {
            var sb = new StringBuilder();
            sb.Append(Constant.GeneratedHeader);
            sb.AppendLine($"namespace {rootNamespace}.Dtos;");
            sb.AppendLine();
            sb.AppendLine("/// <summary>Fields accepted when creating a new " + table.ClassName + " (excludes identity/computed columns).</summary>");
            sb.AppendLine($"public class {table.ClassName}CreateDto");
            sb.AppendLine("{");
            foreach (var col in GetCreatableColumns(table))
            {
                var needsInit = col.ClrType is "string" && !col.IsNullable;
                sb.AppendLine($"    public {col.ClrType} {col.PropertyName} {{ get; set; }}{(needsInit ? " = string.Empty;" : "")}");
            }
            sb.AppendLine("}");

            return new GeneratedFile { RelativePath = $"Dtos/{table.ClassName}CreateDto.cs", Content = sb.ToString() };
        }

        private static GeneratedFile GenerateUpdateDto(TableInfo table, string rootNamespace)
        {
            var sb = new StringBuilder();
            sb.Append(Constant.GeneratedHeader);
            sb.AppendLine($"namespace {rootNamespace}.Dtos;");
            sb.AppendLine();
            sb.AppendLine("/// <summary>Fields accepted when updating an existing " + table.ClassName +
                           " (the key is passed separately to the service method, not in this DTO).</summary>");
            sb.AppendLine($"public class {table.ClassName}UpdateDto");
            sb.AppendLine("{");
            foreach (var col in GetUpdatableColumns(table))
            {
                var needsInit = col.ClrType is "string" && !col.IsNullable;
                sb.AppendLine($"    public {col.ClrType} {col.PropertyName} {{ get; set; }}{(needsInit ? " = string.Empty;" : "")}");
            }
            sb.AppendLine("}");

            return new GeneratedFile { RelativePath = $"Dtos/{table.ClassName}UpdateDto.cs", Content = sb.ToString() };
        }



        private static GeneratedFile GenerateServiceInterface(TableInfo table, string rootNamespace)
        {
            var pkParams = BuildPkMethodParams(table);

            var sb = new StringBuilder();
            sb.Append(Constant.GeneratedHeader);
            sb.AppendLine($"using {rootNamespace}.Common;");
            sb.AppendLine($"using {rootNamespace}.Dtos;");
            sb.AppendLine();
            sb.AppendLine($"namespace {rootNamespace}.Services;");
            sb.AppendLine();
            sb.AppendLine($"public interface I{table.ClassName}Service");
            sb.AppendLine("{");
            sb.AppendLine($"    Task<ResponseResult<List<{table.ClassName}Dto>>> GetAllAsync(CancellationToken cancellationToken = default);");
            sb.AppendLine();
            sb.AppendLine($"    Task<ResponseResult<{table.ClassName}Dto>> GetByIdAsync({pkParams}, CancellationToken cancellationToken = default);");
            sb.AppendLine();
            sb.AppendLine($"    Task<ResponseResult<{table.ClassName}Dto>> CreateAsync({table.ClassName}CreateDto dto, CancellationToken cancellationToken = default);");
            sb.AppendLine();
            sb.AppendLine($"    Task<ResponseResult<bool>> UpdateAsync({pkParams}, {table.ClassName}UpdateDto dto, CancellationToken cancellationToken = default);");
            sb.AppendLine();
            sb.AppendLine($"    Task<ResponseResult<bool>> DeleteAsync({pkParams}, CancellationToken cancellationToken = default);");
            sb.AppendLine("}");

            return new GeneratedFile { RelativePath = $"Services/I{table.ClassName}Service.cs", Content = sb.ToString() };
        }

        private static GeneratedFile GenerateServiceImplementationDapper(TableInfo table, string rootNamespace, string? applicationNamespace = null, string? domainNamespace = null)
        {
            applicationNamespace ??= rootNamespace;
            domainNamespace ??= rootNamespace;


            var pkParams = BuildPkMethodParams(table);
            var pkArgs = Constant.GetPkArgs(table);
            var whereSql = BuildPkWhereSql(table);
            var pkParamsObj = BuildPkParamsObject(table);
            var creatableCols = GetCreatableColumns(table);
            var updatableCols = GetUpdatableColumns(table);
            var tableRef = Constant.QualifiedTableName(table);

            var insertColumns = string.Join(", ", creatableCols.Select(c => $"[{c.ColumnName}]"));
            var insertValues = string.Join(", ", creatableCols.Select(c => $"@{c.PropertyName}"));
            var setClauses = string.Join(", ", updatableCols.Select(c => $"[{c.ColumnName}] = @{c.PropertyName}"));

            var selectColumnList = Constant.BuildSelectColumnList(table);
            var outputColumnList = Constant.BuildSelectColumnList(table, "INSERTED");

            var sb = new StringBuilder();
            sb.Append(Constant.GeneratedHeader);
            sb.AppendLine("using System.Data;");
            sb.AppendLine("using System.Net;");
            sb.AppendLine("using Dapper;");
            sb.AppendLine("using Mapster;");
            sb.AppendLine($"using {applicationNamespace}.Common;");
            sb.AppendLine($"using {applicationNamespace}.Dtos;");
            sb.AppendLine($"using {applicationNamespace}.Services;");
            sb.AppendLine($"using {domainNamespace}.Entities;");
            sb.AppendLine();
            sb.AppendLine($"namespace {rootNamespace}.Services;");
            sb.AppendLine();
            sb.AppendLine("/// <summary>");
            sb.AppendLine($"/// Dapper + Mapster-backed CRUD service for {table.ClassName}. SQL is hand-written and explicit —");
            sb.AppendLine("/// no LINQ-to-SQL translation to reason about. Entity&lt;-&gt;DTO mapping goes through Mapster's");
            sb.AppendLine("/// convention-based Adapt&lt;T&gt;(), which works here because DTO property names mirror the");
            sb.AppendLine("/// entity's (add a TypeAdapterConfig if you introduce DTO fields that don't line up 1:1).");
            sb.AppendLine("/// </summary>");
            sb.AppendLine($"public class {table.ClassName}Service : I{table.ClassName}Service");
            sb.AppendLine("{");
            sb.AppendLine("    private readonly IDbConnection _db;");
            sb.AppendLine();
            sb.AppendLine($"    public {table.ClassName}Service(IDbConnection db)");
            sb.AppendLine("    {");
            sb.AppendLine("        _db = db;");
            sb.AppendLine("    }");
            sb.AppendLine();

            // GetAllAsync
            sb.AppendLine($"    public async Task<ResponseResult<List<{table.ClassName}Dto>>> GetAllAsync(CancellationToken cancellationToken = default)");
            sb.AppendLine("    {");
            sb.AppendLine("        try");
            sb.AppendLine("        {");
            sb.AppendLine($"            const string sql = \"SELECT {selectColumnList} FROM {tableRef}\";");
            sb.AppendLine($"            var entities = (await _db.QueryAsync<{table.ClassName}>(");
            sb.AppendLine("                new CommandDefinition(sql, cancellationToken: cancellationToken))).ToList();");
            sb.AppendLine($"            var dtos = entities.Adapt<List<{table.ClassName}Dto>>();");
            sb.AppendLine();
            sb.AppendLine($"            return new ResponseResult<List<{table.ClassName}Dto>>");
            sb.AppendLine("            {");
            sb.AppendLine("                IsSuccessful = true,");
            sb.AppendLine("                StatusCode = HttpStatusCode.OK,");
            sb.AppendLine("                Data = dtos,");
            sb.AppendLine("                TotalCount = dtos.Count");
            sb.AppendLine("            };");
            sb.AppendLine("        }");
            sb.AppendLine("        catch (Exception ex)");
            sb.AppendLine("        {");
            sb.AppendLine($"            return new ResponseResult<List<{table.ClassName}Dto>>");
            sb.AppendLine("            {");
            sb.AppendLine("                IsSuccessful = false,");
            sb.AppendLine("                StatusCode = HttpStatusCode.InternalServerError,");
            sb.AppendLine("                Message = ex.Message,");
            sb.AppendLine($"                Data = new List<{table.ClassName}Dto>()");
            sb.AppendLine("            };");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine();

            // GetByIdAsync
            sb.AppendLine($"    public async Task<ResponseResult<{table.ClassName}Dto>> GetByIdAsync({pkParams}, CancellationToken cancellationToken = default)");
            sb.AppendLine("    {");
            sb.AppendLine("        try");
            sb.AppendLine("        {");
            sb.AppendLine($"            const string sql = \"SELECT {selectColumnList} FROM {tableRef} WHERE {whereSql}\";");
            sb.AppendLine($"            var entity = await _db.QuerySingleOrDefaultAsync<{table.ClassName}>(");
            sb.AppendLine($"                new CommandDefinition(sql, {pkParamsObj}, cancellationToken: cancellationToken));");
            sb.AppendLine();
            sb.AppendLine("            if (entity is null)");
            sb.AppendLine("            {");
            sb.AppendLine($"                return new ResponseResult<{table.ClassName}Dto>");
            sb.AppendLine("                {");
            sb.AppendLine("                    IsSuccessful = false,");
            sb.AppendLine("                    StatusCode = HttpStatusCode.NotFound,");
            sb.AppendLine($"                    Message = \"{table.ClassName} not found.\"");
            sb.AppendLine("                };");
            sb.AppendLine("            }");
            sb.AppendLine();
            sb.AppendLine($"            return new ResponseResult<{table.ClassName}Dto>");
            sb.AppendLine("            {");
            sb.AppendLine("                IsSuccessful = true,");
            sb.AppendLine("                StatusCode = HttpStatusCode.OK,");
            sb.AppendLine($"                Data = entity.Adapt<{table.ClassName}Dto>()");
            sb.AppendLine("            };");
            sb.AppendLine("        }");
            sb.AppendLine("        catch (Exception ex)");
            sb.AppendLine("        {");
            sb.AppendLine($"            return new ResponseResult<{table.ClassName}Dto>");
            sb.AppendLine("            {");
            sb.AppendLine("                IsSuccessful = false,");
            sb.AppendLine("                StatusCode = HttpStatusCode.InternalServerError,");
            sb.AppendLine("                Message = ex.Message");
            sb.AppendLine("            };");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine();

            // CreateAsync
            sb.AppendLine($"    public async Task<ResponseResult<{table.ClassName}Dto>> CreateAsync({table.ClassName}CreateDto dto, CancellationToken cancellationToken = default)");
            sb.AppendLine("    {");
            sb.AppendLine("        try");
            sb.AppendLine("        {");
            sb.AppendLine("            // OUTPUT hands back the full new row (identity value + any server-side defaults)");
            sb.AppendLine("            // in one round trip, columns aliased to property names for Dapper's mapper -");
            sb.AppendLine("            // skip the OUTPUT clause if the table has AFTER INSERT triggers.");
            sb.AppendLine("            const string sql = @\"");
            sb.AppendLine($"                INSERT INTO {tableRef} ({insertColumns})");
            sb.AppendLine($"                OUTPUT {outputColumnList}");
            sb.AppendLine($"                VALUES ({insertValues})\";");
            sb.AppendLine();
            sb.AppendLine($"            var created = await _db.QuerySingleAsync<{table.ClassName}>(");
            sb.AppendLine("                new CommandDefinition(sql, dto, cancellationToken: cancellationToken));");
            sb.AppendLine();
            sb.AppendLine($"            return new ResponseResult<{table.ClassName}Dto>");
            sb.AppendLine("            {");
            sb.AppendLine("                IsSuccessful = true,");
            sb.AppendLine("                StatusCode = HttpStatusCode.Created,");
            sb.AppendLine($"                Data = created.Adapt<{table.ClassName}Dto>()");
            sb.AppendLine("            };");
            sb.AppendLine("        }");
            sb.AppendLine("        catch (Exception ex)");
            sb.AppendLine("        {");
            sb.AppendLine($"            return new ResponseResult<{table.ClassName}Dto>");
            sb.AppendLine("            {");
            sb.AppendLine("                IsSuccessful = false,");
            sb.AppendLine("                StatusCode = HttpStatusCode.InternalServerError,");
            sb.AppendLine("                Message = ex.Message");
            sb.AppendLine("            };");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine();

            // UpdateAsync
            sb.AppendLine($"    public async Task<ResponseResult<bool>> UpdateAsync({pkParams}, {table.ClassName}UpdateDto dto, CancellationToken cancellationToken = default)");
            sb.AppendLine("    {");
            sb.AppendLine("        try");
            sb.AppendLine("        {");
            sb.AppendLine($"            const string sql = \"UPDATE {tableRef} SET {setClauses} WHERE {whereSql}\";");
            sb.AppendLine();
            sb.AppendLine("            var parameters = new DynamicParameters(dto);");
            foreach (var arg in pkArgs)
            {
                sb.AppendLine($"            parameters.Add(\"{arg.ArgName}\", {arg.ArgName});");
            }
            sb.AppendLine();
            sb.AppendLine("            var rowsAffected = await _db.ExecuteAsync(");
            sb.AppendLine("                new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));");
            sb.AppendLine();
            sb.AppendLine("            if (rowsAffected == 0)");
            sb.AppendLine("            {");
            sb.AppendLine("                return new ResponseResult<bool>");
            sb.AppendLine("                {");
            sb.AppendLine("                    IsSuccessful = false,");
            sb.AppendLine("                    StatusCode = HttpStatusCode.NotFound,");
            sb.AppendLine($"                    Message = \"{table.ClassName} not found.\",");
            sb.AppendLine("                    Data = false");
            sb.AppendLine("                };");
            sb.AppendLine("            }");
            sb.AppendLine();
            sb.AppendLine("            return new ResponseResult<bool> { IsSuccessful = true, StatusCode = HttpStatusCode.OK, Data = true };");
            sb.AppendLine("        }");
            sb.AppendLine("        catch (Exception ex)");
            sb.AppendLine("        {");
            sb.AppendLine("            return new ResponseResult<bool>");
            sb.AppendLine("            {");
            sb.AppendLine("                IsSuccessful = false,");
            sb.AppendLine("                StatusCode = HttpStatusCode.InternalServerError,");
            sb.AppendLine("                Message = ex.Message,");
            sb.AppendLine("                Data = false");
            sb.AppendLine("            };");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine();

            // DeleteAsync
            sb.AppendLine($"    public async Task<ResponseResult<bool>> DeleteAsync({pkParams}, CancellationToken cancellationToken = default)");
            sb.AppendLine("    {");
            sb.AppendLine("        try");
            sb.AppendLine("        {");
            sb.AppendLine($"            const string sql = \"DELETE FROM {tableRef} WHERE {whereSql}\";");
            sb.AppendLine("            var rowsAffected = await _db.ExecuteAsync(");
            sb.AppendLine($"                new CommandDefinition(sql, {pkParamsObj}, cancellationToken: cancellationToken));");
            sb.AppendLine();
            sb.AppendLine("            if (rowsAffected == 0)");
            sb.AppendLine("            {");
            sb.AppendLine("                return new ResponseResult<bool>");
            sb.AppendLine("                {");
            sb.AppendLine("                    IsSuccessful = false,");
            sb.AppendLine("                    StatusCode = HttpStatusCode.NotFound,");
            sb.AppendLine($"                    Message = \"{table.ClassName} not found.\",");
            sb.AppendLine("                    Data = false");
            sb.AppendLine("                };");
            sb.AppendLine("            }");
            sb.AppendLine();
            sb.AppendLine("            return new ResponseResult<bool> { IsSuccessful = true, StatusCode = HttpStatusCode.OK, Data = true };");
            sb.AppendLine("        }");
            sb.AppendLine("        catch (Exception ex)");
            sb.AppendLine("        {");
            sb.AppendLine("            return new ResponseResult<bool>");
            sb.AppendLine("            {");
            sb.AppendLine("                IsSuccessful = false,");
            sb.AppendLine("                StatusCode = HttpStatusCode.InternalServerError,");
            sb.AppendLine("                Message = ex.Message,");
            sb.AppendLine("                Data = false");
            sb.AppendLine("            };");
            sb.AppendLine("        }");
            sb.AppendLine("    }");

            sb.AppendLine("}");

            return new GeneratedFile { RelativePath = $"Services/{table.ClassName}Service.cs", Content = sb.ToString() };
        }

        private static GeneratedFile GenerateController(TableInfo table, string rootNamespace, string? applicationNamespace = null)
        {
            applicationNamespace ??= rootNamespace;

            var pkParams = BuildPkMethodParams(table);
            var pkArgList = BuildPkArgList(table);
            var routeTemplate = Constant.BuildPkPathSegment(table);
            var pluralClass = NamingHelper.Pluralize(table.ClassName);
            var pluralRoute = pluralClass.ToLowerInvariant();

            var sb = new StringBuilder();
            sb.Append(Constant.GeneratedHeader);
            sb.AppendLine("using Microsoft.AspNetCore.Mvc;");
            sb.AppendLine($"using {applicationNamespace}.Dtos;");
            sb.AppendLine($"using {applicationNamespace}.Services;");
            sb.AppendLine();
            sb.AppendLine($"namespace {rootNamespace}.Controllers;");
            sb.AppendLine();
            sb.AppendLine("[ApiController]");
            sb.AppendLine($"[Route(\"api/{pluralRoute}\")]");
            sb.AppendLine($"public class {pluralClass}Controller : ControllerBase");
            sb.AppendLine("{");
            sb.AppendLine($"    private readonly I{table.ClassName}Service _service;");
            sb.AppendLine();
            sb.AppendLine($"    public {pluralClass}Controller(I{table.ClassName}Service service)");
            sb.AppendLine("    {");
            sb.AppendLine("        _service = service;");
            sb.AppendLine("    }");
            sb.AppendLine();

            sb.AppendLine("    [HttpGet]");
            sb.AppendLine("    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)");
            sb.AppendLine("    {");
            sb.AppendLine("        var result = await _service.GetAllAsync(cancellationToken);");
            sb.AppendLine("        return StatusCode((int)result.StatusCode, result);");
            sb.AppendLine("    }");
            sb.AppendLine();

            sb.AppendLine($"    [HttpGet(\"{routeTemplate}\")]");
            sb.AppendLine($"    public async Task<IActionResult> GetById({pkParams}, CancellationToken cancellationToken)");
            sb.AppendLine("    {");
            sb.AppendLine($"        var result = await _service.GetByIdAsync({pkArgList}, cancellationToken);");
            sb.AppendLine("        return StatusCode((int)result.StatusCode, result);");
            sb.AppendLine("    }");
            sb.AppendLine();

            sb.AppendLine("    [HttpPost]");
            sb.AppendLine($"    public async Task<IActionResult> Create([FromBody] {table.ClassName}CreateDto dto, CancellationToken cancellationToken)");
            sb.AppendLine("    {");
            sb.AppendLine("        var result = await _service.CreateAsync(dto, cancellationToken);");
            sb.AppendLine("        return StatusCode((int)result.StatusCode, result);");
            sb.AppendLine("    }");
            sb.AppendLine();

            sb.AppendLine($"    [HttpPut(\"{routeTemplate}\")]");
            sb.AppendLine($"    public async Task<IActionResult> Update({pkParams}, [FromBody] {table.ClassName}UpdateDto dto, CancellationToken cancellationToken)");
            sb.AppendLine("    {");
            sb.AppendLine($"        var result = await _service.UpdateAsync({pkArgList}, dto, cancellationToken);");
            sb.AppendLine("        return StatusCode((int)result.StatusCode, result);");
            sb.AppendLine("    }");
            sb.AppendLine();

            sb.AppendLine($"    [HttpDelete(\"{routeTemplate}\")]");
            sb.AppendLine($"    public async Task<IActionResult> Delete({pkParams}, CancellationToken cancellationToken)");
            sb.AppendLine("    {");
            sb.AppendLine($"        var result = await _service.DeleteAsync({pkArgList}, cancellationToken);");
            sb.AppendLine("        return StatusCode((int)result.StatusCode, result);");
            sb.AppendLine("    }");

            sb.AppendLine("}");

            return new GeneratedFile { RelativePath = $"Controllers/{pluralClass}Controller.cs", Content = sb.ToString() };
        }

        private static GeneratedFile GenerateApiHttpServiceBase(string rootNamespace, string? applicationNamespace = null)
        {
            applicationNamespace ??= rootNamespace;
            var sb = new StringBuilder();
            sb.Append(Constant.GeneratedHeader);
            sb.AppendLine("using System.Net.Http.Json;");
            sb.AppendLine($"using {applicationNamespace}.Common;");
            sb.AppendLine();
            sb.AppendLine($"namespace {rootNamespace}.Services;");
            sb.AppendLine();
            sb.AppendLine("/// <summary>");
            sb.AppendLine("/// Shared helper for the generated *HttpService classes: turns an HttpResponseMessage into the");
            sb.AppendLine("/// same ResponseResult&lt;T&gt; shape the server-side services return, so callers can swap between");
            sb.AppendLine("/// the direct (Dapper) and remote (HTTP) implementations of a service interface with no other");
            sb.AppendLine("/// code changes.");
            sb.AppendLine("/// </summary>");
            sb.AppendLine("public abstract class ApiHttpServiceBase");
            sb.AppendLine("{");
            sb.AppendLine("    protected static async Task<ResponseResult<T>> ReadResultAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)");
            sb.AppendLine("    {");
            sb.AppendLine("        try");
            sb.AppendLine("        {");
            sb.AppendLine("            var result = await response.Content.ReadFromJsonAsync<ResponseResult<T>>(cancellationToken: cancellationToken);");
            sb.AppendLine("            if (result is not null)");
            sb.AppendLine("                return result;");
            sb.AppendLine("        }");
            sb.AppendLine("        catch");
            sb.AppendLine("        {");
            sb.AppendLine("            // Malformed/empty body, non-JSON error page, etc. - fall through to the generic error below.");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        return new ResponseResult<T>");
            sb.AppendLine("        {");
            sb.AppendLine("            IsSuccessful = false,");
            sb.AppendLine("            StatusCode = response.StatusCode,");
            sb.AppendLine("            Message = $\"Unexpected response from API ({(int)response.StatusCode} {response.StatusCode}).\"");
            sb.AppendLine("        };");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            return new GeneratedFile { RelativePath = "Services/ApiHttpServiceBase.cs", Content = sb.ToString() };
        }

        private static GeneratedFile GenerateHttpService(TableInfo table, string rootNamespace, string? applicationNamespace = null)
        {
            applicationNamespace ??= rootNamespace;
            var pkParams = BuildPkMethodParams(table);
            var pathSegment = Constant.BuildPkPathSegment(table); // e.g. "{id}" or "{orderId}/{lineNumber}" - valid as a C# interpolation body too
            var pluralRoute = NamingHelper.Pluralize(table.ClassName).ToLowerInvariant();

            var sb = new StringBuilder();
            sb.Append(Constant.GeneratedHeader);
            sb.AppendLine("using System.Net.Http.Json;");
            sb.AppendLine($"using {applicationNamespace}.Common;");
            sb.AppendLine($"using {applicationNamespace}.Dtos;");
            sb.AppendLine($"using {applicationNamespace}.Services;");
            sb.AppendLine();
            sb.AppendLine($"namespace {rootNamespace}.Services;");
            sb.AppendLine();
            sb.AppendLine("/// <summary>");
            sb.AppendLine($"/// Calls the generated {NamingHelper.Pluralize(table.ClassName)}Controller endpoints over HTTP, implementing");
            sb.AppendLine($"/// the same I{table.ClassName}Service interface as the direct Dapper-backed service — register this");
            sb.AppendLine("/// one instead when this project talks to the API remotely rather than owning the DB connection.");
            sb.AppendLine("/// </summary>");
            sb.AppendLine($"public class {table.ClassName}HttpService : ApiHttpServiceBase, I{table.ClassName}Service");
            sb.AppendLine("{");
            sb.AppendLine("    private readonly HttpClient _http;");
            sb.AppendLine($"    private const string BaseRoute = \"api/{pluralRoute}\";");
            sb.AppendLine();
            sb.AppendLine($"    public {table.ClassName}HttpService(HttpClient http)");
            sb.AppendLine("    {");
            sb.AppendLine("        _http = http;");
            sb.AppendLine("    }");
            sb.AppendLine();

            sb.AppendLine($"    public async Task<ResponseResult<List<{table.ClassName}Dto>>> GetAllAsync(CancellationToken cancellationToken = default)");
            sb.AppendLine("    {");
            sb.AppendLine("        var response = await _http.GetAsync(BaseRoute, cancellationToken);");
            sb.AppendLine($"        return await ReadResultAsync<List<{table.ClassName}Dto>>(response, cancellationToken);");
            sb.AppendLine("    }");
            sb.AppendLine();

            sb.AppendLine($"    public async Task<ResponseResult<{table.ClassName}Dto>> GetByIdAsync({pkParams}, CancellationToken cancellationToken = default)");
            sb.AppendLine("    {");
            sb.AppendLine($"        var response = await _http.GetAsync($\"{{BaseRoute}}/{pathSegment}\", cancellationToken);");
            sb.AppendLine($"        return await ReadResultAsync<{table.ClassName}Dto>(response, cancellationToken);");
            sb.AppendLine("    }");
            sb.AppendLine();

            sb.AppendLine($"    public async Task<ResponseResult<{table.ClassName}Dto>> CreateAsync({table.ClassName}CreateDto dto, CancellationToken cancellationToken = default)");
            sb.AppendLine("    {");
            sb.AppendLine("        var response = await _http.PostAsJsonAsync(BaseRoute, dto, cancellationToken);");
            sb.AppendLine($"        return await ReadResultAsync<{table.ClassName}Dto>(response, cancellationToken);");
            sb.AppendLine("    }");
            sb.AppendLine();

            sb.AppendLine($"    public async Task<ResponseResult<bool>> UpdateAsync({pkParams}, {table.ClassName}UpdateDto dto, CancellationToken cancellationToken = default)");
            sb.AppendLine("    {");
            sb.AppendLine($"        var response = await _http.PutAsJsonAsync($\"{{BaseRoute}}/{pathSegment}\", dto, cancellationToken);");
            sb.AppendLine("        return await ReadResultAsync<bool>(response, cancellationToken);");
            sb.AppendLine("    }");
            sb.AppendLine();

            sb.AppendLine($"    public async Task<ResponseResult<bool>> DeleteAsync({pkParams}, CancellationToken cancellationToken = default)");
            sb.AppendLine("    {");
            sb.AppendLine($"        var response = await _http.DeleteAsync($\"{{BaseRoute}}/{pathSegment}\", cancellationToken);");
            sb.AppendLine("        return await ReadResultAsync<bool>(response, cancellationToken);");
            sb.AppendLine("    }");

            sb.AppendLine("}");

            return new GeneratedFile { RelativePath = $"Services/{table.ClassName}HttpService.cs", Content = sb.ToString() };
        }

        private static GeneratedFile GenerateServiceRegistrationExtensions(List<TableInfo> tables, string rootNamespace, bool includeHttpServices)
        {
            var sb = new StringBuilder();
            sb.Append(Constant.GeneratedHeader);
            sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
            sb.AppendLine($"using {rootNamespace}.Services;");
            sb.AppendLine();
            sb.AppendLine($"namespace {rootNamespace}.Services;");
            sb.AppendLine();
            sb.AppendLine("public static class GeneratedServiceCollectionExtensions");
            sb.AppendLine("{");
            sb.AppendLine("    /// <summary>Registers the Dapper-backed services - use when this project owns the DB connection directly.</summary>");
            sb.AppendLine("    public static IServiceCollection AddGeneratedCrudServices(this IServiceCollection services)");
            sb.AppendLine("    {");
            foreach (var table in tables.OrderBy(t => t.ClassName))
            {
                sb.AppendLine($"        services.AddScoped<I{table.ClassName}Service, {table.ClassName}Service>();");
            }
            sb.AppendLine();
            sb.AppendLine("        return services;");
            sb.AppendLine("    }");

            if (includeHttpServices)
            {
                sb.AppendLine();
                sb.AppendLine("    /// <summary>Registers the HttpClient-backed services instead - use when this project calls the API");
                sb.AppendLine("    /// remotely. Configure each client's base address via AddHttpClient's builder, e.g.:");
                sb.AppendLine("    /// services.AddGeneratedCrudHttpServices(client => client.BaseAddress = new Uri(\"https://your-api/\"));</summary>");
                sb.AppendLine("    public static IServiceCollection AddGeneratedCrudHttpServices(this IServiceCollection services, Action<HttpClient> configureClient)");
                sb.AppendLine("    {");
                foreach (var table in tables.OrderBy(t => t.ClassName))
                {
                    sb.AppendLine($"        services.AddHttpClient<I{table.ClassName}Service, {table.ClassName}HttpService>(configureClient);");
                }
                sb.AppendLine();
                sb.AppendLine("        return services;");
                sb.AppendLine("    }");
            }

            sb.AppendLine("}");

            return new GeneratedFile { RelativePath = "Services/GeneratedServiceCollectionExtensions.cs", Content = sb.ToString() };
        }


        private static GeneratedFile GenerateListPage(TableInfo table, string rootNamespace, PageStyle pageStyle)
        {
            var pluralClass = NamingHelper.Pluralize(table.ClassName);
            var pluralRoute = pluralClass.ToLowerInvariant();
            var displayCols = table.Columns.OrderBy(c => c.OrdinalPosition).Take(8).ToList(); // keep the grid readable; full record is one click away on Edit
                                                                                              // The DeleteAsync/GetEditUrl helpers below always name their own parameter "item" - that's
                                                                                              // independent of what the markup's row variable is called (MudTable's RowTemplate implicitly
                                                                                              // exposes "context"; a plain @foreach uses whatever we name the loop variable, "item" here too).
            var pkArgsFromItem = BuildPkArgsFromItem(table, "item");
            var editUrlBody = "/" + pluralRoute + "/edit/" + string.Join("/", Constant.GetPkArgs(table).Select(a => "{item." + a.Column.PropertyName + "}"));

            var sb = new StringBuilder();
            sb.AppendLine("@* auto-generated by SQLAZOR - hand-edit freely, this file is not regenerated automatically *@");
            sb.AppendLine($"@page \"/{pluralRoute}\"");
            if (pageStyle == PageStyle.MudBlazor) sb.AppendLine("@using MudBlazor");
            sb.AppendLine($"@inject I{table.ClassName}Service Service");
            sb.AppendLine();

            switch (pageStyle)
            {
                case PageStyle.MudBlazor:
                    sb.AppendLine($"<PageTitle>{pluralClass}</PageTitle>");
                    sb.AppendLine();
                    sb.AppendLine($"<MudText Typo=\"Typo.h4\" Class=\"mb-4\">{pluralClass}</MudText>");
                    sb.AppendLine();
                    sb.AppendLine("<MudButton Variant=\"Variant.Filled\" Color=\"Color.Primary\" StartIcon=\"@Icons.Material.Filled.Add\"");
                    sb.AppendLine($"           Href=\"/{pluralRoute}/create\" Class=\"mb-4\">");
                    sb.AppendLine($"    New {table.ClassName}");
                    sb.AppendLine("</MudButton>");
                    sb.AppendLine();
                    sb.AppendLine("@if (_loading)");
                    sb.AppendLine("{");
                    sb.AppendLine("    <MudProgressCircular Indeterminate=\"true\" />");
                    sb.AppendLine("}");
                    sb.AppendLine("else if (!string.IsNullOrEmpty(_error))");
                    sb.AppendLine("{");
                    sb.AppendLine("    <MudAlert Severity=\"Severity.Error\">@_error</MudAlert>");
                    sb.AppendLine("}");
                    sb.AppendLine("else if (_items.Count == 0)");
                    sb.AppendLine("{");
                    sb.AppendLine("    <MudText>No records found.</MudText>");
                    sb.AppendLine("}");
                    sb.AppendLine("else");
                    sb.AppendLine("{");
                    sb.AppendLine("    <MudTable Items=\"_items\" Hover=\"true\" Breakpoint=\"Breakpoint.Sm\">");
                    sb.AppendLine("        <HeaderContent>");
                    foreach (var col in displayCols)
                    {
                        sb.AppendLine($"            <MudTh>{col.PropertyName}</MudTh>");
                    }
                    sb.AppendLine("            <MudTh></MudTh>");
                    sb.AppendLine("        </HeaderContent>");
                    sb.AppendLine("        <RowTemplate>");
                    foreach (var col in displayCols)
                    {
                        sb.AppendLine($"            <MudTd DataLabel=\"{col.PropertyName}\">@context.{col.PropertyName}</MudTd>");
                    }
                    sb.AppendLine("            <MudTd>");
                    sb.AppendLine("                <MudIconButton Icon=\"@Icons.Material.Filled.Edit\" Size=\"Size.Small\" Href=\"@GetEditUrl(context)\" />");
                    sb.AppendLine("                <MudIconButton Icon=\"@Icons.Material.Filled.Delete\" Size=\"Size.Small\" Color=\"Color.Error\" OnClick=\"() => DeleteAsync(context)\" />");
                    sb.AppendLine("            </MudTd>");
                    sb.AppendLine("        </RowTemplate>");
                    sb.AppendLine("        <PagerContent>");
                    sb.AppendLine("        <MudTablePager />");
                    sb.AppendLine("        </PagerContent>");
                    sb.AppendLine("    </MudTable>");
                    sb.AppendLine("}");
                    break;

                case PageStyle.Tabler:
                    sb.AppendLine($"<PageTitle>{pluralClass}</PageTitle>");
                    sb.AppendLine();
                    sb.AppendLine("<div class=\"page-header d-print-none\">");
                    sb.AppendLine("    <div class=\"row align-items-center\">");
                    sb.AppendLine("        <div class=\"col\">");
                    sb.AppendLine($"            <h2 class=\"page-title\">{pluralClass}</h2>");
                    sb.AppendLine("        </div>");
                    sb.AppendLine("        <div class=\"col-auto ms-auto\">");
                    sb.AppendLine($"            <a href=\"/{pluralRoute}/create\" class=\"btn btn-primary\">+ New {table.ClassName}</a>");
                    sb.AppendLine("        </div>");
                    sb.AppendLine("    </div>");
                    sb.AppendLine("</div>");
                    sb.AppendLine();
                    sb.AppendLine("@if (_loading)");
                    sb.AppendLine("{");
                    sb.AppendLine("    <div class=\"text-secondary\">Loading…</div>");
                    sb.AppendLine("}");
                    sb.AppendLine("else if (!string.IsNullOrEmpty(_error))");
                    sb.AppendLine("{");
                    sb.AppendLine("    <div class=\"alert alert-danger\">@_error</div>");
                    sb.AppendLine("}");
                    sb.AppendLine("else if (_items.Count == 0)");
                    sb.AppendLine("{");
                    sb.AppendLine("    <div class=\"text-secondary\">No records found.</div>");
                    sb.AppendLine("}");
                    sb.AppendLine("else");
                    sb.AppendLine("{");
                    sb.AppendLine("    <div class=\"card\">");
                    sb.AppendLine("        <div class=\"table-responsive\">");
                    sb.AppendLine("            <table class=\"table table-vcenter card-table\">");
                    sb.AppendLine("                <thead>");
                    sb.AppendLine("                    <tr>");
                    foreach (var col in displayCols)
                    {
                        sb.AppendLine($"                        <th>{col.PropertyName}</th>");
                    }
                    sb.AppendLine("                        <th class=\"w-1\"></th>");
                    sb.AppendLine("                    </tr>");
                    sb.AppendLine("                </thead>");
                    sb.AppendLine("                <tbody>");
                    sb.AppendLine("                    @foreach (var item in _items)");
                    sb.AppendLine("                    {");
                    sb.AppendLine("                        <tr>");
                    foreach (var col in displayCols)
                    {
                        sb.AppendLine($"                            <td>@item.{col.PropertyName}</td>");
                    }
                    sb.AppendLine("                            <td class=\"text-nowrap\">");
                    sb.AppendLine("                                <a href=\"@GetEditUrl(item)\" class=\"btn btn-sm btn-outline-secondary\">Edit</a>");
                    sb.AppendLine("                                <button class=\"btn btn-sm btn-outline-danger\" @onclick=\"() => DeleteAsync(item)\">Delete</button>");
                    sb.AppendLine("                            </td>");
                    sb.AppendLine("                        </tr>");
                    sb.AppendLine("                    }");
                    sb.AppendLine("                </tbody>");
                    sb.AppendLine("            </table>");
                    sb.AppendLine("        </div>");
                    sb.AppendLine("    </div>");
                    sb.AppendLine("}");
                    break;

                default: // Plain
                    sb.AppendLine($"<PageTitle>{pluralClass}</PageTitle>");
                    sb.AppendLine();
                    sb.AppendLine($"<h3>{pluralClass}</h3>");
                    sb.AppendLine();
                    sb.AppendLine($"<a href=\"/{pluralRoute}/create\" class=\"btn btn-primary mb-3\">+ New {table.ClassName}</a>");
                    sb.AppendLine();
                    sb.AppendLine("@if (_loading)");
                    sb.AppendLine("{");
                    sb.AppendLine("    <p>Loading…</p>");
                    sb.AppendLine("}");
                    sb.AppendLine("else if (!string.IsNullOrEmpty(_error))");
                    sb.AppendLine("{");
                    sb.AppendLine("    <div class=\"alert alert-danger\">@_error</div>");
                    sb.AppendLine("}");
                    sb.AppendLine("else if (_items.Count == 0)");
                    sb.AppendLine("{");
                    sb.AppendLine("    <p>No records found.</p>");
                    sb.AppendLine("}");
                    sb.AppendLine("else");
                    sb.AppendLine("{");
                    sb.AppendLine("    <table class=\"table table-striped\">");
                    sb.AppendLine("        <thead>");
                    sb.AppendLine("            <tr>");
                    foreach (var col in displayCols)
                    {
                        sb.AppendLine($"                <th>{col.PropertyName}</th>");
                    }
                    sb.AppendLine("                <th></th>");
                    sb.AppendLine("            </tr>");
                    sb.AppendLine("        </thead>");
                    sb.AppendLine("        <tbody>");
                    sb.AppendLine("            @foreach (var item in _items)");
                    sb.AppendLine("            {");
                    sb.AppendLine("                <tr>");
                    foreach (var col in displayCols)
                    {
                        sb.AppendLine($"                    <td>@item.{col.PropertyName}</td>");
                    }
                    sb.AppendLine("                    <td>");
                    sb.AppendLine("                        <a href=\"@GetEditUrl(item)\" class=\"btn btn-sm btn-secondary\">Edit</a>");
                    sb.AppendLine("                        <button class=\"btn btn-sm btn-danger\" @onclick=\"() => DeleteAsync(item)\">Delete</button>");
                    sb.AppendLine("                    </td>");
                    sb.AppendLine("                </tr>");
                    sb.AppendLine("            }");
                    sb.AppendLine("        </tbody>");
                    sb.AppendLine("    </table>");
                    sb.AppendLine("}");
                    break;
            }

            sb.AppendLine();
            sb.AppendLine("@code {");
            sb.AppendLine($"    private List<{table.ClassName}Dto> _items = [];");
            sb.AppendLine("    private bool _loading = true;");
            sb.AppendLine("    private string? _error;");
            sb.AppendLine();
            sb.AppendLine("    protected override async Task OnInitializedAsync() => await LoadAsync();");
            sb.AppendLine();
            sb.AppendLine("    private async Task LoadAsync()");
            sb.AppendLine("    {");
            sb.AppendLine("        _loading = true;");
            sb.AppendLine("        _error = null;");
            sb.AppendLine();
            sb.AppendLine("        var result = await Service.GetAllAsync();");
            sb.AppendLine("        if (result.IsSuccessful)");
            sb.AppendLine("        {");
            sb.AppendLine("            _items = result.Data ?? [];");
            sb.AppendLine("        }");
            sb.AppendLine("        else");
            sb.AppendLine("        {");
            sb.AppendLine("            _error = result.Message;");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        _loading = false;");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine($"    private async Task DeleteAsync({table.ClassName}Dto item)");
            sb.AppendLine("    {");
            sb.AppendLine($"        var result = await Service.DeleteAsync({pkArgsFromItem});");
            sb.AppendLine("        if (result.IsSuccessful)");
            sb.AppendLine("        {");
            sb.AppendLine("            await LoadAsync();");
            sb.AppendLine("        }");
            sb.AppendLine("        else");
            sb.AppendLine("        {");
            sb.AppendLine("            _error = result.Message;");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine($"    private static string GetEditUrl({table.ClassName}Dto item) => $\"{editUrlBody}\";");
            sb.AppendLine("}");

            return new GeneratedFile { RelativePath = $"{PageFolder(table)}/{table.ClassName}List.razor", Content = sb.ToString() };
        }


        private static string BuildPkArgList(TableInfo table) =>
       string.Join(", ", Constant.GetPkArgs(table).Select(a => a.ArgName));
        private static string BuildPkWhereSql(TableInfo table) =>
      string.Join(" AND ", Constant.GetPkArgs(table).Select(a => $"[{a.Column.ColumnName}] = @{a.ArgName}"));

        private static string BuildPkParamsObject(TableInfo table) =>
            "new { " + string.Join(", ", Constant.GetPkArgs(table).Select(a => a.ArgName)) + " }";
        private static string BuildPkArgsFromItem(TableInfo table, string itemVar) =>
       string.Join(", ", Constant.GetPkArgs(table).Select(a => $"{itemVar}.{a.Column.PropertyName}"));

        /// <summary>Route/URL segment template, e.g. "{id}" or "{orderId}/{lineNumber}" — valid as both
        /// an ASP.NET route template and (since the token syntax matches) a C# interpolated string body.</summary>
       

        private static string BuildPkMethodParams(TableInfo table) =>
            string.Join(", ", Constant.GetPkArgs(table).Select(a => $"{a.Column.ClrType.TrimEnd('?')} {a.ArgName}"));


        // ----------------------------------------------------------------------
        // Create page
        // ----------------------------------------------------------------------

        private static GeneratedFile GenerateCreatePage(TableInfo table, string rootNamespace, PageStyle pageStyle)
        {
            var pluralRoute = NamingHelper.Pluralize(table.ClassName).ToLowerInvariant();
            var creatableCols = GetCreatableColumns(table);

            var sb = new StringBuilder();
            sb.AppendLine("@* auto-generated by SQLAZOR - hand-edit freely, this file is not regenerated automatically *@");
            sb.AppendLine($"@page \"/{pluralRoute}/create\"");
            if (pageStyle == PageStyle.MudBlazor) sb.AppendLine("@using MudBlazor");
            sb.AppendLine($"@inject I{table.ClassName}Service Service");
            sb.AppendLine("@inject NavigationManager Nav");
            sb.AppendLine();

            switch (pageStyle)
            {
                case PageStyle.MudBlazor:
                    sb.AppendLine($"<PageTitle>New {table.ClassName}</PageTitle>");
                    sb.AppendLine();
                    sb.AppendLine($"<MudText Typo=\"Typo.h4\" Class=\"mb-4\">New {table.ClassName}</MudText>");
                    sb.AppendLine();
                    sb.AppendLine("@if (!string.IsNullOrEmpty(_error))");
                    sb.AppendLine("{");
                    sb.AppendLine("    <MudAlert Severity=\"Severity.Error\" Class=\"mb-4\">@_error</MudAlert>");
                    sb.AppendLine("}");
                    sb.AppendLine();
                    sb.AppendLine("<MudForm Model=\"_model\">");
                    AppendMudFields(sb, creatableCols, "_model");
                    sb.AppendLine("    <MudButton Variant=\"Variant.Filled\" Color=\"Color.Primary\" OnClick=\"SubmitAsync\" Disabled=\"@_saving\">Create</MudButton>");
                    sb.AppendLine($"    <MudButton Variant=\"Variant.Text\" Href=\"/{pluralRoute}\">Cancel</MudButton>");
                    sb.AppendLine("</MudForm>");
                    break;

                case PageStyle.Tabler:
                    sb.AppendLine($"<PageTitle>New {table.ClassName}</PageTitle>");
                    sb.AppendLine();
                    sb.AppendLine("<div class=\"page-header d-print-none\">");
                    sb.AppendLine("    <div class=\"row align-items-center\">");
                    sb.AppendLine("        <div class=\"col\">");
                    sb.AppendLine($"            <h2 class=\"page-title\">New {table.ClassName}</h2>");
                    sb.AppendLine("        </div>");
                    sb.AppendLine("    </div>");
                    sb.AppendLine("</div>");
                    sb.AppendLine();
                    sb.AppendLine("@if (!string.IsNullOrEmpty(_error))");
                    sb.AppendLine("{");
                    sb.AppendLine("    <div class=\"alert alert-danger\">@_error</div>");
                    sb.AppendLine("}");
                    sb.AppendLine();
                    sb.AppendLine("<div class=\"card\">");
                    sb.AppendLine("    <div class=\"card-body\">");
                    sb.AppendLine("        <EditForm Model=\"_model\" OnValidSubmit=\"SubmitAsync\">");
                    AppendPlainFields(sb, creatableCols, "_model", indent: "            ");
                    sb.AppendLine("            <div class=\"form-footer\">");
                    sb.AppendLine("                <button type=\"submit\" class=\"btn btn-primary\" disabled=\"@_saving\">Create</button>");
                    sb.AppendLine($"                <a href=\"/{pluralRoute}\" class=\"btn btn-link\">Cancel</a>");
                    sb.AppendLine("            </div>");
                    sb.AppendLine("        </EditForm>");
                    sb.AppendLine("    </div>");
                    sb.AppendLine("</div>");
                    break;

                default: // Plain
                    sb.AppendLine($"<PageTitle>New {table.ClassName}</PageTitle>");
                    sb.AppendLine();
                    sb.AppendLine($"<h3>New {table.ClassName}</h3>");
                    sb.AppendLine();
                    sb.AppendLine("@if (!string.IsNullOrEmpty(_error))");
                    sb.AppendLine("{");
                    sb.AppendLine("    <div class=\"alert alert-danger\">@_error</div>");
                    sb.AppendLine("}");
                    sb.AppendLine();
                    sb.AppendLine("<EditForm Model=\"_model\" OnValidSubmit=\"SubmitAsync\">");
                    AppendPlainFields(sb, creatableCols, "_model");
                    sb.AppendLine("    <button type=\"submit\" class=\"btn btn-primary\" disabled=\"@_saving\">Create</button>");
                    sb.AppendLine($"    <a href=\"/{pluralRoute}\" class=\"btn btn-secondary\">Cancel</a>");
                    sb.AppendLine("</EditForm>");
                    break;
            }

            sb.AppendLine();
            sb.AppendLine("@code {");
            sb.AppendLine($"    private const string ListUrl = \"/{pluralRoute}\";");
            sb.AppendLine("    private bool _saving;");
            sb.AppendLine("    private string? _error;");
            sb.AppendLine($"    private {table.ClassName}CreateDto _model = new();");
            sb.AppendLine();
            sb.AppendLine("    private async Task SubmitAsync()");
            sb.AppendLine("    {");
            sb.AppendLine("        _saving = true;");
            sb.AppendLine("        _error = null;");
            sb.AppendLine();
            sb.AppendLine("        var result = await Service.CreateAsync(_model);");
            sb.AppendLine("        if (result.IsSuccessful)");
            sb.AppendLine("        {");
            sb.AppendLine("            Nav.NavigateTo(ListUrl);");
            sb.AppendLine("        }");
            sb.AppendLine("        else");
            sb.AppendLine("        {");
            sb.AppendLine("            _error = result.Message;");
            sb.AppendLine("            _saving = false;");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            return new GeneratedFile { RelativePath = $"{PageFolder(table)}/{table.ClassName}Create.razor", Content = sb.ToString() };
        }

        // ----------------------------------------------------------------------
        // Edit page
        // ----------------------------------------------------------------------

        private static GeneratedFile GenerateEditPage(TableInfo table, string rootNamespace, PageStyle pageStyle)
        {
            var pluralRoute = NamingHelper.Pluralize(table.ClassName).ToLowerInvariant();
            var routeParams = GetPkRouteParams(table);
            var routeTemplate = string.Join("/", routeParams.Select(p => "{" + p.RouteParamName + RouteConstraintFor(p.Column.ClrType) + "}"));
            var routeParamArgs = string.Join(", ", routeParams.Select(p => p.RouteParamName));
            var updatableCols = GetUpdatableColumns(table);

            var sb = new StringBuilder();
            sb.AppendLine("@* auto-generated by SQLAZOR - hand-edit freely, this file is not regenerated automatically *@");
            sb.AppendLine($"@page \"/{pluralRoute}/edit/{routeTemplate}\"");
            if (pageStyle == PageStyle.MudBlazor) sb.AppendLine("@using MudBlazor");
            sb.AppendLine($"@inject I{table.ClassName}Service Service");
            sb.AppendLine("@inject NavigationManager Nav");
            sb.AppendLine();

            switch (pageStyle)
            {
                case PageStyle.MudBlazor:
                    sb.AppendLine($"<PageTitle>Edit {table.ClassName}</PageTitle>");
                    sb.AppendLine();
                    sb.AppendLine($"<MudText Typo=\"Typo.h4\" Class=\"mb-4\">Edit {table.ClassName}</MudText>");
                    sb.AppendLine();
                    sb.AppendLine("@if (_loading)");
                    sb.AppendLine("{");
                    sb.AppendLine("    <MudProgressCircular Indeterminate=\"true\" />");
                    sb.AppendLine("}");
                    sb.AppendLine("else");
                    sb.AppendLine("{");
                    sb.AppendLine("    @if (!string.IsNullOrEmpty(_error))");
                    sb.AppendLine("    {");
                    sb.AppendLine("        <MudAlert Severity=\"Severity.Error\" Class=\"mb-4\">@_error</MudAlert>");
                    sb.AppendLine("    }");
                    sb.AppendLine();
                    sb.AppendLine("    <MudForm Model=\"_model\">");
                    AppendMudFields(sb, updatableCols, "_model", indent: "        ");
                    sb.AppendLine("        <MudButton Variant=\"Variant.Filled\" Color=\"Color.Primary\" OnClick=\"SubmitAsync\" Disabled=\"@_saving\">Save</MudButton>");
                    sb.AppendLine($"        <MudButton Variant=\"Variant.Text\" Href=\"/{pluralRoute}\">Cancel</MudButton>");
                    sb.AppendLine("    </MudForm>");
                    sb.AppendLine("}");
                    break;

                case PageStyle.Tabler:
                    sb.AppendLine($"<PageTitle>Edit {table.ClassName}</PageTitle>");
                    sb.AppendLine();
                    sb.AppendLine("<div class=\"page-header d-print-none\">");
                    sb.AppendLine("    <div class=\"row align-items-center\">");
                    sb.AppendLine("        <div class=\"col\">");
                    sb.AppendLine($"            <h2 class=\"page-title\">Edit {table.ClassName}</h2>");
                    sb.AppendLine("        </div>");
                    sb.AppendLine("    </div>");
                    sb.AppendLine("</div>");
                    sb.AppendLine();
                    sb.AppendLine("@if (_loading)");
                    sb.AppendLine("{");
                    sb.AppendLine("    <div class=\"text-secondary\">Loading…</div>");
                    sb.AppendLine("}");
                    sb.AppendLine("else");
                    sb.AppendLine("{");
                    sb.AppendLine("    @if (!string.IsNullOrEmpty(_error))");
                    sb.AppendLine("    {");
                    sb.AppendLine("        <div class=\"alert alert-danger\">@_error</div>");
                    sb.AppendLine("    }");
                    sb.AppendLine();
                    sb.AppendLine("    <div class=\"card\">");
                    sb.AppendLine("        <div class=\"card-body\">");
                    sb.AppendLine("            <EditForm Model=\"_model\" OnValidSubmit=\"SubmitAsync\">");
                    AppendPlainFields(sb, updatableCols, "_model", indent: "                ");
                    sb.AppendLine("                <div class=\"form-footer\">");
                    sb.AppendLine("                    <button type=\"submit\" class=\"btn btn-primary\" disabled=\"@_saving\">Save</button>");
                    sb.AppendLine($"                    <a href=\"/{pluralRoute}\" class=\"btn btn-link\">Cancel</a>");
                    sb.AppendLine("                </div>");
                    sb.AppendLine("            </EditForm>");
                    sb.AppendLine("        </div>");
                    sb.AppendLine("    </div>");
                    sb.AppendLine("}");
                    break;

                default: // Plain
                    sb.AppendLine($"<PageTitle>Edit {table.ClassName}</PageTitle>");
                    sb.AppendLine();
                    sb.AppendLine($"<h3>Edit {table.ClassName}</h3>");
                    sb.AppendLine();
                    sb.AppendLine("@if (_loading)");
                    sb.AppendLine("{");
                    sb.AppendLine("    <p>Loading…</p>");
                    sb.AppendLine("}");
                    sb.AppendLine("else");
                    sb.AppendLine("{");
                    sb.AppendLine("    @if (!string.IsNullOrEmpty(_error))");
                    sb.AppendLine("    {");
                    sb.AppendLine("        <div class=\"alert alert-danger\">@_error</div>");
                    sb.AppendLine("    }");
                    sb.AppendLine();
                    sb.AppendLine("    <EditForm Model=\"_model\" OnValidSubmit=\"SubmitAsync\">");
                    AppendPlainFields(sb, updatableCols, "_model", indent: "        ");
                    sb.AppendLine("        <button type=\"submit\" class=\"btn btn-primary\" disabled=\"@_saving\">Save</button>");
                    sb.AppendLine($"        <a href=\"/{pluralRoute}\" class=\"btn btn-secondary\">Cancel</a>");
                    sb.AppendLine("    </EditForm>");
                    sb.AppendLine("}");
                    break;
            }

            sb.AppendLine();
            sb.AppendLine("@code {");
            foreach (var p in routeParams)
            {
                sb.AppendLine($"    [Parameter] public {p.Column.ClrType.TrimEnd('?')} {p.RouteParamName} {{ get; set; }}");
            }
            sb.AppendLine();
            sb.AppendLine($"    private const string ListUrl = \"/{pluralRoute}\";");
            sb.AppendLine("    private bool _loading = true;");
            sb.AppendLine("    private bool _saving;");
            sb.AppendLine("    private string? _error;");
            sb.AppendLine($"    private {table.ClassName}UpdateDto _model = new();");
            sb.AppendLine();
            sb.AppendLine("    protected override async Task OnInitializedAsync()");
            sb.AppendLine("    {");
            sb.AppendLine($"        var result = await Service.GetByIdAsync({routeParamArgs});");
            sb.AppendLine("        if (result.IsSuccessful && result.Data is not null)");
            sb.AppendLine("        {");
            foreach (var col in updatableCols)
            {
                sb.AppendLine($"            _model.{col.PropertyName} = result.Data.{col.PropertyName};");
            }
            sb.AppendLine("        }");
            sb.AppendLine("        else");
            sb.AppendLine("        {");
            sb.AppendLine($"            _error = result.Message ?? \"{table.ClassName} not found.\";");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        _loading = false;");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    private async Task SubmitAsync()");
            sb.AppendLine("    {");
            sb.AppendLine("        _saving = true;");
            sb.AppendLine("        _error = null;");
            sb.AppendLine();
            sb.AppendLine($"        var result = await Service.UpdateAsync({routeParamArgs}, _model);");
            sb.AppendLine("        if (result.IsSuccessful)");
            sb.AppendLine("        {");
            sb.AppendLine("            Nav.NavigateTo(ListUrl);");
            sb.AppendLine("        }");
            sb.AppendLine("        else");
            sb.AppendLine("        {");
            sb.AppendLine("            _error = result.Message;");
            sb.AppendLine("            _saving = false;");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            return new GeneratedFile { RelativePath = $"{PageFolder(table)}/{table.ClassName}Edit.razor", Content = sb.ToString() };
        }


        /// <summary>
        /// Appends one MudBlazor field component per column. MudNumericField/MudTextField's generic
        /// T is set to the column's exact declared CLR type (nullable marker included) since
        /// @bind-Value requires an exact match with the bound property's type.
        /// </summary>
        private static void AppendMudFields(StringBuilder sb, List<ColumnInfo> columns, string modelVar, string indent = "    ")
        {
            foreach (var col in columns)
            {
                if (!IsMudRenderable(col.ClrType))
                {
                    sb.AppendLine($"{indent}@* {col.PropertyName} ({col.SqlDataType}) isn't auto-renderable - add a field for it by hand if needed *@");
                    continue;
                }

                var baseType = col.ClrType.TrimEnd('?');

                switch (baseType)
                {
                    case "string":
                        sb.AppendLine($"{indent}<MudTextField @bind-Value=\"{modelVar}.{col.PropertyName}\" Label=\"{col.PropertyName}\" Variant=\"Variant.Outlined\" Class=\"mb-3\" />");
                        break;

                    case "int" or "long" or "short" or "byte" or "decimal" or "double" or "float":
                        sb.AppendLine($"{indent}<MudNumericField T=\"{col.ClrType}\" @bind-Value=\"{modelVar}.{col.PropertyName}\" Label=\"{col.PropertyName}\" Variant=\"Variant.Outlined\" Class=\"mb-3\" />");
                        break;

                    case "bool":
                        sb.AppendLine($"{indent}<MudCheckBox @bind-Value=\"{modelVar}.{col.PropertyName}\" Label=\"{col.PropertyName}\" Class=\"mb-3\" />");
                        break;

                    case "DateTime" or "DateTimeOffset" or "Guid":
                        // MudTextField<T> works with any T via its default ToString()/Parse converter -
                        // simpler and more predictable here than MudDatePicker's separate nullable-Date binding.
                        sb.AppendLine($"{indent}<MudTextField T=\"{col.ClrType}\" @bind-Value=\"{modelVar}.{col.PropertyName}\" Label=\"{col.PropertyName}\" Variant=\"Variant.Outlined\" Class=\"mb-3\" />");
                        break;

                    default:
                        sb.AppendLine($"{indent}@* {col.PropertyName} ({col.SqlDataType}) isn't auto-renderable - add a field for it by hand if needed *@");
                        break;
                }
            }
        }

        private static bool IsMudRenderable(string clrType) => clrType.TrimEnd('?') switch
        {
            "byte[]" or "TimeSpan" or "object" => false,
            _ => true
        };


        private static void AppendPlainFields(StringBuilder sb, List<ColumnInfo> columns, string modelVar, string indent = "    ")
        {
            foreach (var col in columns)
            {
                var (renderable, inputType) = GetHtmlInputInfo(col.ClrType);
                if (!renderable)
                {
                    sb.AppendLine($"{indent}@* {col.PropertyName} ({col.SqlDataType}) isn't auto-renderable - add a field for it by hand if needed *@");
                    continue;
                }

                if (inputType == "checkbox")
                {
                    sb.AppendLine($"{indent}<div class=\"mb-3 form-check\">");
                    sb.AppendLine($"{indent}    <input type=\"checkbox\" class=\"form-check-input\" id=\"{modelVar}_{col.PropertyName}\" @bind-value=\"{modelVar}.{col.PropertyName}\" />");
                    sb.AppendLine($"{indent}    <label class=\"form-check-label\" for=\"{modelVar}_{col.PropertyName}\">{col.PropertyName}</label>");
                    sb.AppendLine($"{indent}</div>");
                }
                else
                {
                    sb.AppendLine($"{indent}<div class=\"mb-3\">");
                    sb.AppendLine($"{indent}    <label class=\"form-label\" for=\"{modelVar}_{col.PropertyName}\">{col.PropertyName}</label>");
                    sb.AppendLine($"{indent}    <input type=\"{inputType}\" class=\"form-control\" id=\"{modelVar}_{col.PropertyName}\" @bind-value=\"{modelVar}.{col.PropertyName}\" />");
                    sb.AppendLine($"{indent}</div>");
                }
            }
        }


        private static (bool Renderable, string InputType) GetHtmlInputInfo(string clrType) => clrType.TrimEnd('?') switch
        {
            "string" => (true, "text"),
            "int" or "long" or "short" or "byte" => (true, "number"),
            "decimal" or "double" or "float" => (true, "number"),
            "bool" => (true, "checkbox"),
            "DateTime" => (true, "datetime-local"),
            "DateTimeOffset" => (true, "text"), // datetime-local's format doesn't carry an offset; keep this one as plain text
            "Guid" => (true, "text"),
            _ => (false, "")
        };

        /// <summary>Blazor route constraint token for a CLR type, or "" for types with no built-in constraint (treated as string).</summary>
        private static string RouteConstraintFor(string clrType) => clrType.TrimEnd('?') switch
        {
            "int" => ":int",
            "long" => ":long",
            "bool" => ":bool",
            "decimal" => ":decimal",
            "double" => ":double",
            "Guid" => ":guid",
            _ => ""
        };

        /// <summary>
        /// Route parameter names for the edit page. A single-column PK is always called "Id" (PascalCase,
        /// Blazor [Parameter] convention) regardless of the real column name, matching the "id" method-arg
        /// convention used elsewhere; a composite PK uses each column's real property name so multi-segment
        /// routes stay self-describing (e.g. /orders/edit/{OrderId}/{LineNumber}).
        /// </summary>
        private static List<(string RouteParamName, ColumnInfo Column)> GetPkRouteParams(TableInfo table)
        {
            var pkCols = table.Columns.Where(c => c.IsPrimaryKey).OrderBy(c => c.OrdinalPosition).ToList();
            if (pkCols.Count == 1)
                return [("Id", pkCols[0])];

            return pkCols.Select(c => (c.PropertyName, c)).ToList();
        }

        private static GeneratedFile GenerateInfrastructureServiceRegistration(List<TableInfo> tables, string infrastructureNamespace, string applicationNamespace)
        {
            var sb = new StringBuilder();
            sb.Append(Constant.GeneratedHeader);
            sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
            sb.AppendLine($"using {applicationNamespace}.Services;");
            sb.AppendLine();
            sb.AppendLine($"namespace {infrastructureNamespace}.Services;");
            sb.AppendLine();
            sb.AppendLine("/// <summary>Registers the Dapper-backed services - use when this project owns the DB connection directly.</summary>");
            sb.AppendLine("public static class GeneratedServiceCollectionExtensions");
            sb.AppendLine("{");
            sb.AppendLine("    public static IServiceCollection AddGeneratedCrudServices(this IServiceCollection services)");
            sb.AppendLine("    {");
            foreach (var table in tables.OrderBy(t => t.ClassName))
            {
                sb.AppendLine($"        services.AddScoped<I{table.ClassName}Service, {table.ClassName}Service>();");
            }
            sb.AppendLine();
            sb.AppendLine("        return services;");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            return new GeneratedFile { RelativePath = "Services/GeneratedServiceCollectionExtensions.cs", Content = sb.ToString() };
        }

        private static GeneratedFile GenerateWebHttpServiceRegistration(List<TableInfo> tables, string webNamespace, string applicationNamespace)
        {
            var sb = new StringBuilder();
            sb.Append(Constant.GeneratedHeader);
            sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
            sb.AppendLine($"using {applicationNamespace}.Services;");
            sb.AppendLine();
            sb.AppendLine($"namespace {webNamespace}.Services;");
            sb.AppendLine();
            sb.AppendLine("/// <summary>Registers the HttpClient-backed services - use when this project calls the API remotely.");
            sb.AppendLine("/// Configure each client's base address via AddHttpClient's builder, e.g.:");
            sb.AppendLine("/// services.AddGeneratedCrudHttpServices(client => client.BaseAddress = new Uri(\"https://your-api/\"));</summary>");
            sb.AppendLine("public static class GeneratedHttpServiceCollectionExtensions");
            sb.AppendLine("{");
            sb.AppendLine("    public static IServiceCollection AddGeneratedCrudHttpServices(this IServiceCollection services, Action<HttpClient> configureClient)");
            sb.AppendLine("    {");
            foreach (var table in tables.OrderBy(t => t.ClassName))
            {
                sb.AppendLine($"        services.AddHttpClient<I{table.ClassName}Service, {table.ClassName}HttpService>(configureClient);");
            }
            sb.AppendLine();
            sb.AppendLine("        return services;");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            return new GeneratedFile { RelativePath = "Services/GeneratedHttpServiceCollectionExtensions.cs", Content = sb.ToString() };
        }

    }
}
