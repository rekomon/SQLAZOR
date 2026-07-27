using SQLAZOR.Models;
using System;

namespace SQLAZOR.Services
{
    public sealed class GenerationState
    {
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
        }
        public DatabaseSchema? Schema { get; set; }
        public HashSet<string> SelectedTableKeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> SelectedViewKeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public List<StoredProcedureSummary> Procedures { get; set; } = [];
        public HashSet<string> SelectedProcedureKeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);



        public List<GeneratedFile> GeneratedFiles { get; set; } = [];
        public string RootNamespace { get; set; } = "MyApp.Data";
        public string DbContextName { get; set; } = "AppDbContext";
        public string ApplicationName { get; set; } = "GeneratedApp";


     

        // AI schema assistant (Ollama)
        public string OllamaEndpoint { get; set; } = "http://localhost:11434";
        public string OllamaModel { get; set; } = "gpt-oss:120b-cloud";
        public List<ChatMessage> ChatHistory { get; set; } = [];

        /// <summary>Results of "Run query" clicks on SQL code blocks in the chat, keyed by "{messageIndex}:{blockIndex}".</summary>
        public Dictionary<string, SqlQueryRunResult> ChatQueryResults { get; set; } = new(StringComparer.Ordinal);

        // AI naming/documentation suggestions
        public Dictionary<string, TableNamingSuggestion> NamingSuggestions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> AppliedNamingTableKeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        // Implicit relationship detection
        public List<ImplicitForeignKeyCandidate> ImplicitFkCandidates { get; set; } = [];
        public HashSet<string> AcceptedImplicitFkKeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        // Dashboard insights (AI-suggested charts)
        public List<DashboardInsightCandidate> DashboardInsights { get; set; } = [];
        public HashSet<string> AcceptedInsightTitles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
