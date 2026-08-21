/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Regression coverage for deriving System-value construction provenance from the focus AST.
 */

using Ignixa.Abstractions;
using Ignixa.FhirPath.Analysis;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Parser;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Ignixa.Specification.Extensions;

namespace Ignixa.FhirPath.Tests.Analysis;

public class AnalyzerCastProvenanceRegressionTests
{
    private const string PatientJson = """
        {
          "resourceType": "Patient",
          "id": "cast-provenance",
          "active": true,
          "multipleBirthInteger": 3,
          "name": [ { "family": "FHIR" } ]
        }
        """;

    private readonly FhirPathParser _parser = new();
    private readonly FhirPathEvaluator _evaluator = new();

    public static TheoryData<FhirVersion, string> TemporalStringCasts => new()
    {
        { FhirVersion.R5, "(@2024-01).ofType(String)" },
        { FhirVersion.R5, "(@T12:00:00).ofType(String)" },
        { FhirVersion.R5, "(@2024-01-01T10:00:00Z).ofType(String)" },
        { FhirVersion.R5, "(@2024-01-01).as(String)" },
        { FhirVersion.R6, "(@2024-01).ofType(String)" },
        { FhirVersion.R6, "(@T12:00:00).ofType(String)" },
        { FhirVersion.R6, "(@2024-01-01T10:00:00Z).ofType(String)" },
        { FhirVersion.R6, "(@2024-01-01).as(String)" },
    };

    [Theory]
    [MemberData(nameof(TemporalStringCasts))]
    public void GivenATemporalLiteral_WhenCastToSystemString_ThenEvaluationAndAnalysisAreEmpty(
        FhirVersion version,
        string expression)
    {
        // Arrange
        var schema = version.GetSchemaProvider();
        var subject = ResourceJsonNode.Parse(PatientJson).ToElement(schema);
        string emptyExpression = expression.Contains(".as(", StringComparison.Ordinal)
            ? "{}.as(String)"
            : "{}.ofType(String)";
        string[] expressions = [expression, emptyExpression];

        // Act
        var outcomes = expressions
            .Select(currentExpression => ObserveCast(subject, currentExpression, schema))
            .ToArray();

        // Assert
        outcomes.ShouldBe(
        [
            new(expression, 0, true, true, false),
            new(emptyExpression, 0, true, true, false),
        ]);
    }

    [Theory]
    [InlineData(FhirVersion.R5)]
    [InlineData(FhirVersion.R6)]
    public void GivenIifBranchReachability_WhenCastingToSystemString_ThenAnalysisIncludesExactlyTheReachableBranches(
        FhirVersion version)
    {
        // Arrange
        var schema = version.GetSchemaProvider();
        var subject = ResourceJsonNode.Parse(PatientJson).ToElement(schema);
        string[] expressions =
        [
            "(iif(true, name.family, 'constructed')).ofType(String)",
            "(iif(false, 'constructed', name.family)).ofType(String)",
            "(iif(active, name.family, 'constructed')).ofType(String)",
        ];

        // Act
        var outcomes = expressions
            .Select(expression => ObserveCast(subject, expression, schema))
            .ToArray();

        // Assert
        outcomes.ShouldBe(
        [
            new(expressions[0], 0, true, true, false),
            new(expressions[1], 0, true, true, false),
            new(expressions[2], 0, true, false, true),
        ]);
    }

    [Theory]
    [InlineData(FhirVersion.R5)]
    [InlineData(FhirVersion.R6)]
    public void GivenAUnionContainingAConstructedString_WhenCastToSystemString_ThenItSurvivesInEitherOrder(
        FhirVersion version)
    {
        // Arrange
        var schema = version.GetSchemaProvider();
        var subject = ResourceJsonNode.Parse(PatientJson).ToElement(schema);
        string[] expressions =
        [
            "(name.family | 'constructed').ofType(System.String)",
            "('constructed' | name.family).ofType(System.String)",
        ];

        // Act
        var results = expressions
            .Select(expression => (
                Evaluated: Evaluate(subject, expression, schema),
                Analysed: new FhirPathAnalyzer(schema).Analyze(expression, "Patient")))
            .ToList();

        // Assert
        results.Count.ShouldBe(2);
        foreach (var result in results)
        {
            result.Evaluated.ShouldHaveSingleItem().Value.ShouldBe("constructed");
            result.Analysed.IsValid.ShouldBeTrue();
            result.Analysed.HasAlwaysEmptySubexpression.ShouldBeFalse();
        }
    }

