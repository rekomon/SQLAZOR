using SQLAZOR.Models;
using System.Runtime.CompilerServices;

namespace SQLAZOR.Services;

/// <summary>
/// Dispatches AI calls to the provider named in the supplied config. Callers pass the config they
/// resolved from <c>GenerationState.CurrentAiConfig</c>; an unknown provider id fails loudly rather
/// than silently falling back to a different vendor.
/// </summary>
public interface IChatProviderService
{
    Task<string> ChatAsync(
        AiProviderConfig config,
        string schemaContext,
        IReadOnlyList<ChatMessage> history,
        CancellationToken ct = default);

    IAsyncEnumerable<string> ChatStreamAsync(
        AiProviderConfig config,
        string schemaContext,
        IReadOnlyList<ChatMessage> history,
        CancellationToken ct = default);

    Task<(bool Success, string? Error)> TestConnectionAsync(AiProviderConfig config, CancellationToken ct = default);

    Task<string> GenerateJsonAsync(AiProviderConfig config, string prompt, CancellationToken ct = default);
}

public sealed class ChatProviderService : IChatProviderService
{
    private readonly Dictionary<string, IChatProvider> _providers;

    public ChatProviderService(IEnumerable<IChatProvider> providers)
    {
        _providers = providers.ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);
    }

    public Task<string> ChatAsync(AiProviderConfig config, string schemaContext, IReadOnlyList<ChatMessage> history, CancellationToken ct = default) =>
        Resolve(config).ChatAsync(config, schemaContext, history, ct);

    public async IAsyncEnumerable<string> ChatStreamAsync(
        AiProviderConfig config,
        string schemaContext,
        IReadOnlyList<ChatMessage> history,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var provider = Resolve(config);
        await foreach (var chunk in provider.ChatStreamAsync(config, schemaContext, history, ct))
        {
            yield return chunk;
        }
    }

    public Task<(bool Success, string? Error)> TestConnectionAsync(AiProviderConfig config, CancellationToken ct = default) =>
        Resolve(config).TestConnectionAsync(config, ct);

    public Task<string> GenerateJsonAsync(AiProviderConfig config, string prompt, CancellationToken ct = default) =>
        Resolve(config).GenerateJsonAsync(config, prompt, ct);

    private IChatProvider Resolve(AiProviderConfig config)
    {
        if (_providers.TryGetValue(config.Provider ?? string.Empty, out var provider))
            return provider;

        throw new InvalidOperationException($"Unknown AI provider '{config.Provider}'.");
    }
}
