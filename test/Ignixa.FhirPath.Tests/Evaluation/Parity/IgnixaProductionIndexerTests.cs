using Ignixa.Abstractions;
using Ignixa.FhirFakes;
using Ignixa.Search.Indexing;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ignixa.FhirPath.Tests.Evaluation.Parity;

public class IgnixaProductionIndexerTests
{
    [Fact]
    public void GivenAGeneratedObservation_WhenIndexed_ThenProductionIndexerProducesEntries()
    {
        // Arrange
        var schema = new R4CoreSchemaProvider();
        var faker = new SchemaBasedFhirResourceFaker(schema, seed: 405)
        {
            Density = GenerationDensity.Maximum
        };
        var resource = faker.Generate("Observation").ToElement(schema);
        var indexer = SearchIndexerFactory.CreateInstance(
            schema,
            NullLoggerFactory.Instance,
            searchParameterDefinitionManager: null!,
            NullFhirBaseUriProvider.Instance);

        // Act
        var entries = indexer.Extract(resource);

        // Assert
        entries.ShouldNotBeEmpty();
        entries.ShouldContain(entry => entry.SearchParameter.Code == "status");
    }
}
