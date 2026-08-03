namespace SQLAZOR.Models;

public sealed class ProjectLayers
{
    public required bool IsCleanArchitecture { get; init; }
    public required string ApplicationName { get; init; }

    public required string DomainNamespace { get; init; }
    public required string ApplicationNamespace { get; init; }
    public required string InfrastructureNamespace { get; init; }
    public required string WebNamespace { get; init; }

    /// <summary>Folder each layer's files are nested under, e.g. "MyApp.Domain/" - empty string when not using Clean Architecture.</summary>
    public required string DomainPath { get; init; }
    public required string ApplicationPath { get; init; }
    public required string InfrastructurePath { get; init; }
    public required string WebPath { get; init; }

    public static ProjectLayers Create(bool useCleanArchitecture, string applicationName, string rootNamespace)
    {
        if (!useCleanArchitecture)
        {
            return new ProjectLayers
            {
                IsCleanArchitecture = false,
                ApplicationName = applicationName,
                DomainNamespace = rootNamespace,
                ApplicationNamespace = rootNamespace,
                InfrastructureNamespace = rootNamespace,
                WebNamespace = rootNamespace,
                DomainPath = "",
                ApplicationPath = "",
                InfrastructurePath = "",
                WebPath = ""
            };
        }

        return new ProjectLayers
        {
            IsCleanArchitecture = true,
            ApplicationName = applicationName,
            DomainNamespace = $"{applicationName}.Domain",
            ApplicationNamespace = $"{applicationName}.Application",
            InfrastructureNamespace = $"{applicationName}.Infrastructure",
            WebNamespace = $"{applicationName}.Web",
            DomainPath = $"{applicationName}.Domain/",
            ApplicationPath = $"{applicationName}.Application/",
            InfrastructurePath = $"{applicationName}.Infrastructure/",
            WebPath = $"{applicationName}.Web/"
        };
    }
}