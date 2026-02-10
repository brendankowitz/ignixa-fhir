/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Unit tests for NodeEvaluationHandler functionality in FhirPathEvaluator.
 * Ensures per-node evaluation details are correctly captured during debug tracing.
 */

using System.Collections.Immutable;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Expressions;
using Ignixa.FhirPath.Parser;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Ignixa.Specification.Extensions;
using Shouldly;

namespace Ignixa.FhirPath.Tests.Evaluation;

/// <summary>
/// Tests for NodeEvaluationHandler callback mechanism in FhirPathEvaluator.
/// Verifies that per-node evaluation details are correctly captured for debug tracing.
/// </summary>
public class NodeEvaluationHandlerTests
{
    private readonly FhirPathParser _parser = new();
    private readonly FhirPathEvaluator _evaluator = new();
    private readonly IFhirSchemaProvider _r4Provider;

    public NodeEvaluationHandlerTests()
    {
        _r4Provider = FhirVersion.R4.GetSchemaProvider();
    }

    private IElement CreatePatientElement()
    {
        var patientJson = """
        {
            "resourceType": "Patient",
            "id": "example",
            "name": [
                {
                    "family": "Doe",
                    "given": ["John"]
                },
                {
                    "use": "usual",
                    "given": ["Johnny"]
                }
            ],
            "gender": "male",
            "birthDate": "1970-01-01"
        }
        """;

        var resource = ResourceJsonNode.Parse(patientJson);
        return resource.ToElement(_r4Provider);
    }

    [Fact]
    public void GivenSimpleChildExpression_WhenEvaluatingWithHandler_ThenCapturesNodeEvaluation()
    {
        // Arrange
        var patient = CreatePatientElement();
        var expression = _parser.Parse("name");
        var entries = new List<NodeEvaluationEntry>();
        var context = new EvaluationContext().WithNodeEvaluationHandler(entry => entries.Add(entry));

        // Act
        var results = _evaluator.Evaluate(patient, expression, context).ToList();

        // Assert
        results.ShouldNotBeEmpty();
        results.Count.ShouldBe(2); // Two name elements

        entries.ShouldNotBeEmpty();

        // Find entries that produced HumanName results
        var nameEntry = entries.FirstOrDefault(e =>
            e.Results.Count > 0 && e.Results.Any(r => r.InstanceType == "HumanName"));
        nameEntry.ShouldNotBeNull();

        // Verify Results contain HumanName elements
        nameEntry.Results.ShouldAllBe(r => r.InstanceType == "HumanName");
        nameEntry.Results[0].Location.ShouldBe("Patient.name[0]");
        nameEntry.Results[1].Location.ShouldBe("Patient.name[1]");
    }

    [Fact]
    public void GivenNestedExpression_WhenEvaluatingWithHandler_ThenCapturesAllNodeEvaluations()
    {
        // Arrange
        var patient = CreatePatientElement();
        var expression = _parser.Parse("name.given");
        var entries = new List<NodeEvaluationEntry>();
        var context = new EvaluationContext().WithNodeEvaluationHandler(entry => entries.Add(entry));

        // Act
        var results = _evaluator.Evaluate(patient, expression, context).ToList();

        // Assert
        results.ShouldNotBeEmpty();
        results.Count.ShouldBe(2); // "John" and "Johnny"

        entries.ShouldNotBeEmpty();

        // Find entry that produced string results (from evaluating "given" on HumanName)
        var givenEntry = entries.FirstOrDefault(e =>
            e.Results.Count > 0 && e.Results.Any(r => r.InstanceType == "string" && r.Value is string));
        givenEntry.ShouldNotBeNull();

        // Verify results are the string values
        givenEntry.Results.Count.ShouldBe(2);
        givenEntry.Results[0].Value.ShouldBe("John");
        givenEntry.Results[1].Value.ShouldBe("Johnny");
    }

    [Fact]
    public void GivenExpressionWithFunction_WhenEvaluatingWithHandler_ThenCapturesFunctionEvaluation()
    {
        // Arrange
        var patient = CreatePatientElement();
        var expression = _parser.Parse("name.first()");
        var entries = new List<NodeEvaluationEntry>();
        var context = new EvaluationContext().WithNodeEvaluationHandler(entry => entries.Add(entry));

        // Act
        var results = _evaluator.Evaluate(patient, expression, context).ToList();

        // Assert
        results.ShouldNotBeEmpty();
        results.Count.ShouldBe(1); // first() returns single result

        entries.ShouldNotBeEmpty();

        // Find entry for first() function - should have single HumanName result
        var firstEntry = entries.FirstOrDefault(e =>
            e.Expression is FunctionCallExpression func && func.FunctionName == "first" &&
            e.Results.Count == 1 && e.Results[0].InstanceType == "HumanName");
        firstEntry.ShouldNotBeNull();

        // Verify the function returned the first name
        firstEntry.Results[0].Location.ShouldBe("Patient.name[0]");
    }

