namespace SQLAZOR.Models;

/// <summary>
/// Connection settings for one AI provider. <see cref="ApiKey"/> is persisted only in
/// %LOCALAPPDATA%\SQLAZOR\ai-settings.json - conversations and exports never reference it.
/// </summary>
public sealed class AiProviderConfig
{
    public string Provider { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
}

public sealed class AiSettingsSnapshot
{
    public string Provider { get; set; } = "ollama";
    public Dictionary<string, AiProviderConfig> Configs { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
