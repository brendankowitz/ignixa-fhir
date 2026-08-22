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
            "Select: {0} evaluations per engine across {1} resources in {2:F3}s; {3} divergences; {4} both threw; {5} both empty; {6} agreed on values.",
            select.SelectEvaluationsPerEngine,
            select.ResourceCount,
            select.Elapsed.TotalSeconds,
            select.Divergences.Count,
            select.BothThrew,
            select.BothEmpty,
            select.AgreementsOnValues);
        _output.WriteLine(
            "Index: {0} resources in {1:F3}s; {2} entries compared ({3} Firely / {4} Ignixa); {5} divergent resources; {6} reference failures; {7} Ignixa evaluation failures; {8} Ignixa pipeline skips.",
            index.ResourceCount,
            index.Elapsed.TotalSeconds,
            index.EntriesCompared,
            index.FirelyEntriesCompared,
            index.IgnixaEntriesCompared,
            index.Divergences.Count,
            index.ReferenceFailures.Count,
            index.IgnixaFailures.Count(failure => failure.ContainedAThrow),
            index.IgnixaFailures.Count(failure => !failure.ContainedAThrow));
        foreach (var failure in index.ReferenceFailures
                     .GroupBy(failure => failure.Signature, StringComparer.Ordinal)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            _output.WriteLine("Reference failure: {0} = {1}.", failure.Key, failure.Count());
        }

        foreach (var failure in index.IgnixaFailures
                     .GroupBy(failure => failure.Signature, StringComparer.Ordinal)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            _output.WriteLine("Ignixa failure: {0} = {1}.", failure.Key, failure.Count());
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
            ResourceBackedKnownDivergences.MinimumSelectEvaluationsPerEngine,
            """
            The parity sweep evaluated fewer expressions per engine than the floor.
            The evidence base for the conformance claim has shrunk, so something removed
            expressions or resources from the corpus. Find what, and restore it.
            Raise this floor when the corpus genuinely grows; never lower it to accommodate a loss.
            """);
        report.ResourceCount.ShouldBeGreaterThanOrEqualTo(
            ResourceBackedKnownDivergences.MinimumResourceCount,
            """
            The sweep ran over fewer resources than the floor.
            Check SchemaBasedFhirResourceFaker and the resource type list before anything else.
            Raise this floor when the corpus genuinely grows; never lower it to accommodate a loss.
            """);
        report.BothThrew.ShouldBe(
            ResourceBackedKnownDivergences.ExpectedBothThrew,
            """
            Both engines now throw on an expression where previously neither did.
            This is not a parity failure - the engines still agree - but it is coverage lost:
            an evaluation that used to compare real values no longer does.
            Identify the expression from the divergence output and confirm the throw is correct
            for both engines before re-pinning.
            """);
        report.BothEmpty.ShouldBe(
            ResourceBackedKnownDivergences.ExpectedBothEmpty,
            """
            The both-empty count moved. Roughly 62% of this number is corpus shape and 38% is
            engine behaviour: 5,888 of 9,453 come from expressions that are never non-empty
            anywhere in the corpus, so the faker's density decides them, not either engine.
            Check whether the corpus, the resource type list, or SchemaBasedFhirResourceFaker
            changed before concluding an engine improved. Re-pin only once you can say which of
            the two moved, and say so in the commit message.
            """);
        report.BucketsPartitionEvaluations.ShouldBeTrue(
            $"""
            The outcome buckets no longer account for every evaluation exactly once:
            {report.BothThrew} both threw + {report.BothEmpty} both empty
            + {report.AgreementsOnValues} agreed on values + {report.DivergentEvaluations} divergent
            != {report.SelectEvaluationsPerEngine} evaluations, or the divergent count disagrees with
            the {report.Divergences.Count} divergences collected.
            AgreementsOnValues is counted at the point of observation and this is the cross-check
            against the subtraction it replaced, so a mismatch is a defect in ParityOutcomeTally -
            not a number to re-pin.
            """);
        report.AgreementsOnValues.ShouldBeGreaterThanOrEqualTo(
            ResourceBackedKnownDivergences.MinimumAgreementsOnValues,
            """
            Fewer evaluations produced matching non-empty values than the floor requires.
            This is the number the conformance claim rests on, so a drop is a regression until
            proven otherwise - agreements do not become empty or divergent for a benign reason.
            A floor is raised when evidence is gained and never lowered to accommodate a loss:
            if it is red, fix the regression rather than re-pinning the floor beneath it.
            """);

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
            ResourceBackedKnownDivergences.ExpectedSelectCounts,
            "The reach of a classified Select divergence changed.");
    }

    private static void AssertIndexFindings(SearchIndexParityReport report)
    {
        report.ReferenceFailures.ShouldBeEmpty(
            string.Join(Environment.NewLine, report.ReferenceFailures.Select(failure => failure.Describe())));

        report.ResourceCount.ShouldBeGreaterThanOrEqualTo(
            ResourceBackedKnownDivergences.MinimumIndexResourceCount,
            """
            The index sweep ran over fewer resources than the floor.
            Check SchemaBasedFhirResourceFaker and the resource type list before anything else.
            Raise this floor when the corpus genuinely grows; never lower it to accommodate a loss.
            """);
        report.FirelyEntriesCompared.ShouldBeGreaterThanOrEqualTo(
            ResourceBackedKnownDivergences.MinimumIndexEntriesComparedPerEngine,
            """
            The reference side of the index sweep contributed fewer entries than the floor, so the
            evidence base shrank while the divergence and failure pins stayed satisfied - they say
            what went wrong, never how much was examined. Find what stopped producing entries.
            Raise this floor when the corpus genuinely grows; never lower it to accommodate a loss.
            """);
        report.IgnixaEntriesCompared.ShouldBeGreaterThanOrEqualTo(
            ResourceBackedKnownDivergences.MinimumIndexEntriesComparedPerEngine,
            """
            The production side of the index sweep contributed fewer entries than the floor.
            A search parameter that stops indexing is invisible to entry equality whenever the
            reference side also produced nothing for it, which is what this floor exists to catch.
            Raise this floor when the corpus genuinely grows; never lower it to accommodate a loss.
            """);

        AssertCounts(
            report.IgnixaFailures.Where(failure => failure.ContainedAThrow).Select(failure => failure.Signature),
            ResourceBackedKnownDivergences.ExpectedIgnixaEvaluationFailures,
            """
            The set of exceptions production ElementSearchIndexer contained during the sweep moved.
            These are the failures this harness can adjudicate: Ignixa's evaluator threw where
            Firely's ran the same expression, and the contained throw indexed nothing while the
            comparison still scored it as agreement.
            """);
        AssertCounts(
            report.IgnixaFailures.Where(failure => !failure.ContainedAThrow).Select(failure => failure.Signature),
            ResourceBackedKnownDivergences.ExpectedIgnixaConverterPipelineSkips,
            """
            The set of elements production ElementSearchIndexer skipped as unindexable moved.
            Both indexers reach that decision through the same Ignixa objects, so this corpus records
            these and cannot adjudicate them - 186 of them are the canonical converter gap tracked as
            production work. A new signature is a new unindexable site; a higher count is an existing
            gap reaching further; a lower count is either a converter landing or a corpus that stopped
            generating the shape. Say which before re-pinning.
            """);

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
            ResourceBackedKnownDivergences.ExpectedIndexResourceCounts,
            "The reach of a classified index divergence changed.");
    }

    private static void AssertCounts(
        IEnumerable<string> rootCauses,
        IReadOnlyDictionary<string, int> expected,
        string because)
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

        actualCounts.ShouldBe(expectedCounts, because);
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
