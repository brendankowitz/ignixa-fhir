using Hl7.Fhir.ElementModel;
using Hl7.Fhir.FhirPath;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Hl7.Fhir.Specification;
using Hl7.FhirPath;
using Ignixa.Abstractions;
using Ignixa.Extensions.FirelySdk;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Search.Indexing.Converters;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Specification.Extensions;

namespace Ignixa.FhirPath.Phase3.Stu3.Tests;

public class Stu3NativeFirelyPhase3SliceTests
{
    private const string ObservationJson = """
        {
          "resourceType": "Observation",
          "status": "final",
          "code": { "text": "test" },
          "valueString": "parity"
        }
        """;

    [Fact]
    public void GivenNativeFirelyChoice_WhenUppercaseStringCastEvaluated_ThenKnownIndexDivergenceIsVisible()
    {
        // Arrange
        const string expression = "Observation.value.as(String)";

        // Act
        var (firely, ignixa) = Evaluate(expression);

        // Assert
        firely.ShouldBeEmpty();
        Render(ignixa).ShouldBe(["string|parity"]);
        Index(firely).ShouldBeEmpty();
        Index(ignixa).ShouldBe(["parity"]);
    }

    [Fact]
    public void GivenNativeFirelyChoice_WhenLowercaseStringCastEvaluated_ThenProvidersAgree()
    {
        // Arrange
        const string expression = "Observation.value.as(string)";

        // Act
        var (firely, ignixa) = Evaluate(expression);

        // Assert
        IReadOnlyList<string> firelyResults = Render(firely);
        IReadOnlyList<string> firelyIndex = Index(firely);
        firelyResults.ShouldBe(["string|parity"]);
        firelyIndex.ShouldBe(["parity"]);
        firelyResults.ShouldBe(Render(ignixa));
        firelyIndex.ShouldBe(Index(ignixa));
    }

    [Fact]
    public void GivenNativeFirelyChoice_WhenIgnixaResultReturnsThroughAdapter_ThenConcreteChoiceTypeIsPreserved()
    {
        // Arrange
        const string expression = "Observation.value.as(String)";

        // Act
        var (_, ignixa) = Evaluate(expression);
        ITypedElement result = ignixa.ShouldHaveSingleItem();

        // Assert
        result.Name.ShouldBe("string");
        result.InstanceType.ShouldBe("string");
        result.Definition.ShouldNotBeNull();
        result.Definition.Type.ShouldHaveSingleItem().GetTypeName().ShouldBe("string");
    }

    private static (
        IReadOnlyList<ITypedElement> Firely,
        IReadOnlyList<ITypedElement> Ignixa) Evaluate(string expression)
    {
        ITypedElement input = ParseNativeInput();
        var firely = input.Select(expression, new Hl7.Fhir.FhirPath.FhirEvaluationContext()).ToList();
        IElement ignixaInput = input.ToIgnixaElement();
        var context = new Ignixa.FhirPath.Evaluation.FhirEvaluationContext
        {
            Schema = FhirVersion.Stu3.GetSchemaProvider(),
            Resource = ignixaInput,
            RootResource = ignixaInput,
        };
        var ignixa = Ignixa.FhirPath.Evaluation.TypedElementExtensions.Select(ignixaInput, expression, context)
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

    private static IReadOnlyList<string> Index(IEnumerable<ITypedElement> results)
    {
        var converter = new StringToStringSearchValueConverter();
        return results
            .Select(result => (IElement)new IgnixaElementAdapter(result))
            .SelectMany(converter.ConvertTo)
            .Cast<StringSearchValue>()
            .Select(value => value.String)
            .ToArray();
    }
}
