using Hl7.Fhir.ElementModel;
using Hl7.Fhir.FhirPath;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Hl7.Fhir.Specification;
using Hl7.FhirPath;
using Ignixa.Abstractions;
using Ignixa.Extensions.FirelySdk;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Search.Definition;
using Ignixa.Search.Indexing;
using Ignixa.Specification.Extensions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ignixa.FhirPath.Phase3.Stu3.Tests;

public class Stu3CompositeDoubleEmptyTests
{
    private const string ObservationJson = """
        {
          "resourceType": "Observation",
          "id": "stu3-double-empty-probe",
          "status": "final",
          "code": {
            "coding": [
              {
                "system": "http://loinc.org",
                "code": "29463-7",
                "display": "Body weight"
              }
            ]
          },
          "valueDateTime": "2024-06-15T08:00:00Z"
        }
        """;

    private static readonly IFhirSchemaProvider Schema = FhirVersion.Stu3.GetSchemaProvider();

    [Fact]
    public void GivenCodedObservation_WhenCompositeIndexed_ThenIndependentFailuresProduceTheSameEmptyIndexResult()
    {
        // Arrange
        var definitions = new SearchParameterDefinitionManager(
            Schema,
            NullLogger<SearchParameterDefinitionManager>.Instance);
        var composite = definitions.GetSearchParameter("Observation", "code-value-date");
        string dateExpression = composite.Component[1].Expression;
        ITypedElement nativeInput = ParseNativeInput();
        IElement ignixaInput = nativeInput.ToIgnixaElement();
        var context = new Ignixa.FhirPath.Evaluation.FhirEvaluationContext
        {
            Schema = Schema,
            Resource = ignixaInput,
            RootResource = ignixaInput,
        };
        var indexer = SearchIndexerFactory.CreateInstance(
            Schema,
            NullLoggerFactory.Instance,
            definitions,
            NullFhirBaseUriProvider.Instance);

        // Act
        ITypedElement[] firelyDate = nativeInput
            .Select(dateExpression, new Hl7.Fhir.FhirPath.FhirEvaluationContext())
            .ToArray();
        IElement[] ignixaDate = Ignixa.FhirPath.Evaluation.TypedElementExtensions
            .Select(ignixaInput, dateExpression, context)
            .ToArray();
        var finalCompositeEntries = indexer.Extract(ignixaInput)
            .Where(entry => entry.SearchParameter.Code == "code-value-date")
            .ToArray();

        // Assert: Firely drops the date component because the shipped cast is case-sensitive.
        dateExpression.ShouldBe("value.as(DateTime) | value.as(Period)");
        firelyDate.ShouldBeEmpty();

        // Ignixa accepts the legacy alias and evaluates that same component successfully.
        ignixaDate
            .Select(result => $"{result.InstanceType}|{result.Value}")
            .ShouldBe(["dateTime|2024-06-15T08:00:00Z"]);

        // The independent failure is unresolved component metadata, not date evaluation.
        composite.Component[0].ResolvedSearchParameter.ShouldBeNull();

        // Consequently the production Ignixa indexer also emits no composite entry.
        finalCompositeEntries.ShouldBeEmpty();
    }

    private static ITypedElement ParseNativeInput() =>
        new FhirJsonParser()
            .Parse<Resource>(ObservationJson)
            .ToTypedElement();
}
