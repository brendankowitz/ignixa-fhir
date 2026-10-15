// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.CodeSystems;

/// <summary>One code system used in the folder. Its URI and codes are folder data; no data display is ever included.</summary>
/// <param name="System">The URI as written, or its digest when it is not an http(s) or urn URI without whitespace or control characters.</param>
/// <param name="Name">The well-known name (SNOMED CT, LOINC, HL7 v2 table 0203, ...); null when unknown.</param>
/// <param name="Steward">The steward of a well-known system.</param>
/// <param name="Kind"><c>external</c>, <c>hl7-v2</c>, <c>hl7-v3</c>, <c>hl7-terminology</c>, <c>fhir</c> or <c>unknown</c>.</param>
/// <param name="PreferredUri">The preferred URI when the one written is an alias or OID form of a well-known system.</param>
/// <param name="SuggestedCanonical">For a <c>http://hl7.org/fhir/CodeSystem/...</c>-style URI that is not a FHIR canonical, the canonical it probably means.</param>
/// <param name="Flags"><c>non-preferred-uri</c>, <c>not-a-fhir-canonical</c> (checked against loaded terminology) or <c>possibly-not-a-fhir-canonical</c> (by pattern only).</param>
/// <param name="DistinctCodes">Distinct codes.</param>
/// <param name="Codings">Codings in all.</param>
/// <param name="Resources">Resources holding at least one.</param>
/// <param name="Elements">Codings per element, most first (at most 10).</param>
/// <param name="TopCodes">The most frequent codes as <c>system|code</c> with their counts.</param>
/// <param name="Terminology">How the codes resolve against terminology already loaded; null when none is.</param>
public sealed record CodeSystemUsage(
    string System,
    string? Name,
    string? Steward,
    string Kind,
    string? PreferredUri,
    string? SuggestedCanonical,
    IReadOnlyList<string> Flags,
    int DistinctCodes,
    int Codings,
    int Resources,
    IReadOnlyList<ElementCount> Elements,
    IReadOnlyList<CodeCount> TopCodes,
    SystemTerminologyStatus? Terminology);
