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
        comparison.FirelyEntries.ShouldBe(comparison.IgnixaEntries, ignoreOrder: true);
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
        comparison.FirelyEntries.ShouldBe(comparison.IgnixaEntries, ignoreOrder: true);
    }

    [Fact]
    public void GivenAResourceWithCompositeParameters_WhenIndexed_ThenProductionAndFirelyEntriesMatch()
    {
        // Arrange
        var resource = GeneratedParityCorpus.BuildResource(FhirVersion.R4, "MolecularSequence");

        // Act
        var comparison = SearchIndexParityHarness.Compare(resource.Version, resource.Json);

        // Assert
        comparison.FirelyEntries.ShouldBe(comparison.IgnixaEntries, ignoreOrder: true);
    }
}
