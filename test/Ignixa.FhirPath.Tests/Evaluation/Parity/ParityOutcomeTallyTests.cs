namespace Ignixa.FhirPath.Tests.Evaluation.Parity;

/// <summary>
/// Pins which bucket each outcome pair lands in.
/// </summary>
/// <remarks>
/// The sweeps assert the four buckets partition the population and floor the agreements-on-values
/// count, which catches a tally that loses or double-counts an evaluation but not one that files an
/// evaluation under the wrong heading - a mutual throw counted as an agreement on values keeps the
/// partition intact and inflates the number the conformance claim rests on. That is the defect this
/// tally replaced, so the assignment itself is pinned here rather than only in aggregate.
/// </remarks>
public class ParityOutcomeTallyTests
{
    [Fact]
    public void GivenBothEnginesThrew_WhenObserved_ThenOnlyTheMutualThrowBucketMoves()
    {
        // Arrange
        var tally = new ParityOutcomeTally();

        // Act
        tally.Observe(Threw, Threw);

        // Assert
        ShouldBeBuckets(tally, bothThrew: 1, bothEmpty: 0, agreedOnValues: 0, divergent: 0);
    }

    [Fact]
    public void GivenBothEnginesReturnedNothing_WhenObserved_ThenOnlyTheMutualEmptyBucketMoves()
    {
        // Arrange
        var tally = new ParityOutcomeTally();

        // Act
        tally.Observe(Returned(), Returned());

        // Assert
        ShouldBeBuckets(tally, bothThrew: 0, bothEmpty: 1, agreedOnValues: 0, divergent: 0);
    }

    [Fact]
    public void GivenBothEnginesReturnedTheSameValues_WhenObserved_ThenOnlyTheValueAgreementBucketMoves()
    {
        // Arrange
        var tally = new ParityOutcomeTally();

        // Act
        tally.Observe(Returned("a", "b"), Returned("a", "b"));

        // Assert
        ShouldBeBuckets(tally, bothThrew: 0, bothEmpty: 0, agreedOnValues: 1, divergent: 0);
    }

    [Fact]
    public void GivenOneEngineThrewAndTheOtherReturned_WhenObserved_ThenTheEvaluationIsDivergent()
    {
        // Arrange
        var tally = new ParityOutcomeTally();

        // Act
        tally.Observe(Threw, Returned("a"));
        tally.Observe(Returned(), Threw);

        // Assert
        ShouldBeBuckets(tally, bothThrew: 0, bothEmpty: 0, agreedOnValues: 0, divergent: 2);
    }

    [Fact]
    public void GivenOneEngineReturnedNothing_WhenTheOtherReturnedAValue_ThenTheEvaluationIsDivergent()
    {
        // Arrange
        var tally = new ParityOutcomeTally();

        // Act
        tally.Observe(Returned("a"), Returned());
        tally.Observe(Returned("a"), Returned("b"));

        // Assert
        ShouldBeBuckets(tally, bothThrew: 0, bothEmpty: 0, agreedOnValues: 0, divergent: 2);
    }

    [Fact]
    public void GivenAMixedPopulation_WhenObserved_ThenTheBucketsPartitionTheEvaluations()
    {
        // Arrange
        var tally = new ParityOutcomeTally();

        // Act
        tally.Observe(Threw, Threw);
        tally.Observe(Returned(), Returned());
        tally.Observe(Returned("a"), Returned("a"));
        tally.Observe(Returned("a"), Threw);

        // Assert
        ShouldBeBuckets(tally, bothThrew: 1, bothEmpty: 1, agreedOnValues: 1, divergent: 1);
    }

    private static ParityOutcome Threw => ParityOutcome.Failed(new InvalidOperationException());

    private static ParityOutcome Returned(params string[] results) => ParityOutcome.Returned(results);

    private static void ShouldBeBuckets(
        ParityOutcomeTally tally,
        int bothThrew,
        int bothEmpty,
        int agreedOnValues,
        int divergent)
    {
        tally.BothThrew.ShouldBe(bothThrew);
        tally.BothEmpty.ShouldBe(bothEmpty);
        tally.AgreedOnValues.ShouldBe(agreedOnValues);
        tally.Divergent.ShouldBe(divergent);
        tally.Evaluations.ShouldBe(bothThrew + bothEmpty + agreedOnValues + divergent);
    }
}
