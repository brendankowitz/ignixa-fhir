using Ignixa.Abstractions;

namespace Ignixa.FhirPath.Tests.Evaluation.Parity;

public class SearchIndexParityTests
{
    [Fact]
    public void GivenAResourceWithNonCompositeValues_WhenIndexed_ThenProductionAndFirelyEntriesMatch()
    {
        // Arrange
        const string json = """
            {
              "resourceType": "Observation",
              "id": "parity",
              "status": "final",
              "code": { "coding": [{ "system": "http://loinc.org", "code": "test" }] },
              "valueQuantity": {
                "value": 9,
                "unit": "mg",
                "system": "http://unitsofmeasure.org",
                "code": "mg"
              }
            }
            """;

        // Act
        var comparison = SearchIndexParityHarness.Compare(FhirVersion.R4, json);

        // Assert
        AssertParity(comparison);
        comparison.IgnixaEntries.ShouldContain(entry => entry.Contains("Observation-status", StringComparison.Ordinal));
    }

    [Fact]
    public void GivenAMaximumDensityResource_WhenIndexed_ThenProductionAndFirelyEntriesMatch()
    {
        // Arrange
        var resource = GeneratedParityCorpus.BuildResource(FhirVersion.R4, "AllergyIntolerance");

        // Act
        var comparison = SearchIndexParityHarness.Compare(resource.Version, resource.Json);

        // Assert
        AssertParity(comparison);
    }

    [Fact]
    public void GivenAResourceWithCompositeParameters_WhenIndexed_ThenProductionAndFirelyEntriesMatch()
    {
        // Arrange
        var resource = GeneratedParityCorpus.BuildResource(FhirVersion.R4, "MolecularSequence");

        // Act
        var comparison = SearchIndexParityHarness.Compare(resource.Version, resource.Json);

        // Assert
        AssertParity(comparison);
    }

    /// <summary>
    /// Asserts real agreement rather than compatible silence.
    /// </summary>
    /// <remarks>
    /// The two failure checks run first and the non-empty checks run before the equality check, because
    /// equality on its own is satisfied by two engines that both produced nothing - the Firely
    /// expression throwing and production <c>ElementSearchIndexer</c> containing its own evaluation
    /// failure both yield an empty entry set. Only once neither side is silent does entry equality mean
    /// the two engines agreed.
    /// </remarks>
    private static void AssertParity(SearchIndexComparison comparison)
    {
        comparison.FirelyFailures.ShouldBeEmpty(
            string.Join(Environment.NewLine, comparison.FirelyFailures.Select(failure => failure.Describe())));
        comparison.IgnixaFailures.ShouldBeEmpty(
            string.Join(Environment.NewLine, comparison.IgnixaFailures.Select(failure => failure.Describe())));
        comparison.FirelyEntries.ShouldNotBeEmpty();
        comparison.IgnixaEntries.ShouldNotBeEmpty();
        comparison.FirelyEntries.ShouldBe(comparison.IgnixaEntries, ignoreOrder: true);
    }
}