    [Theory]
    [InlineData(FhirVersion.R5)]
    [InlineData(FhirVersion.R6)]
    public void GivenArithmeticWithOneConstructedOperand_WhenCastToSystemInteger_ThenBothOperandOrdersSurvive(
        FhirVersion version)
    {
        // Arrange
        var schema = version.GetSchemaProvider();
        var subject = ResourceJsonNode.Parse(PatientJson).ToElement(schema);
        string[] expressions =
        [
            "(multipleBirthInteger + 4).ofType(System.Integer)",
            "(4 + multipleBirthInteger).ofType(System.Integer)",
        ];

        // Act
        var results = expressions
            .Select(expression => (
                Evaluated: Evaluate(subject, expression, schema),
                Analysed: new FhirPathAnalyzer(schema).Analyze(expression, "Patient")))
            .ToList();

        // Assert
        results.Count.ShouldBe(2);
        foreach (var result in results)
        {
            result.Evaluated.ShouldHaveSingleItem().Value.ShouldBe(7);
            result.Analysed.HasAlwaysEmptySubexpression.ShouldBeFalse();
        }
    }

    [Theory]
    [InlineData(FhirVersion.R5)]
    [InlineData(FhirVersion.R6)]
    public void GivenStringAdditionWithNavigatedOperands_WhenCastToSystemString_ThenConstructedResultSurvives(
        FhirVersion version)
    {
        // Arrange
        var schema = version.GetSchemaProvider();
        var subject = ResourceJsonNode.Parse(PatientJson).ToElement(schema);
        const string expression =
            "(name.family.first() + name.family.first()).ofType(System.String)";

        // Act
        var evaluated = Evaluate(subject, expression, schema);
        var analysed = new FhirPathAnalyzer(schema).Analyze(expression, "Patient");

        // Assert
        evaluated.ShouldHaveSingleItem().Value.ShouldBe("FHIRFHIR");
        analysed.HasAlwaysEmptySubexpression.ShouldBeFalse();
        analysed.InferredTypes.Types.ShouldNotBeEmpty();
    }

    [Theory]
    [InlineData(FhirVersion.R5)]
    [InlineData(FhirVersion.R6)]
    public void GivenToQuantityReturnMetadata_WhenCastToQuantity_ThenAnalysisUsesTheEvaluatorRuntimeSpelling(
        FhirVersion version)
    {
        // Arrange
        var schema = version.GetSchemaProvider();
        var subject = ResourceJsonNode.Parse(PatientJson).ToElement(schema);
        string[] expressions =
        [
            "(name.family | 'constructed').ofType(System.String)",
            "1.toQuantity().ofType(Quantity)",
            "1.toQuantity().as(Quantity)",
            "1.toQuantity().ofType(quantity)",
        ];

        // Act
        var outcomes = expressions
            .Select(expression => ObserveCast(subject, expression, schema))
            .ToArray();
        var constructedType = new FhirPathAnalyzer(schema)
            .Analyze("1.toQuantity()", "Patient")
            .InferredTypes.Types
            .ShouldHaveSingleItem();

        // Assert
        constructedType.TypeName.ShouldBe("Quantity");
        outcomes.ShouldBe(
        [
            new(expressions[0], 1, true, false, true),
            new(expressions[1], 1, true, false, true),
            new(expressions[2], 1, true, false, true),
            new(expressions[3], 0, true, true, false),
        ]);
    }

    [Theory]
    [InlineData(FhirVersion.R5)]
    [InlineData(FhirVersion.R6)]
    public void GivenUnaryNumericExpressions_WhenCast_ThenAnalysisPreservesPassThroughAndConstructionSemantics(
        FhirVersion version)
    {
        // Arrange
        var schema = version.GetSchemaProvider();
        var subject = ResourceJsonNode.Parse(PatientJson).ToElement(schema);
        string[] expressions =
        [
            "(name.family | 'constructed').ofType(System.String)",
            "(+{}).ofType(String)",
            "(-{}).ofType(Integer)",
            "(-multipleBirth).ofType(Integer)",
            "(-5).ofType(Decimal)",
            "(-5.5).ofType(Integer)",
            "(-(5 'mg')).ofType(Quantity)",
            "(-(5 'mg')).ofType(Integer)",
            "(+multipleBirth).ofType(Integer)",
        ];

        // Act
        var outcomes = expressions
            .Select(expression => ObserveCast(subject, expression, schema))
            .ToArray();

        // Assert
        outcomes.ShouldBe(
        [
            new(expressions[0], 1, true, false, true),
            new(expressions[1], 0, true, true, false),
            new(expressions[2], 0, true, true, false),
            new(expressions[3], 1, true, false, true),
            new(expressions[4], 0, true, true, false),
            new(expressions[5], 0, true, true, false),
            new(expressions[6], 1, true, false, true),
            new(expressions[7], 0, true, true, false),
            new(expressions[8], 0, true, true, false),
        ]);
    }

