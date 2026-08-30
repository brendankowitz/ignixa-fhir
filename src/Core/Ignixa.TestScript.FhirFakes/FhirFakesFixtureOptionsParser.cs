using System.Globalization;
using System.Text.Json.Nodes;
using Ignixa.FhirFakes;
using Ignixa.TestScript.Model;

namespace Ignixa.TestScript.FhirFakes;

internal static class FhirFakesFixtureOptionsParser
{
    public const string LegacyExtensionUrl = "http://ignixa.io/testscript/fhirfakes";
    public const string CanonicalExtensionUrl = "http://ignixa.io/fhir/StructureDefinition/testscript-fhirfakes";

    public static FhirFakesFixtureOptions? Parse(FixtureDefinition fixture)
    {
        var node = fixture.Resource?.MutableNode;
        if (node is null) return null;

        var extensions = node["extension"]?.AsArray();
        if (extensions is null) return null;

        foreach (var ext in extensions)
        {
            if (ext is not JsonObject extObj) continue;
            if (!IsFhirFakesExtension(extObj)) continue;

            return ParseExtension(extObj);
        }

        return null;
    }

    private static FhirFakesFixtureOptions? ParseExtension(JsonObject extension)
    {
        var legacyResourceType = GetString(extension, "valueCode");
        var nested = extension["extension"]?.AsArray();
        if (nested is null)
        {
            return string.IsNullOrWhiteSpace(legacyResourceType)
                ? null
                : new FhirFakesFixtureOptions { ResourceType = legacyResourceType };
        }

        var resourceType = GetNestedString(nested, "resourceType", "valueCode") ?? legacyResourceType;
        if (string.IsNullOrWhiteSpace(resourceType))
        {
            return null;
        }

        return new FhirFakesFixtureOptions
        {
            ResourceType = resourceType,
            Seed = GetNestedInt(nested, "seed"),
            Density = ParseDensity(GetNestedString(nested, "density", "valueCode")),
            Theme = ParseTheme(GetNestedString(nested, "theme", "valueCode") ?? GetNestedCodingCode(nested, "theme")),
            Profile = GetNestedString(nested, "profile", "valueCanonical"),
            Tag = GetNestedString(nested, "tag", "valueString"),
            Patient = ParsePatient(nested),
            EdgeCases = ParseEdgeCases(nested),
        };
    }

    private static FhirFakesPatientOptions? ParsePatient(JsonArray extensions)
    {
        var patient = GetNestedExtensions(extensions, "patient");
        if (patient is null) return null;

        var identifiers = patient
            .Where(ext => ext is JsonObject obj && GetString(obj, "url") == "identifier")
            .OfType<JsonObject>()
            .Select(ParseIdentifier)
            .Where(identifier => identifier is not null)
            .Cast<FhirFakesIdentifierOptions>()
            .ToList();

        return new FhirFakesPatientOptions
        {
            GivenName = GetNestedString(patient, "givenName", "valueString"),
            FamilyName = GetNestedString(patient, "familyName", "valueString"),
            Gender = GetNestedString(patient, "gender", "valueCode"),
            Age = GetNestedInt(patient, "age"),
            BirthDate = GetNestedString(patient, "birthDate", "valueDate"),
            City = GetNestedString(patient, "city", "valueString"),
            State = GetNestedString(patient, "state", "valueString"),
            ZipCode = GetNestedString(patient, "zipCode", "valueString"),
            Active = GetNestedBool(patient, "active"),
            Bmi = GetNestedDecimal(patient, "bmi"),
            Identifiers = identifiers,
        };
    }

    private static FhirFakesIdentifierOptions? ParseIdentifier(JsonObject extension)
    {
        var nested = extension["extension"]?.AsArray();
        if (nested is null) return null;

        var value = GetNestedString(nested, "value", "valueString");
        if (string.IsNullOrWhiteSpace(value)) return null;

        return new FhirFakesIdentifierOptions
        {
            System = GetNestedString(nested, "system", "valueUri"),
            Value = value,
        };
    }

