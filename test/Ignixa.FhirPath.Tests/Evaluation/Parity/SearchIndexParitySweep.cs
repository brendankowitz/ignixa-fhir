using System.Diagnostics;
using System.Globalization;

namespace Ignixa.FhirPath.Tests.Evaluation.Parity;

internal static class SearchIndexParitySweep
{
    public static SearchIndexParityReport Run()
    {
        var stopwatch = Stopwatch.StartNew();
        var divergences = new List<SearchIndexDivergence>();
        var referenceFailures = new List<ReferenceEvaluationFailure>();
        var ignixaFailures = new List<IgnixaEvaluationFailure>();
        var entries = new int[2];
        int resources = 0;

        foreach (var version in GeneratedParityCorpus.Build())
        {
            foreach (var resource in version.Resources)
            {
                Collect(
                    version.Version,
                    $"{version.Version}/{resource.ResourceType}/generated",
                    resource.Json,
                    "de-DE",
                    divergences,
                    referenceFailures,
                    ignixaFailures,
                    entries);
                resources++;
            }
        }

        foreach (var resource in TargetedParityCorpus.Build())
        {
            Collect(
                resource.Version,
                $"{resource.Version}/{resource.Name}",
                resource.Json,
                resource.CultureName,
                divergences,
                referenceFailures,
                ignixaFailures,
                entries);
            resources++;
        }

        stopwatch.Stop();
        return new SearchIndexParityReport(
            resources,
            entries[0],
            entries[1],
            stopwatch.Elapsed,
            divergences,
            referenceFailures,
            ignixaFailures);
    }

    private static void Collect(
        Ignixa.Abstractions.FhirVersion version,
        string resourceName,
        string json,
        string? cultureName,
        List<SearchIndexDivergence> divergences,
        List<ReferenceEvaluationFailure> referenceFailures,
        List<IgnixaEvaluationFailure> ignixaFailures,
        int[] entries)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            if (cultureName is not null)
            {
                var culture = CultureInfo.GetCultureInfo(cultureName);
                CultureInfo.CurrentCulture = culture;
                CultureInfo.CurrentUICulture = culture;
            }

            var comparison = SearchIndexParityHarness.Compare(version, json);
            referenceFailures.AddRange(comparison.FirelyFailures);
            ignixaFailures.AddRange(comparison.IgnixaFailures);
            entries[0] += comparison.FirelyEntries.Count;
            entries[1] += comparison.IgnixaEntries.Count;
            if (!comparison.FirelyEntries.SequenceEqual(comparison.IgnixaEntries, StringComparer.Ordinal))
            {
                divergences.Add(
                    new SearchIndexDivergence(
                        version,
                        resourceName,
                        comparison.FirelyEntries,
                        comparison.IgnixaEntries));
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }
}
