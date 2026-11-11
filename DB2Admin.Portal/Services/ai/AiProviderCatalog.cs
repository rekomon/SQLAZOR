using SQLAZOR.Models;

namespace SQLAZOR.Services;

public sealed record AiModelOption(string Id, string Name);

public sealed record AiProviderInfo(
    string Id,
    string DisplayName,
    string DefaultEndpoint,
    string DefaultModel,
    bool RequiresApiKey,
    bool SupportsImages,
    IReadOnlyList<AiModelOption> Models);

/// <summary>
/// Hardcoded provider and model lists. Models are deliberately curated rather than discovered
/// from a provider API, so what the UI offers is exactly what has been tested with this app.
/// </summary>
public static class AiProviderCatalog
{
    public const string Ollama = "ollama";
    public const string DeepSeek = "deepseek";
    public const string OpenRouter = "openrouter";

    public static readonly IReadOnlyList<AiProviderInfo> All =
    [
        new(
            Ollama,
            "Ollama",
            "http://localhost:11434",
            "llama3.2:3b",
            RequiresApiKey: false,
            SupportsImages: true,
            [
                new("llama3.2:3b","Meta - Llama 3.2 3B (Local)"),
                new("gpt-oss:120b-cloud", "Chat GPT 120B  (Cloud)"),
                new("nemotron-3-super:cloud", "Nvidia Nemotron 3 Super  (Cloud)"),
                new("minimax-m3.1-flash:cloud", "minimax m3.1  (Cloud)"),
                new("gemma4:31b-cloud", "Google Gemma 4 (Cloud)"),
                new("beam:cloud", "Beam (Cloud)"),
                new("llava:latest", "LLaVA (vision) (Local)"),
                new("qwen2.5vl:7b", "Qwen 2.5 VL 7B (vision) (Local)"),
                new("gpt-oss:20b", "Chat GPT 20B (Local)"),
            ]),
        new(
            OpenRouter,
            "OpenRouter",
            "https://openrouter.ai/api/v1",
            "nvidia/nemotron-3-ultra-550b-a55b:free",
            RequiresApiKey: true,
            SupportsImages: true,
            [
                new("openrouter/free","Automatically route"),
                new("nvidia/nemotron-3-ultra-550b-a55b:free", "NVIDIA Nemotron 3 Ultra (free)"),
                new("nvidia/nemotron-3.5-lightning:free", "NVIDIA Nemotron 3.5 Lightning (free)"),
                new("nvidia/nemotron-3.5-super-vl:free", "NVIDIA Nemotron 3.5 Super VL (free, vision)"),
                new("google/gemma-4-26b-a4b-it:free", "Google Gemma 4 26B (free, vision)"),
                new("thinkingmachines/inkling-2-small:free", "Inkling 2 Small (free, vision)"),
                new("cohere/north-mini-code:free", "Cohere North Mini Code (free)"),
                new("recursive/flint-v1.0:free", "Flint v1.0 (free)")
            ]),
        new(
            DeepSeek,
            "DeepSeek",
            "https://api.deepseek.com",
            "deepseek-chat",
            RequiresApiKey: true,
            SupportsImages: false,
            [
                new("deepseek-chat", "DeepSeek Chat"),
                new("deepseek-reasoner", "DeepSeek Reasoner")
            ])
    ];

    public static bool TryGet(string? id, out AiProviderInfo info)
    {
        info = All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase))!;
        return info is not null;
    }

    public static AiProviderInfo Get(string? id) => TryGet(id, out var info) ? info : All[0];

    public static AiProviderConfig CreateConfig(string id)
    {
        var info = Get(id);
        return new AiProviderConfig
        {
            Provider = info.Id,
            Endpoint = info.DefaultEndpoint,
            Model = info.DefaultModel
        };
    }

    /// <summary>Fills in missing providers/blank fields so the UI always has a usable config.</summary>
    public static void EnsureDefaults(IDictionary<string, AiProviderConfig> configs)
    {
        foreach (var info in All)
        {
            if (!configs.TryGetValue(info.Id, out var config) || config is null)
            {
                configs[info.Id] = CreateConfig(info.Id);
                continue;
            }

            config.Provider = info.Id;
            if (string.IsNullOrWhiteSpace(config.Endpoint))
                config.Endpoint = info.DefaultEndpoint;
            if (string.IsNullOrWhiteSpace(config.Model))
                config.Model = info.DefaultModel;
            config.ApiKey ??= string.Empty;
        }
    }
}
