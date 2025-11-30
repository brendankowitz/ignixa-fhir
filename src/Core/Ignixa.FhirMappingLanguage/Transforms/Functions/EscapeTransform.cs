/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Standard transform functions for FHIR Mapping Language.
 * Implements the built-in transforms defined in the FHIR specification.
 */

namespace Ignixa.FhirMappingLanguage.Transforms;

/// <summary>
/// escape(source, format) - Escapes a string for the specified format (url, json, xml).
/// </summary>
internal class EscapeTransform : ITransformFunction
{
    public string Name => "escape";

    public object Execute(IReadOnlyList<object> arguments, ITransformContext context)
    {
        if (arguments.Count < 2)
            throw new ArgumentException("escape() requires source and format arguments");

        var source = arguments[0].ToString() ?? string.Empty;
#pragma warning disable CA1308 // Normalize strings to uppercase - FHIR format names are lowercase by convention
        var format = arguments[1].ToString()?.ToLowerInvariant() ?? "url";
#pragma warning restore CA1308

        return format switch
        {
            "url" => Uri.EscapeDataString(source),
            "json" => EscapeJson(source),
            "xml" => System.Security.SecurityElement.Escape(source) ?? source,
            "html" => System.Security.SecurityElement.Escape(source) ?? source,
            _ => throw new ArgumentException($"Unknown escape format: {format}")
        };
    }

    private static string EscapeJson(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\r", StringComparison.Ordinal)
            .Replace("\t", "\t", StringComparison.Ordinal);
    }
}
