// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Patients;

/// <summary>The v1 domains of a patient's context (agent-tool-surface.md, "Navigate one patient") and the types in each.</summary>
internal static class ClinicalDomains
{
    public const string Demographics = "demographics";

    public const string Problems = "problems";

    public const string Medications = "medications";

    public const string Allergies = "allergies";

    public const string Encounters = "encounters";

    public const string Labs = "labs";

    /// <summary>Every domain in display order with its resource types; the last three have no stub in the root digest yet.</summary>
    public static readonly IReadOnlyList<(string Name, IReadOnlyList<string> Types)> All =
    [
        (Demographics, ["Patient"]),
        (Problems, ["Condition"]),
        (Medications, ["MedicationRequest", "MedicationStatement"]),
        (Allergies, ["AllergyIntolerance"]),
        (Encounters, ["Encounter"]),
        (Labs, ["Observation"]),
        ("procedures", ["Procedure"]),
        ("immunizations", ["Immunization"]),
        ("documents", ["DocumentReference", "Composition", "DiagnosticReport"]),
    ];

    /// <summary>The types the root digest summarizes in a domain stub.</summary>
    public static readonly IReadOnlySet<string> StubTypes = new HashSet<string>(
        ["Patient", "Condition", "MedicationRequest", "MedicationStatement", "AllergyIntolerance", "Encounter", "Observation"],
        StringComparer.Ordinal);

    /// <summary>The domains with at least one resource among <paramref name="resourceTypes"/>, in display order.</summary>
    public static IReadOnlyList<string> Present(IReadOnlySet<string> resourceTypes) =>
        [.. All.Where(domain => domain.Types.Any(resourceTypes.Contains)).Select(domain => domain.Name)];
}
