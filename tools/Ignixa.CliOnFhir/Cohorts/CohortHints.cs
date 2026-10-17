// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using Ignixa.CliOnFhir.CodeSystems;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Patients;
using Ignixa.CliOnFhir.Session;
using Ignixa.Search.Indexing.SearchValues;

namespace Ignixa.CliOnFhir.Cohorts;

/// <summary>
/// Facts <c>patient_cohort</c> states beside its count, each computed from the folder or the request: an asOf that defaulted to
/// the clock while the folder's data ends earlier, and code systems the criteria name that the folder does not use. Each is
/// empty unless its condition holds exactly.
/// </summary>
internal static class CohortHints
{
    public const string AsOfAfterDataCode = "asof-after-data";

    private const int MostCodeHints = 5;

    private static readonly (string Type, string Parameter)[] ConditionPlaces = [("Condition", "code")];
    private static readonly (string Type, string Parameter)[] MedicationPlaces = [("MedicationRequest", "code"), ("Medication", "code")];
    private static readonly (string Type, string Parameter)[] ImmunizationPlaces = [("Immunization", "vaccine-code")];
    private static readonly (string Type, string Parameter)[] ObservationPlaces = [("Observation", "combo-code")];

    /// <summary>
    /// A warning when asOf defaulted to the clock and the latest date any clinical resource of the folder starts at is before it.
    /// The dates are those of the date search parameters in the indexes of the types a patient's date span counts
    /// (<see cref="ClinicalRules.MainDate"/>); nothing is read when asOf was given.
    /// </summary>
    public static IEnumerable<IssueItem> AsOfAfterData(FolderSession session, CohortRequest request, CancellationToken cancellationToken)
    {
        if (!request.AsOfDefaulted || LatestClinicalDate(session, cancellationToken) is not { } latest || latest.UtcDateTime.Date >= request.AsOf.UtcDateTime.Date)
        {
            yield break;
        }

        yield return new IssueItem(
            "warning",
            AsOfAfterDataCode,
            $"asOf defaulted to {Day(request.AsOf)}; the latest dated resource in this folder is {Day(latest)}. Pass asOf if the question has its own 'now'.",
            Location: null);
    }

    /// <summary>The code-system hints of every criterion that lists codes, for a cohort that came back empty.</summary>
    public static IEnumerable<IssueItem> UnusedCodeSystems(
        FolderSession session,
        CohortRequest request,
        IReadOnlyList<CriterionCodes> conditionCodes,
        IReadOnlyList<CriterionCodes> medicationCodes,
        CancellationToken cancellationToken)
    {
        IEnumerable<IssueItem> hints =
        [
            .. conditionCodes.SelectMany(codes => ForCriterion(session, codes.Codes, ConditionPlaces, cancellationToken)),
            .. medicationCodes.SelectMany(codes => ForCriterion(session, codes.Codes, MedicationPlaces, cancellationToken)),
            .. request.Immunizations.SelectMany(criterion => ForCriterion(session, criterion.Codes, ImmunizationPlaces, cancellationToken)),
            .. request.Observations.SelectMany(criterion => ForCriterion(session, criterion.Codes, ObservationPlaces, cancellationToken)),
        ];
        return hints.Distinct().Take(MostCodeHints).ToList();
    }

    /// <summary>
    /// The hint for each code when it holds in every coded place the criterion's codes are searched in (a medication is searched
    /// on the order and on the Medication it references).
    /// </summary>
    private static IEnumerable<IssueItem> ForCriterion(FolderSession session, IReadOnlyList<CohortCode> codes, (string Type, string Parameter)[] places, CancellationToken cancellationToken)
    {
        List<(string Type, CodedParameterUse Use)> coded = [.. places
            .Select(place => (place.Type, Use: CodedParameterUse.Read(session, place.Type, place.Parameter)))
            .Where(place => place.Use.IsCoded)];
        if (coded.Count == 0)
        {
            yield break;
        }

        foreach (CohortCode code in codes)
        {
            List<IssueItem?> hints = [.. coded.Select(place => UnusedCodeSystemHint.ForToken(session, place.Type, place.Use, code.System, code.Code, cancellationToken))];
            if (hints.All(hint => hint is not null))
            {
                yield return hints[0]!;
            }
        }
    }

    /// <summary>The latest start of any date search parameter of the folder's clinical resources, or null when it has none.</summary>
    private static DateTimeOffset? LatestClinicalDate(FolderSession session, CancellationToken cancellationToken)
    {
        DateTimeOffset? latest = null;
        foreach (string type in ClinicalRules.MainDate.Keys.Where(session.HoldsType).Order(StringComparer.Ordinal))
        {
            foreach (var resource in session.GetIndex(type).Resources)
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var entry in resource.Index)
                {
                    if (entry.Value is DateTimeSearchValue date && (latest is null || date.Start > latest))
                    {
                        latest = date.Start;
                    }
                }
            }
        }

        return latest;
    }

    private static string Day(DateTimeOffset instant) => instant.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
