// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;

namespace Ignixa.CliOnFhir.Terminology;

/// <summary>Reads a ConceptMap resource, R4 (<c>equivalence</c>, <c>source[x]</c>) or R5 (<c>relationship</c>, <c>sourceScope[x]</c>).</summary>
internal static class ConceptMapReader
{
    public static (ConceptMapEntry? Entry, string? Problem) Read(JsonElement resource, ResourceOrigin origin)
    {
        ArgumentNullException.ThrowIfNull(origin);

        if (TerminologyJson.NonEmpty(resource, "url") is not { } url)
        {
            return (null, "has no url");
        }

        return (new ConceptMapEntry(
            url,
            TerminologyJson.NonEmpty(resource, "version"),
            FirstOf(resource, "sourceUri", "sourceCanonical", "sourceScopeUri", "sourceScopeCanonical"),
            FirstOf(resource, "targetUri", "targetCanonical", "targetScopeUri", "targetScopeCanonical"),
            [.. TerminologyJson.Array(resource, "group").Select(Group)],
            origin,
            TerminologyJson.Fingerprint(TerminologyJson.Member(resource, "group"))), null);
    }

    private static string? FirstOf(JsonElement resource, params string[] names) =>
        names.Select(name => TerminologyJson.NonEmpty(resource, name)).FirstOrDefault(value => value is not null);

    private static ConceptMapGroup Group(JsonElement group)
    {
        List<ConceptMapElement> elements = [];
        foreach (JsonElement element in TerminologyJson.Array(group, "element"))
        {
            if (TerminologyJson.NonEmpty(element, "code") is not { } code)
            {
                continue;
            }

            elements.Add(new ConceptMapElement(
                code,
                TerminologyJson.Boolean(element, "noMap") == true,
                [.. TerminologyJson.Array(element, "target").Select(target => new ConceptMapTarget(
                    TerminologyJson.NonEmpty(target, "code"),
                    TerminologyJson.String(target, "display"),
                    TerminologyJson.NonEmpty(target, "equivalence"),
                    TerminologyJson.NonEmpty(target, "relationship"),
                    TerminologyJson.String(target, "comment"),
                    TerminologyJson.Array(target, "dependsOn").Any() || TerminologyJson.Array(target, "product").Any()))]));
        }

        ConceptMapUnmapped? unmapped = TerminologyJson.Object(group, "unmapped") is { } rule && TerminologyJson.NonEmpty(rule, "mode") is { } mode
            ? new ConceptMapUnmapped(mode, TerminologyJson.NonEmpty(rule, "code"), TerminologyJson.String(rule, "display"), TerminologyJson.NonEmpty(rule, "url") ?? TerminologyJson.NonEmpty(rule, "otherMap"))
            : null;

        return new ConceptMapGroup(
            TerminologyJson.NonEmpty(group, "source"),
            TerminologyJson.NonEmpty(group, "sourceVersion"),
            TerminologyJson.NonEmpty(group, "target"),
            TerminologyJson.NonEmpty(group, "targetVersion"),
            elements,
            unmapped);
    }
}