    private static FhirFakesEdgeCaseOptions? ParseEdgeCases(JsonArray extensions)
    {
        var edgeCaseExtensions = extensions
            .Where(ext => ext is JsonObject obj && GetString(obj, "url") == "edgeCase")
            .OfType<JsonObject>()
            .ToList();

        if (edgeCaseExtensions.Count == 0) return null;

        int? seed = null;
        var selectors = new List<string>();
        foreach (var edgeCase in edgeCaseExtensions)
        {
            var nested = edgeCase["extension"]?.AsArray();
            if (nested is null) continue;

            seed ??= GetNestedInt(nested, "seed");
            selectors.AddRange(GetNestedStrings(nested, "selector", "valueCode"));
        }

        return new FhirFakesEdgeCaseOptions
        {
            Seed = seed,
            Selectors = selectors,
        };
    }

    private static bool IsFhirFakesExtension(JsonObject extension)
    {
        var url = GetString(extension, "url");
        return url is LegacyExtensionUrl or CanonicalExtensionUrl;
    }

    private static JsonArray? GetNestedExtensions(JsonArray extensions, string url)
    {
        foreach (var ext in extensions)
        {
            if (ext is not JsonObject obj) continue;
            if (GetString(obj, "url") == url)
                return obj["extension"]?.AsArray();
        }

        return null;
    }

    private static string? GetNestedString(JsonArray extensions, string url, string valueName)
    {
        foreach (var ext in extensions)
        {
            if (ext is not JsonObject obj) continue;
            if (GetString(obj, "url") == url)
                return GetString(obj, valueName);
        }

        return null;
    }

    private static IReadOnlyList<string> GetNestedStrings(JsonArray extensions, string url, string valueName)
    {
        var values = new List<string>();
        foreach (var ext in extensions)
        {
            if (ext is not JsonObject obj) continue;
            if (GetString(obj, "url") != url) continue;

            if (GetString(obj, valueName) is { } value)
                values.Add(value);
        }

        return values;
    }

    private static int? GetNestedInt(JsonArray extensions, string url)
    {
        foreach (var ext in extensions)
        {
            if (ext is not JsonObject obj) continue;
            if (GetString(obj, "url") != url) continue;

            return GetInt(obj, "valueInteger");
        }

        return null;
    }

    private static bool? GetNestedBool(JsonArray extensions, string url)
    {
        foreach (var ext in extensions)
        {
            if (ext is not JsonObject obj) continue;
            if (GetString(obj, "url") != url) continue;

            return GetBool(obj, "valueBoolean");
        }

        return null;
    }

    private static decimal? GetNestedDecimal(JsonArray extensions, string url)
    {
        foreach (var ext in extensions)
        {
            if (ext is not JsonObject obj) continue;
            if (GetString(obj, "url") != url) continue;

            return GetDecimal(obj, "valueDecimal");
        }

        return null;
    }

    private static string? GetNestedCodingCode(JsonArray extensions, string url)
    {
        foreach (var ext in extensions)
        {
            if (ext is not JsonObject obj) continue;
            if (GetString(obj, "url") != url) continue;

            return obj["valueCoding"]?["code"]?.GetValue<string>();
        }

        return null;
    }

    private static string? GetString(JsonObject obj, string name)
        => obj[name]?.GetValue<string>();

    private static int? GetInt(JsonObject obj, string name)
        => obj[name] is JsonValue value && value.TryGetValue<int>(out var result) ? result : null;

    private static bool? GetBool(JsonObject obj, string name)
        => obj[name] is JsonValue value && value.TryGetValue<bool>(out var result) ? result : null;

    private static decimal? GetDecimal(JsonObject obj, string name)
        => obj[name] is JsonValue value && value.TryGetValue<decimal>(out var result) ? result : null;

    private static GenerationDensity? ParseDensity(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        return Enum.TryParse<GenerationDensity>(value, ignoreCase: true, out var density)
            ? density
            : throw new FormatException($"Unsupported FhirFakes density '{value}'.");
    }

    private static ClinicalDomain? ParseTheme(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var normalized = value.Replace("-", string.Empty, StringComparison.Ordinal);
        return Enum.TryParse<ClinicalDomain>(normalized, ignoreCase: true, out var theme)
            ? theme
            : throw new FormatException($"Unsupported FhirFakes theme '{value}'.");
    }
}
