// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;

namespace Ignixa.CliOnFhir.Terminology;

/// <summary>Reads a ValueSet resource's compose rules and any stored expansion.</summary>
internal static class ValueSetReader
{
    public static (ValueSetEntry? Entry, string? Problem) Read(JsonElement resource, ResourceOrigin origin)
    {
        ArgumentNullException.ThrowIfNull(origin);

        if (TerminologyJson.NonEmpty(resource, "url") is not { } url)
        {
            return (null, "has no url");
        }

        JsonElement? compose = TerminologyJson.Object(resource, "compose");
        JsonElement? expansion = TerminologyJson.Object(resource, "expansion");
        List<ExpansionCode> stored = [];
        if (expansion is { } expanded)
        {
            Flatten(TerminologyJson.Array(expanded, "contains"), stored);
        }

        return (new ValueSetEntry(
            url,
            TerminologyJson.NonEmpty(resource, "version"),
            compose is { } c ? [.. TerminologyJson.Array(c, "include").Select(Rule)] : [],
            compose is { } d ? [.. TerminologyJson.Array(d, "exclude").Select(Rule)] : [],
            compose is { } e ? TerminologyJson.Boolean(e, "inactive") : null,
            compose is not null,
            stored,
            origin,
            TerminologyJson.Fingerprint(compose, expansion is { } x ? TerminologyJson.Member(x, "contains") : null),
            TerminologyJson.VersionAlgorithm(resource)), null);
    }

    private static ValueSetRule Rule(JsonElement rule) => new(
        TerminologyJson.NonEmpty(rule, "system"),
        TerminologyJson.NonEmpty(rule, "version"),
        [.. TerminologyJson.Array(rule, "concept")
            .Select(concept => (Code: TerminologyJson.NonEmpty(concept, "code"), Display: TerminologyJson.String(concept, "display")))
            .Where(concept => concept.Code is not null)
            .Select(concept => new ValueSetRuleConcept(concept.Code!, concept.Display))],
        [.. TerminologyJson.Array(rule, "filter").Select(filter => new ValueSetFilter(
            TerminologyJson.NonEmpty(filter, "property"),
            TerminologyJson.NonEmpty(filter, "op"),
            TerminologyJson.String(filter, "value")))],
        [.. TerminologyJson.Array(rule, "valueSet").Where(value => value.ValueKind == JsonValueKind.String).Select(value => value.GetString()!).Where(value => value.Length > 0)]);

    private static void Flatten(IEnumerable<JsonElement> contains, List<ExpansionCode> codes)
    {
        foreach (JsonElement item in contains)
        {
            if (TerminologyJson.NonEmpty(item, "system") is { } system && TerminologyJson.NonEmpty(item, "code") is { } code)
            {
                codes.Add(new ExpansionCode(system, TerminologyJson.NonEmpty(item, "version"), code, TerminologyJson.String(item, "display"), TerminologyJson.Boolean(item, "inactive")));
            }

            Flatten(TerminologyJson.Array(item, "contains"), codes);
        }
    }
}
