// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.Clock;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Patients;
using Ignixa.CliOnFhir.Reading;
using Ignixa.CliOnFhir.Session;

namespace Ignixa.CliOnFhir.Cohorts;

/// <summary>
/// <c>patient_cohort</c>: how many patients meet every criterion of a quality-measure-shaped question (living, age band,
/// gender, conditions, medication orders, immunizations, a lab reading compared with a value or missing), in one call, with
/// the patients left after each criterion, the patients counted and the resources that decided. The count is exact; the
/// patient list and evidence are cut to the response budget.
/// </summary>
internal static class PatientCohort
{
    /// <summary>Most patients a compact response lists.</summary>
    public const int PatientsListed = 20;

    /// <summary>How many of the listed patients a compact response gives evidence for.</summary>
    public const int EvidencePatients = 2;

    /// <summary>The <c>detail</c> that adds each step's rule text, every listed patient that fits and all their evidence.</summary>
    public const string FullDetail = "full";

    private const int CompactCodes = 5;

    /// <exception cref="OperationCanceledException">The call was cancelled.</exception>
    public static async Task<ToolEnvelope<CohortFacts?>> RunAsync(FolderSession session, CohortArguments arguments, CancellationToken cancellationToken, int maxRead = CohortEvaluation.MaxResourcesRead)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(arguments);

        if (EnvelopeAssembler.FolderProblem(session) is { } folderProblem)
        {
            return EnvelopeAssembler.Failure<CohortFacts>(session, folderProblem, cancellationToken);
        }

