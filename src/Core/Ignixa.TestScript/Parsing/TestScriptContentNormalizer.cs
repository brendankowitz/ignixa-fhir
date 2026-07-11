using System.Text.Json.Nodes;

namespace Ignixa.TestScript.Parsing;

/// <summary>
/// Rewrites recognized Ignixa authoring shorthands into their canonical
/// <c>http://ignixa.io/testscript/*</c> extension form before typed parsing.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="TestScriptParser"/> applies this normalization automatically, so most callers
/// never need to invoke it directly. It is exposed publicly so hosts that build their own JSON
/// pipeline (e.g. merging fragments, or normalizing ahead of validation) can apply the same
/// rewrite without duplicating the shorthand contract.
/// </para>
/// <para>
/// Currently recognized shorthand: a direct <c>requiresCapability</c> string property, valid at
/// the <c>TestScript</c> root and on each <c>TestScript.test</c> entry, as an authoring
/// convenience for the canonical
/// <see href="https://github.com/brendankowitz/ignixa-fhir/blob/main/docs/adr/adr-2607-testscript-extensions.md">
/// <c>http://ignixa.io/testscript/requiresCapability</c></see> extension. Supplying both forms
/// is only valid when they carry the identical expression; supplying a malformed shorthand
/// (non-string), conflicting values for both forms, or a canonical extension whose
/// <c>valueString</c> is missing or not a string raises
/// <see cref="TestScriptNormalizationException"/>. All other properties — including unrelated
/// extensions, regardless of their <c>url</c> value type — pass through untouched, preserving
/// permissive parsing.
/// </para>
/// </remarks>
public static class TestScriptContentNormalizer
{
    public const string RequiresCapabilityUrl = "http://ignixa.io/testscript/requiresCapability";

    private const string RequiresCapabilityShorthand = "requiresCapability";

    /// <summary>
    /// Returns a normalized deep copy of <paramref name="root"/> with recognized shorthands
    /// rewritten to their canonical extension form. The supplied node is never mutated.
    /// </summary>
    /// <exception cref="TestScriptNormalizationException">
    /// A recognized shorthand is malformed, conflicts with an equivalent canonical extension
    /// already present on the same object, or that canonical extension's <c>valueString</c> is
    /// missing or not a string.
    /// </exception>
    public static JsonObject Normalize(JsonObject root)
    {
        ArgumentNullException.ThrowIfNull(root);

        var clone = root.DeepClone().AsObject();

        NormalizeRequiresCapability(clone, "$");

        if (clone["test"] is JsonArray tests)
        {
            for (var i = 0; i < tests.Count; i++)
            {
                if (tests[i] is JsonObject test)
                    NormalizeRequiresCapability(test, $"test[{i}]");
            }
        }

        return clone;
    }

    private static void NormalizeRequiresCapability(JsonObject node, string path)
    {
        if (node[RequiresCapabilityShorthand] is not JsonNode shorthandNode)
            return;

        if (shorthandNode is not JsonValue shorthandJsonValue || !shorthandJsonValue.TryGetValue<string>(out var shorthand))
            throw new TestScriptNormalizationException(
                $"'{path}.{RequiresCapabilityShorthand}' shorthand must be a string.");

        var canonical = FindRequiresCapabilityExtension(node["extension"] as JsonArray);
        if (canonical is not null)
        {
            if (canonical["valueString"] is not JsonValue canonicalJsonValue || !canonicalJsonValue.TryGetValue<string>(out var canonicalValue))
                throw new TestScriptNormalizationException(
                    $"'{path}' has a canonical '{RequiresCapabilityUrl}' extension with a missing or " +
                    "non-string 'valueString', so it cannot be reconciled with the requiresCapability shorthand.");

            if (!string.Equals(canonicalValue, shorthand, StringComparison.Ordinal))
                throw new TestScriptNormalizationException(
                    $"'{path}' declares conflicting requiresCapability values: shorthand is " +
                    $"'{shorthand}' but the canonical '{RequiresCapabilityUrl}' extension is '{canonicalValue}'.");

            node.Remove(RequiresCapabilityShorthand);
            return;
        }

        node.Remove(RequiresCapabilityShorthand);

        var extensions = node["extension"] as JsonArray;
        if (extensions is null)
        {
            extensions = [];
            node["extension"] = extensions;
        }

        extensions.Add(new JsonObject { ["url"] = RequiresCapabilityUrl, ["valueString"] = shorthand });
    }

    private static JsonObject? FindRequiresCapabilityExtension(JsonArray? extensions)
    {
        if (extensions is null) return null;
        foreach (var ext in extensions)
        {
            if (ext is JsonObject obj &&
                obj["url"] is JsonValue urlValue &&
                urlValue.TryGetValue<string>(out var url) &&
                url == RequiresCapabilityUrl)
                return obj;
        }
        return null;
    }
}
