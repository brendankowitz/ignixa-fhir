// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Ignixa.CliOnFhir.Contracts;

namespace Ignixa.CliOnFhir.Patients;

/// <summary>
/// The keys of a patient's context nodes: <c>problems</c>, <c>medications</c>, <c>allergies</c>, <c>encounters</c>,
/// <c>labs</c>, <c>problem/Condition-{id}</c>, <c>encounter/Encounter-{id}</c> and <c>labs/{system}|{code}</c>.
/// </summary>
/// <remarks>
/// Ids, systems and codes come from the folder, which is untrusted (agent-tool-surface.md, risk 1), and keys travel in
/// <c>next</c>. So a part is used verbatim only when it is a plain token (letters, digits and <c>-._:/#%+</c>, at most 200
/// characters, which every FHIR id and real code system URL and code meets); any other part (one with spaces, quotes,
/// <c>|</c> or of any length beyond) is replaced by <c>~</c> and 16 hex digits of its SHA-256, so no text from a resource
/// reaches a key. A key is resolved by computing the keys of the patient's resources and comparing, so both forms work.
/// </remarks>
internal static partial class NodeKey
{
    public const string ProblemPrefix = "problem/Condition-";

    public const string EncounterPrefix = "encounter/Encounter-";

    public const string LabPrefix = "labs/";

    /// <summary>The domain-level keys, each listing a whole domain.</summary>
    public static readonly IReadOnlyList<string> DomainKeys =
        [ClinicalDomains.Problems, ClinicalDomains.Medications, ClinicalDomains.Allergies, ClinicalDomains.Encounters, ClinicalDomains.Labs];

    public static string Problem(string conditionId) => ProblemPrefix + Part(conditionId);

    public static string Encounter(string encounterId) => EncounterPrefix + Part(encounterId);

    /// <summary>The key of a lab series, from the system and code of its key coding (<see cref="ClinicalRules.LabKeyCoding"/>).</summary>
    public static string Lab(LabCoding coding) => $"{LabPrefix}{Part(coding.System)}|{Part(coding.Code)}";

    /// <summary>The key of the node a resource of <paramref name="resourceType"/> belongs to, or null when no node holds it.</summary>
    public static string? Of(string resourceType, string id, LabCoding? labCoding) => resourceType switch
    {
        "Condition" => Problem(id),
        "Encounter" => Encounter(id),
        "Observation" when labCoding is not null => Lab(labCoding),
        "MedicationRequest" or "MedicationStatement" => ClinicalDomains.Medications,
        "AllergyIntolerance" => ClinicalDomains.Allergies,
        _ => null,
    };

    /// <summary>Refuses a key that has none of the forms above; whether it names a node of this patient is decided by the caller.</summary>
    /// <exception cref="ToolRequestException">The key is malformed.</exception>
    public static void Validate(string? nodeKey)
    {
        bool wellFormed = nodeKey switch
        {
            null => false,
            _ when DomainKeys.Contains(nodeKey, StringComparer.Ordinal) => true,
            _ when nodeKey.StartsWith(ProblemPrefix, StringComparison.Ordinal) => IsPart(nodeKey[ProblemPrefix.Length..]),
            _ when nodeKey.StartsWith(EncounterPrefix, StringComparison.Ordinal) => IsPart(nodeKey[EncounterPrefix.Length..]),
            _ when nodeKey.StartsWith(LabPrefix, StringComparison.Ordinal) => nodeKey[LabPrefix.Length..].Split('|') is [string system, string code] && IsPart(system) && IsPart(code),
            _ => false,
        };

        if (!wellFormed)
        {
            throw ToolRequestException.Error(
                "invalid-node-key",
                $"'{nodeKey}' is not a node key. Use one returned by context_get_root, context_expand or context_find: {string.Join(", ", DomainKeys)}, {ProblemPrefix}<id>, {EncounterPrefix}<id> or {LabPrefix}<system>|<code>.");
        }
    }

    private static string Part(string value) =>
        PlainToken().IsMatch(value) ? value : "~" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)).AsSpan(0, 8));

    private static bool IsPart(string value) => PlainToken().IsMatch(value) || DigestToken().IsMatch(value);

    [GeneratedRegex("^[A-Za-z0-9\\-\\._:/#%+]{1,200}$")]
    private static partial Regex PlainToken();

    [GeneratedRegex("^~[0-9a-f]{16}$")]
    private static partial Regex DigestToken();
}