    [Theory]
    [InlineData("name.family.unknownFunction().ofType(String)")]
    [InlineData("%missing.ofType(String)")]
    [InlineData("name.family[0].ofType(String)")]
    [InlineData("missingProperty.ofType(String)")]
    public void GivenUncertainConstructionProvenance_WhenCastToSystemString_ThenAnalysisStaysSilent(
        string expression)
    {
        // Arrange
        var schema = FhirVersion.R5.GetSchemaProvider();

        // Act
        var analysed = new FhirPathAnalyzer(schema).Analyze(expression, "Patient");

        // Assert
        analysed.HasAlwaysEmptySubexpression.ShouldBeFalse();
    }

    [Theory]
    [InlineData("ofType")]
    [InlineData("as")]
    public void GivenUncertainConstructionProvenance_WhenCastToAValidTarget_ThenAnalysisDoesNotClaimAlwaysEmpty(
        string cast)
    {
        // Arrange
        var schema = FhirVersion.R5.GetSchemaProvider();
        var subject = ResourceJsonNode.Parse(PatientJson).ToElement(schema);
        string[] targets = ["System.uri", "code", "unsignedInt", "Reference", "Patient"];
        string[] expressions = targets
            .Select(target => $"name.family[0].{cast}({target})")
            .ToArray();

        // Act
        var outcomes = expressions
            .Select(expression => ObserveCast(subject, expression, schema))
            .ToArray();

        // Assert
        outcomes.ShouldBe(
        [
            new(expressions[0], 0, true, false, true),
            new(expressions[1], 0, true, false, true),
            new(expressions[2], 0, true, false, true),
            new(expressions[3], 0, true, false, true),
            new(expressions[4], 0, true, false, true),
        ]);
    }

    [Theory]
    [InlineData(FhirVersion.R5)]
    [InlineData(FhirVersion.R6)]
    public void GivenNegatedWideIntegerLiteral_WhenCastToDecimal_ThenConstructedResultSurvives(
        FhirVersion version)
    {
        // Arrange
        var schema = version.GetSchemaProvider();
        var subject = ResourceJsonNode.Parse(PatientJson).ToElement(schema);
        const string expression = "(-3000000000L).ofType(Decimal)";

        // Act
        var evaluated = Evaluate(subject, expression, schema);
        var analysed = new FhirPathAnalyzer(schema).Analyze(expression, "Patient");

        // Assert
        evaluated.ShouldHaveSingleItem().Value.ShouldBe(-3000000000m);
        analysed.HasAlwaysEmptySubexpression.ShouldBeFalse();
        analysed.InferredTypes.Types.ShouldNotBeEmpty();
    }

    [Theory]
    [InlineData(FhirVersion.R5)]
    [InlineData(FhirVersion.R6)]
    public void GivenCoalesceWithALaterReachableConstructedArgument_WhenCastToSystemString_ThenAnalysisIsNotAlwaysEmpty(
        FhirVersion version)
    {
        // Arrange
        var schema = version.GetSchemaProvider();
        var subject = ResourceJsonNode.Parse(PatientJson).ToElement(schema);
        const string expression = "coalesce(name.prefix, 'constructed').ofType(String)";

        // Act
        var evaluated = Evaluate(subject, expression, schema);
        var analysed = new FhirPathAnalyzer(schema).Analyze(expression, "Patient");

        // Assert
        evaluated.ShouldHaveSingleItem().Value.ShouldBe("constructed");
        analysed.IsValid.ShouldBeTrue();
        analysed.HasAlwaysEmptySubexpression.ShouldBeFalse();
    }

