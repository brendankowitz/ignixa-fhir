// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Catalog;
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
    /// <summary>Most patients the 'next' call lists.</summary>
    public const int NextPatients = 20;

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

            CohortRequest request = CohortRequest.Parse(arguments, session.UtcNow());
            List<IssueItem> issues = [];
            List<OmittedItem> omitted = [];
            var codes = new CohortCodes(session, issues, omitted);
            IReadOnlyList<CriterionCodes> conditionCodes = await codes.ConditionsAsync(request.Conditions, cancellationToken);
            IReadOnlyList<CriterionCodes> medicationCodes = await codes.MedicationsAsync(request.Medications, cancellationToken);
            CohortOutcome outcome = new CohortEvaluation(session, request, issues, omitted, maxRead, cancellationToken).Run(conditionCodes, medicationCodes);
            issues.AddRange(NameIssues.For(outcome.Evidence.SelectMany(group => group)));

            // Assembled once: the assembler hands over the session's one-time cache notices, so each candidate size is a copy of it.
            ToolEnvelope<CohortFacts?> shell = EnvelopeAssembler.Create<CohortFacts?>(session, null, outcome.BuiltAt, [], omitted, [], issues, cancellationToken);
            return new Fit(shell, request, outcome).Within(Math.Min(EnvelopeJson.DefaultBudget, session.MaxResponseBytes));
        }
        catch (ToolRequestException ex)
        {
            return EnvelopeAssembler.Failure<CohortFacts>(session, ex.Issue, cancellationToken);
        }
    }

    /// <summary>The count and steps whole, then as many patients as fit, then as much of their evidence as still fits.</summary>
    private sealed class Fit(ToolEnvelope<CohortFacts?> shell, CohortRequest request, CohortOutcome outcome)
    {
        public ToolEnvelope<CohortFacts?> Within(int budget)
        {
            int patients = Largest(outcome.Counted.Count, shown => Build(shown, evidenceShown: 0), budget);
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
        private List<CatalogEntry> Evidence(int patients) => [.. outcome.Evidence.Take(patients).SelectMany(group => group)];

        private ToolEnvelope<CohortFacts?> Build(int patientsShown, int evidenceShown)
        {
            List<CatalogEntry> listed = [.. outcome.Counted.Take(patientsShown)];
            List<CatalogEntry> evidence = Evidence(patientsShown);
            List<OmittedItem> omitted = [.. shell.Omitted];
            if (patientsShown < outcome.Counted.Count)
            {
                omitted.Add(new OmittedItem(
                    $"{outcome.Counted.Count - patientsShown} more patients counted are not listed in facts.patients (it lists the first {patientsShown}); facts.count is exact. Add a criterion to list fewer.",
                    "size-budget"));
            }

            if (evidenceShown < evidence.Count)
            {
                omitted.Add(new OmittedItem($"{evidence.Count - evidenceShown} more evidence items (the listed patients and the resources that decided each criterion)", "size-budget"));
            }

            var facts = new CohortFacts(CohortInstant.Format(request.AsOf), outcome.Counted.Count, outcome.Steps, [.. listed.Select(ResourceReader.Describe)]);
            return shell with
            {
                Facts = facts,
                Evidence = [.. evidence.Take(evidenceShown).Select(entry => new EvidenceItem(ResourceReader.Describe(entry), entry.FilePath, entry.Line, entry.Offset))],
                Omitted = omitted,
                Next = Next(listed),
            };
        }

        // Built from structure only: the counted patients' ids and fixed column paths.
        private static List<NextCall> Next(List<CatalogEntry> listed)
        {
            if (listed.Count == 0)
            {
                return [];
            }

            List<CatalogEntry> first = [.. listed.Take(NextPatients)];
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
