using System.Text.Json.Nodes;

namespace Ignixa.TestScript.Parsing;

internal static class JsonFieldReader
{
    public static string? GetString(JsonObject obj, string field, string path, List<ParseError> errors)
    {
        if (obj[field] is not JsonNode node)
            return null;

        if (node is JsonValue value && value.TryGetValue<string>(out var str))
            return str;

        errors.Add(new ParseError(ParseSeverity.Error,
            $"Field '{field}' must be a string but was '{DescribeKind(node)}'", $"{path}.{field}"));
        return null;
    }

    public static bool? GetBool(JsonObject obj, string field, string path, List<ParseError> errors)
    {
        if (obj[field] is not JsonNode node)
            return null;

        if (node is JsonValue value && value.TryGetValue<bool>(out var b))
            return b;

        errors.Add(new ParseError(ParseSeverity.Error,
            $"Field '{field}' must be a boolean but was '{DescribeKind(node)}'", $"{path}.{field}"));
        return null;
    }

    public static int? GetInt(JsonObject obj, string field, string path, List<ParseError> errors)
    {
        if (obj[field] is not JsonNode node)
            return null;

        if (node is JsonValue value && value.TryGetValue<int>(out var i))
            return i;

        errors.Add(new ParseError(ParseSeverity.Error,
            $"Field '{field}' must be an integer but was '{DescribeKind(node)}'", $"{path}.{field}"));
        return null;
    }

    private static string DescribeKind(JsonNode node) => node switch
    {
        JsonObject => "object",
        JsonArray => "array",
        JsonValue => "value of a different type",
        _ => "unknown"
    };
}