    [Theory]
    [InlineData(FhirVersion.R5)]
    [InlineData(FhirVersion.R6)]
    public void GivenGeneratedReturnMetadata_WhenCastToSystemString_ThenAnalysisUsesItsSchemaClassification(
        FhirVersion version)
    {
        // Arrange
        var schema = version.GetSchemaProvider();
        var subject = ResourceJsonNode.Parse(PatientJson).ToElement(schema);
        string[] expressions =
        [
            "name.family.type().ofType(String)",
            "extension('http://example.test/fhirpath-cast-provenance').ofType(String)",
        ];

        // Act
        var outcomes = expressions
            .Select(expression => ObserveCast(subject, expression, schema))
            .ToArray();

        // Assert
        outcomes.ShouldBe(
        [
            new(expressions[0], 0, true, false, true),
            new(expressions[1], 0, true, true, false),
        ]);
    }

    [Theory]
    [InlineData(FhirVersion.Stu3)]
    [InlineData(FhirVersion.R4)]
    [InlineData(FhirVersion.R4B)]
    [InlineData(FhirVersion.R5)]
    [InlineData(FhirVersion.R6)]
    public void GivenAChildNavigatedOffAConstructedQuantity_WhenCastToTheMemberSystemType_ThenAnalysisIsNotAlwaysEmpty(
        FhirVersion version)
    {
        // Arrange
        var schema = version.GetSchemaProvider();
        var subject = ResourceJsonNode.Parse(PatientJson).ToElement(schema);
        string[] expressions =
        [
            "(1 'mg').value.ofType(System.Decimal)",
            "(1 'mg').unit.ofType(System.String)",
        ];

        // Act
        var outcomes = expressions
            .Select(expression => ObserveCast(subject, expression, schema))
            .ToArray();

        // Assert
        outcomes.ShouldAllBe(outcome => outcome.EvaluatedCount == 1);
        outcomes.ShouldAllBe(outcome => !outcome.HasAlwaysEmptySubexpression);
    }

    public static TheoryData<FhirVersion, bool, int> NavigatedFhirStringCastVerdicts => new()
    {
        { FhirVersion.Stu3, false, 1 },
        { FhirVersion.R4, false, 1 },
        { FhirVersion.R4B, false, 1 },
        { FhirVersion.R5, true, 0 },
        { FhirVersion.R6, true, 0 },
    };

    [Theory]
    [MemberData(nameof(NavigatedFhirStringCastVerdicts))]
    public void GivenAChildNavigatedOffNavigatedFhirData_WhenCastToASystemType_ThenAnalysisKeepsItsVerdict(
        FhirVersion version,
        bool expectedAlwaysEmpty,
        int expectedCount)
    {
        // Arrange
        var schema = version.GetSchemaProvider();
        var subject = ResourceJsonNode.Parse(PatientJson).ToElement(schema);
        const string expression = "Patient.name.family.ofType(System.String)";

        // Act
        var outcome = ObserveCast(subject, expression, schema);

        // Assert
        outcome.ShouldBe(new(expression, expectedCount, true, expectedAlwaysEmpty, expectedCount > 0));
    }

    private IReadOnlyList<IElement> Evaluate(IElement subject, string expression, ISchema schema) =>
        _evaluator
            .Evaluate(
                subject,
                _parser.Parse(expression),
                new EvaluationContext { Resource = subject, RootResource = subject, Schema = schema })
            .ToList();

    private CastOutcome ObserveCast(IElement subject, string expression, IFhirSchemaProvider schema)
    {
        var analysed = new FhirPathAnalyzer(schema).Analyze(expression, "Patient");
        int? evaluatedCount;
        string? evaluationError;
        try
        {
            evaluatedCount = Evaluate(subject, expression, schema).Count;
            evaluationError = null;
        }
        catch (Exception exception)
        {
            evaluatedCount = null;
            evaluationError = $"{exception.GetType().Name}: {exception.Message}";
        }

        return new(
            expression,
            evaluatedCount,
            analysed.IsValid,
            analysed.HasAlwaysEmptySubexpression,
            analysed.InferredTypes.Types.Count > 0,
            evaluationError);
    }

    private sealed record CastOutcome(
        string Expression,
        int? EvaluatedCount,
        bool IsValid,
        bool HasAlwaysEmptySubexpression,
        bool HasInferredTypes,
        string? EvaluationError = null);
}
