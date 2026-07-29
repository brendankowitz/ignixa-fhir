/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Frozen list of official FML oracle cases the evaluator currently fails.
 */

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;

namespace Ignixa.FhirMappingLanguage.Tests.Conformance;

/// <summary>
/// In-scope oracle cases the transform harness executes and compares, but which
/// the <c>MappingEvaluator</c> currently gets wrong. Unlike <see cref="FmlOracleExclusions"/>
/// (cases we cannot compare at all), these are executed on every run and ratcheted:
/// the moment one starts producing matching output, its transform test fails and demands
/// the entry be removed. Each defect is scheduled for a dedicated evaluator branch.
/// </summary>
public static class FmlKnownEvaluatorGaps
{
    private static readonly FrozenDictionary<string, string> Gaps =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["qr2patgender"] = "Target alias binds to the source element rather than the target resource; the QuestionnaireResponse tree is emitted under a 'patient' key instead of a Patient with 'gender'.",
            ["qr2pathumannametwice"] = "Nested/recursive 'then' groups are not evaluated, so repeated HumanName rules yield an empty Patient.",
            ["qr2pathumannameshared"] = "'share' combined with nested 'then' groups is not evaluated, so shared-variable HumanName rules yield an empty Patient.",
            ["reference"] = "The create()/reference() builtins throw TARGET_RESOURCE_NOT_FOUND because the 'ext' target resource is never registered.",
            ["qr2pat-gender-conformstoqr"] = "Depends on the FHIRPath conformsTo() function, which FhirSpecificFunctions declares unsupported (requires profile validation infrastructure)."
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Determines whether a manifest case name is a known evaluator gap.
    /// Manifest names are URLs, so the final path segment is used for matching.
    /// </summary>
    public static bool IsKnownGap(string caseName) => Gaps.ContainsKey(LastSegment(caseName));

    /// <summary>
    /// Gets every known gap case name paired with its rationale.
    /// </summary>
    public static IReadOnlyDictionary<string, string> All => Gaps;

    private static string LastSegment(string caseName) =>
        caseName.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? caseName;
}
