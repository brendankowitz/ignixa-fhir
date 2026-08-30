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

/// <summary>
/// The STU3 <c>Observation-code-value-date</c> composite, which used to fail on both sides for
/// unrelated reasons and now fails on one.
/// </summary>
/// <remarks>
/// The corpus's clearest demonstration that final index equality can give a false answer: Firely dropped
/// this composite because the shipped date component casts with a capitalised <c>DateTime</c>, while
/// Ignixa selected the date successfully but dropped the same composite because STU3 publishes no
/// <c>Observation-code</c> for the code component. Two independent failures, one identical empty result,
/// and an index-only comparison reporting agreement. The component reference is now repaired - STU3
/// publishes that parameter under the multi-resource <c>clinical-code</c> URL - so Ignixa emits the
/// composite and Firely still does not, leaving the evaluator divergence visible in the index too.
/// </remarks>
public class Stu3CompositeComponentRepairTests
{
    private const string ObservationJson = """
        {
          "resourceType": "Observation",
          "id": "stu3-composite-repair-probe",
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
    public void GivenCodedObservation_WhenCompositeIndexed_ThenTheRepairedComponentLetsIgnixaEmitTheEntryFirelyCannot()
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

        // The code component now resolves: STU3 publishes it under the multi-resource clinical-code
        // URL, which is what CompositeComponentDefinitionRepairs redirects the dangling reference to.
        composite.Component[0].ResolvedSearchParameter.ShouldNotBeNull();
        composite.Component[0].ResolvedSearchParameter.Url.OriginalString
            .ShouldBe("http://hl7.org/fhir/SearchParameter/clinical-code");

        // So the production Ignixa indexer emits the composite entry, where Firely's empty date
        // component leaves it with nothing to emit.
        finalCompositeEntries.Length.ShouldBe(1);
        finalCompositeEntries[0].Value.ToString()
            .ShouldBe("(http://loinc.org|29463-7) $ (2024-06-15T08:00:00+00:00)");
    }

    private static ITypedElement ParseNativeInput() =>
        new FhirJsonParser()
            .Parse<Resource>(ObservationJson)
            .ToTypedElement();
}
