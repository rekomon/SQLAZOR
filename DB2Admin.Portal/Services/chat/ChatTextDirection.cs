using System.Text;

namespace SQLAZOR.Services;

/// <summary>
/// Detects Arabic replies so the chat can flip that message to a right-to-left layout.
/// Fenced code is ignored when measuring, because a long SQL or C# block would otherwise
/// drown out the Arabic prose around it.
/// </summary>
public static class ChatTextDirection
{
    private const int ArabicSharePercent = 40;

    public static bool IsRtl(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var arabic = 0;
        var latin = 0;

        foreach (var c in text)
        {
            if (IsArabicLetter(c))
                arabic++;
            else if (c < '\u0080' && char.IsLetter(c))
                latin++;
        }

        var letters = arabic + latin;
        return arabic > 0 && arabic * 100 >= letters * ArabicSharePercent;
    }

    /// <summary>Direction of a whole message, measured on prose only (code fences excluded).</summary>
    public static bool ForMessage(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return false;

        var prose = new StringBuilder(content.Length);
        var parts = content.Split("```");
        for (var i = 0; i < parts.Length; i += 2)
            prose.Append(parts[i]);

        return IsRtl(prose.ToString());
    }

    public static bool IsArabicLetter(char c) =>
        char.IsLetter(c) &&
        ((c >= '\u0600' && c <= '\u06FF') ||   // Arabic
         (c >= '\u0750' && c <= '\u077F') ||   // Arabic Supplement
         (c >= '\u08A0' && c <= '\u08FF') ||   // Arabic Extended-A
         (c >= '\uFB50' && c <= '\uFDFF') ||   // Arabic Presentation Forms-A
         (c >= '\uFE70' && c <= '\uFEFF'));    // Arabic Presentation Forms-B
}
