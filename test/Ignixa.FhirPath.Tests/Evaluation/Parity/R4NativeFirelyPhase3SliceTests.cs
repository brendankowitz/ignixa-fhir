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

namespace Ignixa.FhirPath.Tests.Evaluation.Parity;

public class R4NativeFirelyPhase3SliceTests
{
    private const string ShippedExpression = "value.as(DateTime) | value.as(Period)";
    private const string LowercaseControlExpression = "value.as(dateTime) | value.as(Period)";
    private const string ObservationJson = """
        {
          "resourceType": "Observation",
          "id": "r4-cast-probe",
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

    private static readonly IFhirSchemaProvider Schema = FhirVersion.R4.GetSchemaProvider();

    [Fact]
    public void GivenNativeR4Choice_WhenShippedCapitalizedDateCastEvaluated_ThenKnownDivergenceIsVisible()
    {
        // Arrange
        var definitions = CreateDefinitions();
        string componentExpression = definitions
            .GetSearchParameter("Observation", "code-value-date")
            .Component[1]
            .Expression;

        // Act
        var (firely, ignixa) = Evaluate(componentExpression);

        // Assert
        componentExpression.ShouldBe(ShippedExpression);
        firely.ShouldBeEmpty();
        Render(ignixa).ShouldBe(["dateTime|2024-06-15T08:00:00Z"]);
    }

    [Fact]
    public void GivenNativeR4Choice_WhenLowercaseDateCastEvaluated_ThenProvidersReturnTheDate()
    {
        // Arrange
        IReadOnlyList<string> expected = ["dateTime|2024-06-15T08:00:00Z"];

        // Act
        var (firely, ignixa) = Evaluate(LowercaseControlExpression);

        // Assert
        Render(firely).ShouldBe(expected);
        Render(ignixa).ShouldBe(expected);
    }

    [Fact]
    public void GivenNativeR4Choice_WhenProductionIndexed_ThenCapitalizedDateCompositeIsEmitted()
    {
        // Arrange
        var definitions = CreateDefinitions();
        ITypedElement nativeInput = ParseNativeInput();
        IElement ignixaInput = nativeInput.ToIgnixaElement();
        var indexer = SearchIndexerFactory.CreateInstance(
            Schema,
            NullLoggerFactory.Instance,
            definitions,
            NullFhirBaseUriProvider.Instance);

        // Act
        string[] values = indexer.Extract(ignixaInput)
            .Where(entry => entry.SearchParameter.Code == "code-value-date")
            .Select(entry => entry.Value.ToString() ?? "<null>")
            .ToArray();

        // Assert
        values.ShouldBe(
            ["(http://loinc.org|29463-7) $ (2024-06-15T08:00:00+00:00)"]);
    }

    private static SearchParameterDefinitionManager CreateDefinitions() =>
        new(Schema, NullLogger<SearchParameterDefinitionManager>.Instance);

    private static (
        IReadOnlyList<ITypedElement> Firely,
        IReadOnlyList<ITypedElement> Ignixa) Evaluate(string expression)
    {
        ITypedElement nativeInput = ParseNativeInput();
        var firely = nativeInput
            .Select(expression, new Hl7.Fhir.FhirPath.FhirEvaluationContext())
            .ToList();
        IElement ignixaInput = nativeInput.ToIgnixaElement();
        var context = new Ignixa.FhirPath.Evaluation.FhirEvaluationContext
        {
            Schema = Schema,
            Resource = ignixaInput,
            RootResource = ignixaInput,
        };
        var ignixa = Ignixa.FhirPath.Evaluation.TypedElementExtensions
            .Select(ignixaInput, expression, context)
            .Select(result => (ITypedElement)new TypedElementAdapter(result))
            .ToList();

        return (firely, ignixa);
    }

    private static ITypedElement ParseNativeInput() =>
        new FhirJsonParser()
            .Parse<Resource>(ObservationJson)
            .ToTypedElement();

    private static IReadOnlyList<string> Render(IEnumerable<ITypedElement> results) =>
        results.Select(result => $"{result.InstanceType}|{result.Value}").ToArray();
}
