using SQLAZOR.Models;
using System;

namespace SQLAZOR.Services
{
    public sealed class GenerationState
    {
        private readonly IAiSettingsStore? _aiSettingsStore;

        public GenerationState() : this(null)
        {
        }

        public GenerationState(IAiSettingsStore? aiSettingsStore)
        {
            _aiSettingsStore = aiSettingsStore;

            var snapshot = aiSettingsStore?.Load();
            if (snapshot is not null)
            {
                AiProvider = AiProviderCatalog.Get(snapshot.Provider).Id;
                foreach (var (id, config) in snapshot.Configs)
                {
                    if (config is null || !AiProviderCatalog.TryGet(id, out var info))
                        continue;

                    config.Provider = info.Id;
                    AiConfigs[info.Id] = config;
                }
            }

            AiProviderCatalog.EnsureDefaults(AiConfigs);
        }

        public Connection Conn { get; set; } = new();

        // Event to notify consumers when the state changes
        public event Action? OnChange;

        public void NotifyStateChanged() => OnChange?.Invoke();

        public class Connection {
            public string Server { get; set; } = ".";
            public string userId { get; set; } = string.Empty;
            public string Password { get; set; } = string.Empty;
            public bool TSC { get; set; } = true;
            public string Database { get; set; } = "master";
            public bool IsConnected { get; set; } = false;
            public string ConnectionString => ($"Server={Server};Database={Database};User Id={userId}" +
                $";Password={Password};TrustServerCertificate={TSC.ToString()};");

            /// <summary>Server+database only - used to find saved conversations for the same database. Never includes credentials.</summary>
            public string DatabaseKey => $"{Server.Trim().ToLowerInvariant()}|{Database.Trim().ToLowerInvariant()}";
        }
        public DatabaseSchema? Schema { get; set; }
        public HashSet<string> SelectedTableKeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> SelectedViewKeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public List<StoredProcedureSummary> Procedures { get; set; } = [];
        public HashSet<string> SelectedProcedureKeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);



        public List<GeneratedFile> GeneratedFiles { get; set; } = [];
        public GeneratedCodeValidationReport? Validation { get; set; }
        public GenerationMergePlan? PendingMerge { get; set; }
        public GenerationOptions Options { get; set; } = new();
        public string RootNamespace { get; set; } = "MyApp.Data";
        public string DbContextName { get; set; } = "AppDbContext";
        public string ApplicationName { get; set; } = "GeneratedApp";


     

        // AI providers (Ollama, DeepSeek)
        public string AiProvider { get; set; } = AiProviderCatalog.Ollama;
        public Dictionary<string, AiProviderConfig> AiConfigs { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<ChatMessage> ChatHistory { get; set; } = [];

        /// <summary>Settings for the selected provider. Never contains anything that is written into a chat file.</summary>
        public AiProviderConfig CurrentAiConfig
        {
            get
            {
                var info = AiProviderCatalog.Get(AiProvider);
                if (!AiConfigs.TryGetValue(info.Id, out var config))
                {
                    config = AiProviderCatalog.CreateConfig(info.Id);
                    AiConfigs[info.Id] = config;
                }

                return config;
            }
        }

        public Task SaveAiSettingsAsync(CancellationToken ct = default)
        {
            if (_aiSettingsStore is null)
                return Task.CompletedTask;

            // Copy before handing off: the store serializes on a background continuation while the
            // UI may still be editing the live configs.
            var configs = AiConfigs.ToDictionary(
                pair => pair.Key,
                pair => new AiProviderConfig
                {
                    Provider = pair.Value.Provider,
                    Endpoint = pair.Value.Endpoint,
                    Model = pair.Value.Model,
                    ApiKey = pair.Value.ApiKey
                },
                StringComparer.OrdinalIgnoreCase);

            return _aiSettingsStore.SaveAsync(new AiSettingsSnapshot
            {
                Provider = AiProvider,
                Configs = configs
            }, ct);
        }

        // Saved conversations (see IChatSessionStore)
        public string? CurrentChatSessionId { get; set; }
        public string ChatSessionTitle { get; set; } = "New conversation";
        public List<ChatSessionSummary> ChatSessions { get; set; } = [];
        public List<ChatAttachment> PendingChatAttachments { get; set; } = [];

        /// <summary>Results of "Run query" clicks on SQL code blocks, keyed by "{messageId}:{blockIndex}".</summary>
        public Dictionary<string, SqlQueryRunResult> ChatQueryResults { get; set; } = new(StringComparer.Ordinal);

        // AI naming/documentation suggestions
        public Dictionary<string, TableNamingSuggestion> NamingSuggestions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> AppliedNamingTableKeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        // Implicit relationship detection
        public List<ImplicitForeignKeyCandidate> ImplicitFkCandidates { get; set; } = [];
        public HashSet<string> AcceptedImplicitFkKeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        // AI-suggested lookup dropdowns for non-FK columns
        public List<LookupSuggestion> LookupSuggestions { get; set; } = [];
        public HashSet<string> AcceptedLookupKeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        // Dashboard insights (AI-suggested charts)
        public List<DashboardInsightCandidate> DashboardInsights { get; set; } = [];
        public HashSet<string> AcceptedInsightTitles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
