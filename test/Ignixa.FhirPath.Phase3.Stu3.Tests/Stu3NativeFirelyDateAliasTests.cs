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
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Specification.Extensions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ignixa.FhirPath.Phase3.Stu3.Tests;

public class Stu3NativeFirelyDateAliasTests
{
    private const string GoalJson = """
        {
          "resourceType": "Goal",
          "status": "in-progress",
          "description": { "text": "Native Date alias probe" },
          "subject": { "reference": "Patient/example" },
          "startDate": "2024-06-15",
          "target": [
            { "dueDate": "2024-07-01" }
          ]
        }
        """;

    private static readonly IFhirSchemaProvider Schema = FhirVersion.Stu3.GetSchemaProvider();

    [Theory]
    [InlineData("start-date", "Goal.start.as(Date)", "2024-06-15", 2024, 6, 15)]
    [InlineData("target-date", "Goal.target.due.as(Date)", "2024-07-01", 2024, 7, 1)]
    public void GivenNativeFirelyGoal_WhenShippedDateAliasEvaluated_ThenKnownIndexDivergenceIsVisible(
        string searchParameterCode,
        string shippedExpression,
        string expectedValue,
        int year,
        int month,
        int day)
    {
        // Arrange
        var definitions = CreateDefinitions();
        string actualExpression = definitions
            .GetSearchParameter("Goal", searchParameterCode)
            .Expression;

        // Act
        var (firely, ignixa) = Evaluate(actualExpression);
        DateTimeSearchValue indexed = Index(searchParameterCode, definitions);

        // Assert
        actualExpression.ShouldBe(shippedExpression);
        firely.ShouldBeEmpty();
        Render(ignixa).ShouldBe([$"date|{expectedValue}"]);
        var start = new DateTimeOffset(year, month, day, 0, 0, 0, TimeSpan.Zero);
        indexed.Start.ShouldBe(start);
        indexed.End.ShouldBe(start.AddDays(1).AddTicks(-1));
    }

    [Theory]
    [InlineData("Goal.start.as(date)", "2024-06-15")]
    [InlineData("Goal.target.due.as(date)", "2024-07-01")]
    public void GivenNativeFirelyGoal_WhenLowercaseDateCastEvaluated_ThenProvidersReturnTheDate(
        string expression,
        string expectedValue)
    {
        // Arrange
        IReadOnlyList<string> expected = [$"date|{expectedValue}"];

        // Act
        var (firely, ignixa) = Evaluate(expression);

        // Assert
        Render(firely).ShouldBe(expected);
        Render(ignixa).ShouldBe(expected);
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

    private static DateTimeSearchValue Index(
        string searchParameterCode,
        SearchParameterDefinitionManager definitions)
    {
        IElement ignixaInput = ParseNativeInput().ToIgnixaElement();
        var indexer = SearchIndexerFactory.CreateInstance(
            Schema,
            NullLoggerFactory.Instance,
            definitions,
            NullFhirBaseUriProvider.Instance);

        return indexer.Extract(ignixaInput)
            .Where(entry => entry.SearchParameter.Code == searchParameterCode)
            .Select(entry => entry.Value)
            .Cast<DateTimeSearchValue>()
            .ShouldHaveSingleItem();
    }

    private static ITypedElement ParseNativeInput() =>
        new FhirJsonParser()
            .Parse<Resource>(GoalJson)
            .ToTypedElement();

    private static IReadOnlyList<string> Render(IEnumerable<ITypedElement> results) =>
        results.Select(result => $"{result.InstanceType}|{result.Value}").ToArray();
}
