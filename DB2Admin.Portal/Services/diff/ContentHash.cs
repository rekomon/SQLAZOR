using System.Security.Cryptography;
using System.Text;

namespace SQLAZOR.Services;

public static class ContentHash
{
    /// <summary>SHA-256 of the file text with line endings normalized, so CRLF round-trips don't look like edits.</summary>
    public static string Compute(string? content)
    {
        var normalized = (content ?? string.Empty).Replace("\r\n", "\n");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }
}
