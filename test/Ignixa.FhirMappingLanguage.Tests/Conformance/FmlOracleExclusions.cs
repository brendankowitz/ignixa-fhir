/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Frozen list of official FML oracle cases outside Ignixa's supported scope.
 */

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;

namespace Ignixa.FhirMappingLanguage.Tests.Conformance;

/// <summary>
/// Cases from the official corpus that Ignixa deliberately does not attempt.
/// Per ADR-2607, the exclusion list is frozen and each entry carries a written
/// rationale: conformance is reported as a percentage of supported scope, never
/// inflated by quietly skipping hard cases.
/// </summary>
public static class FmlOracleExclusions
{
    private static readonly FrozenDictionary<string, string> Excluded =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["qr2cda"] = "Targets the CDA logical model and produces XML output; Ignixa's transform pipeline emits FHIR JSON only.",
            ["qr2cdaxsi"] = "CDA logical model with xsi:type discrimination; XML output is out of scope.",
            ["qr2cd-eval-json"] = "CDA logical model target; XML output is out of scope.",
            ["qr2cd-eval-fml"] = "CDA logical model target; XML output is out of scope."
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Determines whether a manifest case name is excluded from the supported scope.
    /// Manifest names are URLs, so the final path segment is used for matching.
    /// </summary>
    public static bool IsExcluded(string caseName) => Excluded.ContainsKey(LastSegment(caseName));

    /// <summary>
    /// Gets the written rationale for an excluded case, or <c>null</c> if it is not excluded.
    /// </summary>
    public static string? RationaleFor(string caseName) =>
        Excluded.TryGetValue(LastSegment(caseName), out var rationale) ? rationale : null;

    /// <summary>
    /// Gets every excluded case name paired with its rationale.
    /// </summary>
    public static IReadOnlyDictionary<string, string> All => Excluded;

    private static string LastSegment(string caseName) =>
        caseName.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? caseName;
}