        try
        {
            if (!ClinicalRules.Verified(session.Schema.Version))
            {
                throw ToolRequestException.Error(
                    ClinicalRules.UnsupportedVersionCode,
                    $"patient_cohort's rules read R4 element paths (onset[x], abatement[x], medication[x], effective[x]) and this folder is read as {session.Schema.Version}; use search_fhir_resources with fhirpath instead.");
            }

            if (arguments.Detail is not (null or "compact" or FullDetail))
            {
                throw ToolRequestException.Error("invalid-parameter", $"'detail' must be 'compact' (the default) or 'full', not '{QueryEcho.Value(arguments.Detail)}'.");
            }

            bool isFull = arguments.Detail == FullDetail;
            AsOf? stated = session.AsOfClock.Stated(arguments.AsOf);
            CohortRequest request = CohortRequest.Parse(arguments, stated, session.UtcNow());
            List<IssueItem> issues = [];
            List<OmittedItem> omitted = [];
            var codes = new CohortCodes(session, issues, omitted);
            IReadOnlyList<CriterionCodes> conditionCodes = await codes.ConditionsAsync(request.Conditions, cancellationToken);
            IReadOnlyList<CriterionCodes> medicationCodes = await codes.MedicationsAsync(request.Medications, cancellationToken);
            CohortOutcome outcome = new CohortEvaluation(session, request, issues, omitted, maxRead, cancellationToken).Run(conditionCodes, medicationCodes);
            issues.AddRange(NameIssues.For(outcome.Evidence.SelectMany(group => group)));
            issues.AddRange(CohortHints.AsOfAfterData(session, request, cancellationToken));
            if (outcome.Counted.Count == 0)
            {
                issues.AddRange(CohortHints.UnusedCodeSystems(session, request, conditionCodes, medicationCodes, cancellationToken));
            }

            // Assembled once: the assembler hands over the session's one-time cache notices, so each candidate size is a copy of it.
            AsOf used = stated ?? new AsOf(request.AsOf, AsOfSources.Clock);
            ToolEnvelope<CohortFacts?> shell = EnvelopeAssembler.Create<CohortFacts?>(session, null, outcome.BuiltAt, [], omitted, [], issues, used, cancellationToken);
            return new Fit(shell, request, outcome, isFull).Within(Math.Min(EnvelopeJson.DefaultBudget, session.MaxResponseBytes));
        }
        catch (ToolRequestException ex)
        {
            return EnvelopeAssembler.Failure<CohortFacts>(session, ex.Issue, cancellationToken);
        }
    }

    /// <summary>The count and steps whole, then as many patients as fit, then as much of their evidence as still fits.</summary>
    private sealed class Fit(ToolEnvelope<CohortFacts?> shell, CohortRequest request, CohortOutcome outcome, bool isFull)
    {
        public ToolEnvelope<CohortFacts?> Within(int budget)
        {
            int patients = Largest(isFull ? outcome.Counted.Count : Math.Min(outcome.Counted.Count, PatientsListed), shown => Build(shown, evidenceShown: 0), budget);
            int evidence = Largest(Evidence(patients).Count, shown => Build(patients, shown), budget);
            return Build(patients, evidence);
        }

        /// <summary>The largest <c>n</c> in <c>[0, count]</c> whose response fits, assuming size grows with <c>n</c>.</summary>
        private static int Largest(int count, Func<int, ToolEnvelope<CohortFacts?>> build, int budget)
        {
            int low = 0;
            int high = count;
            while (low < high)
            {
                int middle = (low + high + 1) / 2;
                if (EnvelopeJson.SizeOf(build(middle)) <= budget)
                {
                    low = middle;
                }
                else
                {
                    high = middle - 1;
                }
            }

            return low;
        }

        /// <summary>The evidence of the first <paramref name="patients"/> patients counted, patient by patient.</summary>
        private List<CatalogEntry> Evidence(int patients) => [.. outcome.Evidence.Take(isFull ? patients : Math.Min(patients, EvidencePatients)).SelectMany(group => group)];

        private ToolEnvelope<CohortFacts?> Build(int patientsShown, int evidenceShown)
        {
            List<CatalogEntry> listed = [.. outcome.Counted.Take(patientsShown)];
            List<CatalogEntry> evidence = Evidence(patientsShown);
            List<OmittedItem> omitted = [.. shell.Omitted];
            if (patientsShown < outcome.Counted.Count)
            {
                omitted.Add(new OmittedItem(
                    $"{outcome.Counted.Count - patientsShown} more patients counted are not listed (facts.patients has the first {patientsShown}); facts.count is exact.{(isFull ? " Add a criterion to list fewer." : " detail=full lists more.")}",
                    "size-budget"));
            }

            if (!isFull && patientsShown > EvidencePatients)
            {
                omitted.Add(new OmittedItem($"evidence for patients after the first {EvidencePatients}; detail=full has it", "size-budget"));
            }

            if (evidenceShown < evidence.Count)
            {
                omitted.Add(new OmittedItem($"{evidence.Count - evidenceShown} more evidence items (the listed patients and the resources that decided each criterion)", "size-budget"));
            }

            var facts = new CohortFacts(CohortInstant.Format(request.AsOf), request.AsOfSource, outcome.Counted.Count, isFull ? outcome.Steps : [.. outcome.Steps.Select(Compact)], [.. listed.Select(ResourceReader.Describe)], request.AsOfPrecision, request.AsOfZoneAssumed ? true : null);
            return shell with
            {
                Facts = facts,
                Evidence = [.. evidence.Take(evidenceShown).Select(entry => new EvidenceItem(ResourceReader.Describe(entry), entry.FilePath, entry.Line, entry.Offset))],
                Omitted = omitted,
                Next = isFull ? Next(listed) : [],
            };
        }

        private static CohortStep Compact(CohortStep step) => step with { Rule = null, Codes = step.Codes?.Take(CompactCodes).ToList() };

        // Built from structure only: the counted patients' ids and fixed column paths.
        private static List<NextCall> Next(List<CatalogEntry> listed)
        {
            if (listed.Count == 0)
            {
                return [];
            }

            List<CatalogEntry> first = [.. listed.Take(PatientsListed)];
            string ids = string.Join(',', first.Select(patient => FhirIds.Reference(patient)["Patient/".Length..]));
            return
            [
                new NextCall(
                    "search_fhir_resources",
                    new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["resourceType"] = "Patient",
                        ["query"] = $"_id={ids}",
                        ["columns"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["name"] = "name.first().family", ["birthDate"] = "birthDate", ["gender"] = "gender" },
                    },
                    $"A table of the first {first.Count} patients counted."),
            ];
        }
    }
}
