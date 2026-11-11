namespace SQLAZOR.Models;

/// <summary>
/// Snapshot of the last generator output for one database. Only hashes are kept, so the file stays
/// small and can tell apart "the generator changed this" from "the user edited this".
/// </summary>
public sealed class GenerationManifest
{
    public string DatabaseKey { get; set; } = string.Empty;
    public string DatabaseName { get; set; } = string.Empty;
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.Now;
    public List<GeneratedFileSnapshot> Files { get; set; } = [];
}

public sealed class GeneratedFileSnapshot
{
    public required string RelativePath { get; set; }
    public required string Sha256 { get; set; }
}