    [Fact]
    public void GivenExpressionWithIndexedLoop_WhenEvaluatingWithHandler_ThenCapturesIndexValues()
    {
        // Arrange
        var patient = CreatePatientElement();
        // Expression using $index within where() predicate
        var expression = _parser.Parse("name.where($index = 0)");
        var entries = new List<NodeEvaluationEntry>();
        var context = new EvaluationContext().WithNodeEvaluationHandler(entry => entries.Add(entry));

        // Act
        var results = _evaluator.Evaluate(patient, expression, context).ToList();

        // Assert
        results.ShouldNotBeEmpty();
        results.Count.ShouldBe(1); // Only first name (index 0)

        entries.ShouldNotBeEmpty();

        // Find entries with Index set (inside the where predicate)
        var indexedEntries = entries.Where(e => e.Index.HasValue).ToList();
        indexedEntries.ShouldNotBeEmpty();

        // At least one entry should have Index = 0
        indexedEntries.Any(e => e.Index == 0).ShouldBeTrue();
    }

    [Fact]
    public void GivenConstantExpression_WhenEvaluatingWithHandler_ThenCapturesConstantEvaluation()
    {
        // Arrange
        var patient = CreatePatientElement();
        var expression = _parser.Parse("42");
        var entries = new List<NodeEvaluationEntry>();
        var context = new EvaluationContext().WithNodeEvaluationHandler(entry => entries.Add(entry));

        // Act
        var results = _evaluator.Evaluate(patient, expression, context).ToList();

        // Assert
        results.ShouldNotBeEmpty();
        results.Count.ShouldBe(1);
        results[0].Value.ShouldBe(42);

        entries.ShouldNotBeEmpty();

        var constantEntry = entries.FirstOrDefault(e => e.Expression is ConstantExpression);
        constantEntry.ShouldNotBeNull();

        // Constant expression should return its value
        constantEntry.Results.Count.ShouldBe(1);
        constantEntry.Results[0].Value.ShouldBe(42);
    }

    [Fact]
    public void GivenBinaryExpression_WhenEvaluatingWithHandler_ThenCapturesBinaryOperation()
    {
        // Arrange
        var patient = CreatePatientElement();
        var expression = _parser.Parse("1 + 2");
        var entries = new List<NodeEvaluationEntry>();
        var context = new EvaluationContext().WithNodeEvaluationHandler(entry => entries.Add(entry));

        // Act
        var results = _evaluator.Evaluate(patient, expression, context).ToList();

        // Assert
        results.ShouldNotBeEmpty();
        results[0].Value.ShouldBe(3);

        entries.ShouldNotBeEmpty();

        // Should have entries for left constant (1), right constant (2), and binary operator (+)
        var leftEntry = entries.FirstOrDefault(e => e.Expression is ConstantExpression c && Equals(c.Value, 1));
        var rightEntry = entries.FirstOrDefault(e => e.Expression is ConstantExpression c && Equals(c.Value, 2));
        var binaryEntry = entries.FirstOrDefault(e => e.Expression is BinaryExpression bin && bin.Operator == "+");

        leftEntry.ShouldNotBeNull();
        rightEntry.ShouldNotBeNull();
        binaryEntry.ShouldNotBeNull();

        binaryEntry.Results.Count.ShouldBe(1);
        binaryEntry.Results[0].Value.ShouldBe(3);
    }

    [Fact]
    public void GivenExpressionWithLocation_WhenGettingKey_ThenFormatsPositionLengthName()
    {
        // Arrange
        var patient = CreatePatientElement();
        var expression = _parser.Parse("name");
        var entries = new List<NodeEvaluationEntry>();
        var context = new EvaluationContext().WithNodeEvaluationHandler(entry => entries.Add(entry));

        // Act
        var results = _evaluator.Evaluate(patient, expression, context).ToList();

        // Assert
        results.ShouldNotBeEmpty(); // Force materialization

        // Find any entry with location information
        var entryWithLocation = entries.FirstOrDefault(e => e.Expression.Location != null);
        entryWithLocation.ShouldNotBeNull();

        var key = entryWithLocation.GetKey();

        // Key should be in format "position,length,name"
        key.ShouldContain(","); // Should have position,length,name format
        key.Split(',').Length.ShouldBe(3);
    }

    [Fact]
    public void GivenExpressionWithoutLocation_WhenGettingKey_ThenReturnsNameOnly()
    {
        // Arrange - Create an expression manually without location info
        var expression = new ConstantExpression(42);
        var entries = new List<NodeEvaluationEntry>();
        var context = new EvaluationContext().WithNodeEvaluationHandler(entry => entries.Add(entry));

        // Act
        var results = _evaluator.Evaluate(CreatePatientElement(), expression, context).ToList();

        // Assert
        results.ShouldNotBeEmpty(); // Force materialization
        var constantEntry = entries.FirstOrDefault(e => e.Expression is ConstantExpression);
        constantEntry.ShouldNotBeNull();

        var key = constantEntry.GetKey();

        // Key should be just the formatted constant value when no location
        key.ShouldBe("42");
    }

