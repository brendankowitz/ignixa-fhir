using Xunit.Abstractions;

namespace Ignixa.FhirPath.Tests.Evaluation.Parity;

public class ResourceBackedParityCorpusTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    [Fact]
    public void GivenGeneratedAndTargetedResources_WhenSwept_ThenOnlyClassifiedDivergencesRemain()
    {
        // Arrange

        // Act
        var select = ResourceParitySweep.Run();
        var index = SearchIndexParitySweep.Run();
        WriteSummary(select, index);

        // Assert
        AssertSelectFindings(select);
        AssertIndexFindings(index);
    }

    private void WriteSummary(ResourceParityReport select, SearchIndexParityReport index)
    {
        _output.WriteLine(
            "Select: {0} evaluations per engine across {1} resources in {2:F3}s; {3} divergences; {4} both threw; {5} both empty.",
            select.SelectEvaluationsPerEngine,
            select.ResourceCount,
            select.Elapsed.TotalSeconds,
            select.Divergences.Count,
            select.BothThrew,
            select.BothEmpty);
        _output.WriteLine(
            "Index: {0} resources in {1:F3}s; {2} divergent resources; {3} reference failures.",
            index.ResourceCount,
            index.Elapsed.TotalSeconds,
            index.Divergences.Count,
            index.ReferenceFailures.Count);
        foreach (var failure in index.ReferenceFailures
                     .GroupBy(failure => failure.Signature, StringComparer.Ordinal)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            _output.WriteLine("Reference failure: {0} = {1}.", failure.Key, failure.Count());
        }

        foreach (var finding in select.Divergences
                     .Select(ResourceBackedKnownDivergences.Classify)
                     .Where(classification => classification is not null)
                     .Select(classification => classification!)
                     .GroupBy(classification => classification.RootCause, StringComparer.Ordinal)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var classification = finding.First();
            _output.WriteLine(
                "Select finding: {0} ({1}, blocks={2}) = {3}.",
                finding.Key,
                classification.Reachability,
                classification.BlocksEnablement,
                finding.Count());
        }

        foreach (var finding in index.Divergences
                     .Select(ResourceBackedKnownDivergences.Classify)
                     .Where(classification => classification is not null)
                     .Select(classification => classification!)
                     .GroupBy(classification => classification.RootCause, StringComparer.Ordinal)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            _output.WriteLine("Index finding: {0} = {1} resources.", finding.Key, finding.Count());
        }
    }

    private static void AssertSelectFindings(ResourceParityReport report)
    {
        report.SelectEvaluationsPerEngine.ShouldBeGreaterThanOrEqualTo(
            ResourceBackedKnownDivergences.MinimumSelectEvaluationsPerEngine);
        report.ResourceCount.ShouldBeGreaterThanOrEqualTo(
            ResourceBackedKnownDivergences.MinimumResourceCount);
        report.BothThrew.ShouldBe(ResourceBackedKnownDivergences.ExpectedBothThrew);
        report.BothEmpty.ShouldBe(ResourceBackedKnownDivergences.ExpectedBothEmpty);

        var classified = report.Divergences
            .Select(divergence => (Divergence: divergence, Classification: ResourceBackedKnownDivergences.Classify(divergence)))
            .ToArray();
        var unclassified = classified
            .Where(item => item.Classification is null)
            .Select(item => item.Divergence)
            .ToArray();

        unclassified.ShouldBeEmpty(
            ParityReport.Render(unclassified, report.SelectEvaluationsPerEngine, report.ResourceCount));
        classified.Select(item => item.Classification!)
            .ShouldAllBe(classification =>
                classification.BlocksEnablement
                == (classification.Reachability == ParityReachability.SearchParameter));
        AssertCounts(
            classified.Select(item => item.Classification!.RootCause),
            ResourceBackedKnownDivergences.ExpectedSelectCounts);
    }

    private static void AssertIndexFindings(SearchIndexParityReport report)
    {
        report.ReferenceFailures.ShouldBeEmpty(
            string.Join(Environment.NewLine, report.ReferenceFailures.Select(failure => failure.Describe())));

        var classified = report.Divergences
            .Select(divergence => (Divergence: divergence, Classification: ResourceBackedKnownDivergences.Classify(divergence)))
            .ToArray();
        var unclassified = classified
            .Where(item => item.Classification is null)
            .Select(item => item.Divergence)
            .ToArray();

        unclassified.ShouldBeEmpty(RenderIndexDivergences(unclassified));
        classified.Select(item => item.Classification!)
            .ShouldAllBe(classification =>
                classification.BlocksEnablement
                && classification.Reachability == ParityReachability.SearchParameter);
        AssertCounts(
            classified.Select(item => item.Classification!.RootCause),
            ResourceBackedKnownDivergences.ExpectedIndexResourceCounts);
    }

    private static void AssertCounts(
        IEnumerable<string> rootCauses,
        IReadOnlyDictionary<string, int> expected)
    {
        var actualCounts = rootCauses
            .GroupBy(rootCause => rootCause, StringComparer.Ordinal)
            .Select(group => $"{group.Key}: {group.Count()}")
            .Order(StringComparer.Ordinal)
            .ToArray();
        var expectedCounts = expected
            .Select(pair => $"{pair.Key}: {pair.Value}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        actualCounts.ShouldBe(expectedCounts);
    }

    private static string RenderIndexDivergences(IReadOnlyList<SearchIndexDivergence> divergences) =>
        string.Join(
            Environment.NewLine,
            divergences.Select(
                divergence =>
                {
                    var firelyOnly = divergence.FirelyEntries
                        .Except(divergence.IgnixaEntries, StringComparer.Ordinal)
                        .Select(entry => $"  Firely only: {entry}");
                    var ignixaOnly = divergence.IgnixaEntries
                        .Except(divergence.FirelyEntries, StringComparer.Ordinal)
                        .Select(entry => $"  Ignixa only: {entry}");
                    return string.Join(
                        Environment.NewLine,
                        [
                            $"{divergence.ResourceName}: Firely={divergence.FirelyEntries.Count}, Ignixa={divergence.IgnixaEntries.Count}",
                            .. firelyOnly,
                            .. ignixaOnly,
                        ]);
                }));
}
