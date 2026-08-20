/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * The reference-resolution rule both engines are given, so resolve() is compared and not the fixture.
 */

namespace Ignixa.FhirPath.Tests.Evaluation.Parity;

/// <summary>
/// Turns a FHIR reference string into the JSON of a minimal target resource.
/// </summary>
/// <remarks>
/// <c>resolve()</c> is only meaningful if something answers it, and it is worth answering: it appears
/// 76 times across the shipped R4/R4B/R5 search parameters, so an engine that resolves differently
/// changes reference-index content on every write. Synthesising the target from the reference's own
/// type prefix keeps the rule identical on both sides while still letting <c>resolve() is Patient</c>
/// and <c>resolve().ofType(Organization)</c> discriminate.
/// </remarks>
internal static class ParityReferenceResolver
{
    public static string? SynthesiseTarget(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return null;
        }

        var withoutFragment = reference.Split('#')[0];
        var segments = withoutFragment.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length < 2)
        {
            return null;
        }

        var type = segments[^2];
        var id = segments[^1];

        if (!IsPlausibleResourceType(type))
        {
            return null;
        }

        return $$"""{"resourceType":"{{type}}","id":"{{id}}"}""";
    }

    private static bool IsPlausibleResourceType(string candidate) =>
        candidate.Length > 1
        && char.IsUpper(candidate[0])
        && candidate.All(char.IsLetter);
}
