using System.Text;
using System.Text.RegularExpressions;

namespace SQLAZOR.Services;

/// <summary>
/// Converts raw SQL Server identifiers and types into idiomatic C# equivalents.
/// Kept dependency-free and static so both the reader and generator can share it.
/// </summary>
public static partial class NamingHelper
{
    // Words that already look singular / shouldn't be de-pluralized (avoid mangling e.g. "Status", "Address").
    private static readonly HashSet<string> UninflectedOrTricky = new(StringComparer.OrdinalIgnoreCase)
    {
        "status", "address", "series", "species", "news", "data", "settings", "analysis"
    };

    /// <summary>
    /// Maps a raw SQL Server type name to a C# type, applying nullability.
    /// </summary>
    public static string MapSqlTypeToClr(string sqlType, bool isNullable, byte precision = 0, byte scale = 0)
    {
        string clr = sqlType.ToLowerInvariant() switch
        {
            "bigint" => "long",
            "binary" or "varbinary" or "image" or "rowversion" or "timestamp" => "byte[]",
            "bit" => "bool",
            "char" or "varchar" or "nchar" or "nvarchar" or "text" or "ntext" or "xml" => "string",
            "date" or "datetime" or "datetime2" or "smalldatetime" => "DateTime",
            "datetimeoffset" => "DateTimeOffset",
            "decimal" or "numeric" or "money" or "smallmoney" => "decimal",
            "float" => "double",
            "real" => "float",
            "int" => "int",
            "smallint" => "short",
            "tinyint" => "byte",
            "time" => "TimeSpan",
            "uniqueidentifier" => "Guid",
            "sql_variant" => "object",
            _ => "string" // safe fallback for unknown/user-defined types
        };

        // Reference types (string, byte[], object) are nullable via '?' too when the column allows NULL,
        // for consistent nullable-annotation hygiene (and so EF's nullability convention lines up 1:1).
        if (isNullable)
        {
            return clr + "?";
        }

        return clr;
    }

    /// <summary>
    /// Converts a snake_case, PascalCase-already, or mixed DB identifier into clean PascalCase.
    /// "customer_id" -> "CustomerId", "CustomerID" -> "CustomerId", "orderLineItems" -> "OrderLineItems".
    /// </summary>
    public static string ToPascalCase(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            return identifier;

        // Split on underscores, spaces, and camel/Pascal boundaries.
        var parts = SplitWordsRegex().Split(identifier)
            .Where(p => p.Length > 0)
            .ToList();

        if (parts.Count == 0)
            return identifier;

        var sb = new StringBuilder();
        foreach (var part in parts)
        {
            sb.Append(NormalizeWord(part));
        }

        var result = sb.ToString();

        // Guard against identifiers starting with a digit (invalid in C#).
        if (result.Length > 0 && char.IsDigit(result[0]))
            result = "_" + result;

        return result;
    }

    private static string NormalizeWord(string word)
    {
        // Special-case common all-caps abbreviations so "ID" -> "Id", "URL" -> "Url", not "Id" -> "I", "D".
        if (word.Equals("id", StringComparison.OrdinalIgnoreCase)) return "Id";
        if (word.Equals("url", StringComparison.OrdinalIgnoreCase)) return "Url";
        if (word.Equals("guid", StringComparison.OrdinalIgnoreCase)) return "Guid";

        if (word.Length == 1)
            return word.ToUpperInvariant();

        return char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant();
    }

    [GeneratedRegex(@"[_\-\s]+|(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])")]
    private static partial Regex SplitWordsRegex();

    /// <summary>
    /// Best-effort singularization for turning a table name into a class name
    /// ("Customers" -> "Customer", "Categories" -> "Category", "Status" -> "Status").
    /// </summary>
    public static string Singularize(string pascalCaseWord)
    {
        if (string.IsNullOrEmpty(pascalCaseWord))
            return pascalCaseWord;

        foreach (var tricky in UninflectedOrTricky)
        {
            if (pascalCaseWord.Equals(tricky, StringComparison.OrdinalIgnoreCase))
                return pascalCaseWord;
        }

        // Irregular plural -> singular lookup (checks whole word or trailing compound, e.g. "Salespeople").
        var lower = pascalCaseWord.ToLowerInvariant();
        if (lower.EndsWith("people"))
            return pascalCaseWord[..^6] + "Person";
        if (lower.EndsWith("children"))
            return pascalCaseWord[..^3];
        if (lower.EndsWith("men") && !lower.EndsWith("umen"))
            return pascalCaseWord[..^3] + "man";
        if (lower.EndsWith("teeth"))
            return pascalCaseWord[..^5] + "Tooth";
        if (lower.EndsWith("mice"))
            return pascalCaseWord[..^4] + "Mouse";
        if (lower.EndsWith("geese"))
            return pascalCaseWord[..^5] + "Goose";

        if (lower.EndsWith("ies") && pascalCaseWord.Length > 3)
            return pascalCaseWord[..^3] + "y";

        if (lower.EndsWith("ses") || lower.EndsWith("xes") || lower.EndsWith("zes")
            || lower.EndsWith("ches") || lower.EndsWith("shes"))
            return pascalCaseWord[..^2];

        if (lower.EndsWith("s") && !lower.EndsWith("ss") && pascalCaseWord.Length > 1)
            return pascalCaseWord[..^1];

        return pascalCaseWord;
    }

    /// <summary>
    /// Pluralizes a PascalCase word for use as a collection navigation property name
    /// ("Order" -> "Orders", "Category" -> "Categories", "Person" -> "People").
    /// </summary>
    public static string Pluralize(string pascalCaseWord)
    {
        if (string.IsNullOrEmpty(pascalCaseWord))
            return pascalCaseWord;

        var lower = pascalCaseWord.ToLowerInvariant();

        if (lower.EndsWith("person"))
            return pascalCaseWord[..^6] + "People";
        if (lower == "child")
            return pascalCaseWord + "ren";
        if (lower == "man")
            return pascalCaseWord[..^2] + "en";
        if (lower == "tooth")
            return pascalCaseWord[..^5] + "Teeth";
        if (lower == "mouse")
            return pascalCaseWord[..^5] + "Mice";
        if (lower == "goose")
            return pascalCaseWord[..^5] + "Geese";

        if (lower.EndsWith("y") && lower.Length > 1 && !"aeiou".Contains(lower[^2]))
            return pascalCaseWord[..^1] + "ies";

        if (lower.EndsWith("s") || lower.EndsWith("x") || lower.EndsWith("z")
            || lower.EndsWith("ch") || lower.EndsWith("sh"))
            return pascalCaseWord + "es";

        return pascalCaseWord + "s";
    }

    /// <summary>Escapes a name if it happens to collide with a C# reserved keyword.</summary>
    public static string EscapeIfReserved(string identifier)
    {
        return ReservedWords.Contains(identifier) ? "@" + identifier : identifier;
    }

    private static readonly HashSet<string> ReservedWords = new()
    {
        "abstract","as","base","bool","break","byte","case","catch","char","checked","class","const",
        "continue","decimal","default","delegate","do","double","else","enum","event","explicit","extern",
        "false","finally","fixed","float","for","foreach","goto","if","implicit","in","int","interface",
        "internal","is","lock","long","namespace","new","null","object","operator","out","override","params",
        "private","protected","public","readonly","ref","return","sbyte","sealed","short","sizeof","stackalloc",
        "static","string","struct","switch","this","throw","true","try","typeof","uint","ulong","unchecked",
        "unsafe","ushort","using","virtual","void","volatile","while"
    };
}
