using System.Text.Json;
using System.Text.RegularExpressions;

namespace McServerManager.Services.Templates;

internal sealed class TemplateManifestException(string code, string path, string message) : Exception(message)
{
    public string Code { get; } = code;
    public string Path { get; } = path;
}

internal static class TemplateJsonFields
{
    public static void Object(JsonElement value, string path, string[] allowed, string[] required)
    {
        if (value.ValueKind != JsonValueKind.Object)
            Fail("InvalidType", path, "Expected an object.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!seen.Add(property.Name))
                Fail("DuplicateProperty", path, "Duplicate JSON properties are not permitted.");
            if (!allowed.Contains(property.Name, StringComparer.Ordinal))
                Fail("UnknownProperty", path, "An unknown property is not permitted here.");
        }
        foreach (var name in required)
            if (!seen.Contains(name))
                Fail("MissingProperty", path + "." + name, "A required property is missing.");
    }

    public static string Text(JsonElement value, string path, int minLength, int maxLength, string? pattern = null)
    {
        if (value.ValueKind != JsonValueKind.String)
            Fail("InvalidType", path, "Expected a string.");
        string text;
        try
        {
            text = value.GetString()!;
        }
        catch (InvalidOperationException)
        {
            Fail("InvalidUnicode", path, "Invalid Unicode escape sequence.");
            throw;
        }
        // Count Unicode scalar values as JSON Schema does, rejecting unpaired UTF-16 escapes.
        var scalarLength = 0;
        for (var index = 0; index < text.Length; index++, scalarLength++)
        {
            if (char.IsHighSurrogate(text[index]))
            {
                if (++index >= text.Length || !char.IsLowSurrogate(text[index]))
                    Fail("InvalidUnicode", path, "Unpaired Unicode surrogate.");
            }
            else if (char.IsLowSurrogate(text[index]))
                Fail("InvalidUnicode", path, "Unpaired Unicode surrogate.");
        }
        if (scalarLength < minLength || scalarLength > maxLength)
            Fail("InvalidLength", path, $"The string length must be between {minLength} and {maxLength}.");
        if (pattern is not null && !Regex.IsMatch(text, pattern, RegexOptions.CultureInvariant | RegexOptions.NonBacktracking))
            Fail("InvalidValue", path, "The value does not match the required fixed format.");
        return text;
    }

    public static string Choice(JsonElement value, string path, params string[] choices)
    {
        var text = Text(value, path, 1, 96);
        if (!choices.Contains(text, StringComparer.Ordinal))
            Fail("InvalidValue", path, "The value is not supported by schema 1.0.");
        return text;
    }

    public static long Integer(JsonElement value, string path, long minimum, long maximum)
    {
        if (value.ValueKind != JsonValueKind.Number)
            Fail("InvalidType", path, "Expected an integer.");
        // Integer JSON tokens only: no loss-prone floating point conversion or rounding.
        if (!value.TryGetInt64(out var number) || number < minimum || number > maximum)
            Fail("InvalidValue", path, $"Expected an integer between {minimum} and {maximum}.");
        return number;
    }

    public static bool Boolean(JsonElement value, string path)
    {
        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            Fail("InvalidType", path, "Expected a boolean.");
        return value.GetBoolean();
    }

    public static void Array(JsonElement value, string path, int maximum)
    {
        if (value.ValueKind != JsonValueKind.Array)
            Fail("InvalidType", path, "Expected an array.");
        if (value.GetArrayLength() > maximum)
            Fail("LimitExceeded", path, $"The array exceeds the limit of {maximum} items.");
    }

    public static void Fail(string code, string path, string message) => throw new TemplateManifestException(code, path, message);
}