    [Fact]
    public void GivenHandlerThrowsException_WhenEvaluating_ThenExceptionPropagates()
    {
        // Arrange
        var patient = CreatePatientElement();
        var expression = _parser.Parse("name");
        var context = new EvaluationContext().WithNodeEvaluationHandler(entry =>
        {
            throw new InvalidOperationException("Handler error - should propagate");
        });

        // Act & Assert - Exception should propagate to caller (fail-fast principle)
        var exception = Should.Throw<InvalidOperationException>(() =>
        {
            var _ = _evaluator.Evaluate(patient, expression, context).ToList();
        });

        exception.Message.ShouldBe("Handler error - should propagate");
    }

    [Fact]
    public void GivenNoHandler_WhenEvaluating_ThenPerformanceIsNotImpacted()
    {
        // Arrange
        var patient = CreatePatientElement();
        var expression = _parser.Parse("name.given");
        var contextWithoutHandler = new EvaluationContext();
        var contextWithHandler = new EvaluationContext().WithNodeEvaluationHandler(_ => { });

        // Act & Assert - Both should return same results
        var resultsWithoutHandler = _evaluator.Evaluate(patient, expression, contextWithoutHandler).ToList();
        var resultsWithHandler = _evaluator.Evaluate(patient, expression, contextWithHandler).ToList();

        resultsWithoutHandler.Count.ShouldBe(resultsWithHandler.Count);
        for (int i = 0; i < resultsWithoutHandler.Count; i++)
        {
            resultsWithoutHandler[i].Value.ShouldBe(resultsWithHandler[i].Value);
        }
    }

    [Fact]
    public void GivenEmptyResults_WhenEvaluatingWithHandler_ThenCapturesEmptyResultsList()
    {
        // Arrange
        var patient = CreatePatientElement();
        var expression = _parser.Parse("name.where(use = 'nickname')"); // No names with use='nickname'
        var entries = new List<NodeEvaluationEntry>();
        var context = new EvaluationContext().WithNodeEvaluationHandler(entry => entries.Add(entry));

        // Act
        var results = _evaluator.Evaluate(patient, expression, context).ToList();

        // Assert
        results.ShouldBeEmpty();

        entries.ShouldNotBeEmpty();

        // The where() function should have an entry with empty results
        var whereEntry = entries.FirstOrDefault(e => e.Expression is FunctionCallExpression func && func.FunctionName == "where");
        whereEntry.ShouldNotBeNull();
        whereEntry.Results.ShouldBeEmpty();
    }

    [Fact]
    public void GivenComplexExpression_WhenGettingKeys_ThenFormatsDifferentExpressionTypes()
    {
        // Arrange
        var patient = CreatePatientElement();
        var expression = _parser.Parse("name.first().family + ' suffix'");
        var entries = new List<NodeEvaluationEntry>();
        var context = new EvaluationContext().WithNodeEvaluationHandler(entry => entries.Add(entry));

        // Act
        var results = _evaluator.Evaluate(patient, expression, context).ToList();

        // Assert
        results.ShouldNotBeEmpty(); // Force materialization
        entries.ShouldNotBeEmpty();

        // Verify different expression types exist
        var hasChild = entries.Any(e => e.Expression is ChildExpression or PropertyAccessExpression);
        var hasFunc = entries.Any(e => e.Expression is FunctionCallExpression);
        var hasBinary = entries.Any(e => e.Expression is BinaryExpression);
        var hasConstant = entries.Any(e => e.Expression is ConstantExpression);

        hasChild.ShouldBeTrue();
        hasFunc.ShouldBeTrue();
        hasBinary.ShouldBeTrue();
        hasConstant.ShouldBeTrue();

        // Verify each type produces a key
        foreach (var entry in entries)
        {
            var key = entry.GetKey();
            key.ShouldNotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void GivenThisAndIndex_WhenEvaluatingInLoop_ThenCapturesThisAndIndexValues()
    {
        // Arrange
        var patient = CreatePatientElement();
        // Expression that uses both $this and $index
        var expression = _parser.Parse("name.select($this.given.first() + ' at ' + $index.toString())");
        var entries = new List<NodeEvaluationEntry>();
        var context = new EvaluationContext().WithNodeEvaluationHandler(entry => entries.Add(entry));

        // Act
        var results = _evaluator.Evaluate(patient, expression, context).ToList();

        // Assert
        results.ShouldNotBeEmpty();

        entries.ShouldNotBeEmpty();

        // Find entries with both ThisElement and Index set (inside select)
        var thisAndIndexEntries = entries.Where(e => e.ThisElement != null && e.Index.HasValue).ToList();
        thisAndIndexEntries.ShouldNotBeEmpty();

        // Verify ThisElement is a HumanName and Index is set
        var firstEntry = thisAndIndexEntries.FirstOrDefault(e => e.Index == 0);
        firstEntry.ShouldNotBeNull();
        firstEntry.ThisElement.ShouldNotBeNull();
        firstEntry.ThisElement!.InstanceType.ShouldBe("HumanName");
    }
}
