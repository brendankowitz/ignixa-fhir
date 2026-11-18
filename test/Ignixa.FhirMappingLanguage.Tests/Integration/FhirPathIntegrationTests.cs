/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Integration tests for FhirPath integration in mapping language.
 */

using Shouldly;
using Ignixa.FhirMappingLanguage.Evaluation;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Xunit;

namespace Ignixa.FhirMappingLanguage.Tests.Integration;

public class FhirPathIntegrationTests
{
    #region Helper Classes

    private class TestTypedElement : IElement
    {
        private readonly Dictionary<string, List<IElement>> _children = new();

        public TestTypedElement(string name, object? value = null, string instanceType = "string")
        {
            Name = name;
            Value = value;
            InstanceType = instanceType;
        }

        public string Name { get; }
        public string InstanceType { get; }
        public object? Value { get; }
        public string Location => string.Empty;
        public IType? Type => null;
        public bool HasPrimitiveValue => Value != null;

        public void AddChild(IElement child)
        {
            if (!_children.ContainsKey(child.Name))
            {
                _children[child.Name] = new List<IElement>();
            }
            _children[child.Name].Add(child);
        }

        public IReadOnlyList<IElement> Children(string? name = null)
        {
            if (name == null)
            {
                return _children.Values.SelectMany(list => list).ToList();
            }

            return _children.TryGetValue(name, out var children)
                ? children
                : new List<IElement>();
        }

        public T? Meta<T>() where T : class => null;
    }

    #endregion

    #region Basic Evaluation Tests

    [Fact]
    public void GivenFhirPathIntegration_WhenEvaluatingSimplePath_ThenReturnsResults()
    {
        // Arrange
        var integration = new FhirPathIntegration();
        var patient = new TestTypedElement("Patient");
        var name = new TestTypedElement("family", "Doe", "string");
        patient.AddChild(name);

        // Act
        var results = integration.Evaluate("family", patient).ToList();

        // Assert
        results.Count.ShouldBe(1);
        results[0].Value.ShouldBe("Doe");
    }

    [Fact]
    public void GivenFhirPathIntegration_WhenEvaluatingEmptyExpression_ThenReturnsEmpty()
    {
        // Arrange
        var integration = new FhirPathIntegration();
        var patient = new TestTypedElement("Patient");

        // Act
        var results = integration.Evaluate("", patient).ToList();

        // Assert
        results.ShouldBeEmpty();
    }

    [Fact]
    public void GivenFhirPathIntegration_WhenEvaluatingWhitespaceExpression_ThenReturnsEmpty()
    {
        // Arrange
        var integration = new FhirPathIntegration();
        var patient = new TestTypedElement("Patient");

        // Act
        var results = integration.Evaluate("   ", patient).ToList();

        // Assert
        results.ShouldBeEmpty();
    }

    #endregion

    #region Boolean Evaluation Tests

    [Fact]
    public void GivenFhirPathIntegration_WhenEvaluatingBooleanTrue_ThenReturnsTrue()
    {
        // Arrange
        var integration = new FhirPathIntegration();
        var element = new TestTypedElement("value", true, "boolean");

        // Act
        var result = integration.EvaluateBoolean("true", element);

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void GivenFhirPathIntegration_WhenEvaluatingBooleanFalse_ThenReturnsFalse()
    {
        // Arrange
        var integration = new FhirPathIntegration();
        var element = new TestTypedElement("value", false, "boolean");

        // Act
        var result = integration.EvaluateBoolean("false", element);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void GivenFhirPathIntegration_WhenEvaluatingEmptyResult_ThenReturnsFalse()
    {
        // Arrange
        var integration = new FhirPathIntegration();
        var patient = new TestTypedElement("Patient");

        // Act
        var result = integration.EvaluateBoolean("name.where(use='official')", patient);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void GivenFhirPathIntegration_WhenEvaluatingNonBooleanResult_ThenReturnsFalse()
    {
        // Arrange
        var integration = new FhirPathIntegration();
        var element = new TestTypedElement("value", "string value", "string");

        // Act
        var result = integration.EvaluateBoolean("'string value'", element);

        // Assert
        result.ShouldBeFalse();
    }

    #endregion

    #region Scalar Evaluation Tests

    [Fact]
    public void GivenFhirPathIntegration_WhenEvaluatingScalar_ThenReturnsValue()
    {
        // Arrange
        var integration = new FhirPathIntegration();
        var patient = new TestTypedElement("Patient");
        var id = new TestTypedElement("id", "patient-123", "string");
        patient.AddChild(id);

        // Act
        var result = integration.EvaluateScalar("id", patient);

        // Assert
        result.ShouldBe("patient-123");
    }

    [Fact]
    public void GivenFhirPathIntegration_WhenEvaluatingScalarWithNoResults_ThenReturnsNull()
    {
        // Arrange
        var integration = new FhirPathIntegration();
        var patient = new TestTypedElement("Patient");

        // Act
        var result = integration.EvaluateScalar("name", patient);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public void GivenFhirPathIntegration_WhenEvaluatingScalarWithMultipleResults_ThenReturnsFirst()
    {
        // Arrange
        var integration = new FhirPathIntegration();
        var patient = new TestTypedElement("Patient");
        patient.AddChild(new TestTypedElement("name", "First", "string"));
        patient.AddChild(new TestTypedElement("name", "Second", "string"));

        // Act
        var result = integration.EvaluateScalar("name", patient);

        // Assert
        result.ShouldBe("First");
    }

    #endregion

    #region Expression Caching Tests

    [Fact]
    public void GivenFhirPathIntegration_WhenEvaluatingSameExpressionTwice_ThenUsesCachedExpression()
    {
        // Arrange
        var integration = new FhirPathIntegration(cacheExpressions: true);
        var patient = new TestTypedElement("Patient");
        var id = new TestTypedElement("id", "patient-123", "string");
        patient.AddChild(id);

        // Act
        var result1 = integration.Evaluate("id", patient).ToList();
        var result2 = integration.Evaluate("id", patient).ToList();

        // Assert
        result1.Count.ShouldBe(1);
        result2.Count.ShouldBe(1);
        result1[0].Value.ShouldBe(result2[0].Value);
    }

    [Fact]
    public void GivenFhirPathIntegration_WhenClearingCache_ThenRemovesCachedExpressions()
    {
        // Arrange
        var integration = new FhirPathIntegration(cacheExpressions: true);
        var patient = new TestTypedElement("Patient");
        var id = new TestTypedElement("id", "patient-123", "string");
        patient.AddChild(id);

        // Act
        _ = integration.Evaluate("id", patient).ToList();
        integration.ClearCache();
        var result = integration.Evaluate("id", patient).ToList();

        // Assert
        result.Count.ShouldBe(1);
        result[0].Value.ShouldBe("patient-123");
    }

    [Fact]
    public void GivenFhirPathIntegration_WhenCachingDisabled_ThenDoesNotCacheExpressions()
    {
        // Arrange
        var integration = new FhirPathIntegration(cacheExpressions: false);
        var patient = new TestTypedElement("Patient");
        var id = new TestTypedElement("id", "patient-123", "string");
        patient.AddChild(id);

        // Act
        var act = () =>
        {
            _ = integration.Evaluate("id", patient).ToList();
            integration.ClearCache(); // Should not throw
        };

        // Assert
        Should.NotThrow(act);
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public void GivenFhirPathIntegration_WhenInvalidExpression_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var integration = new FhirPathIntegration();
        var patient = new TestTypedElement("Patient");

        // Act
        var act = () => integration.Evaluate("invalid..syntax", patient).ToList();

        // Assert
        Should.Throw<InvalidOperationException>(act).Message.ShouldContain("FHIRPath");
    }

    #endregion

    #region Integration with Mapping Evaluator Tests

    [Fact]
    public void GivenMappingEvaluator_WhenFhirPathEnabled_ThenIntegratesAutomatically()
    {
        // Arrange
        var evaluator = new MappingEvaluator(enableFhirPath: true);

        // Assert
        // Constructor should not throw
        evaluator.ShouldNotBeNull();
    }

    [Fact]
    public void GivenMappingEvaluator_WhenFhirPathDisabled_ThenDoesNotIntegrate()
    {
        // Arrange
        var evaluator = new MappingEvaluator(enableFhirPath: false);

        // Assert
        // Constructor should not throw
        evaluator.ShouldNotBeNull();
    }

    #endregion

    #region Complex FhirPath Expression Tests

    [Fact]
    public void GivenFhirPathIntegration_WhenEvaluatingFunctionCall_ThenReturnsResult()
    {
        // Arrange
        var integration = new FhirPathIntegration();
        var patient = new TestTypedElement("Patient");
        var name1 = new TestTypedElement("name", "John", "string");
        var name2 = new TestTypedElement("name", "Jane", "string");
        patient.AddChild(name1);
        patient.AddChild(name2);

        // Act
        var result = integration.EvaluateScalar("name.count()", patient);

        // Assert
        result.ShouldBe(2);
    }

    [Fact]
    public void GivenFhirPathIntegration_WhenEvaluatingWhereClause_ThenFiltersResults()
    {
        // Arrange
        var integration = new FhirPathIntegration();
        var patient = new TestTypedElement("Patient");
        var name = new TestTypedElement("name");
        var use = new TestTypedElement("use", "official", "code");
        name.AddChild(use);
        patient.AddChild(name);

        // Act
        var results = integration.Evaluate("name.where(use='official')", patient).ToList();

        // Assert
        results.Count.ShouldBe(1);
    }

    #endregion

    /// <summary>
    /// Parses cleanly, then fails during evaluation: '&amp;' is a singleton operator and the left operand
    /// is a three-item collection.
    /// </summary>
    private const string ThrowsAtEvaluationTime = "(1 | 2 | 3) & 'b'";

    [Fact]
    public void GivenAnExpressionThatThrowsDuringEvaluation_WhenEvaluating_ThenTheGuardCatchesItBeforeReturning()
    {
        // Arrange
        var integration = new FhirPathIntegration();
        var patient = new TestTypedElement("Patient");

        // Act: deliberately NOT enumerated. The evaluator returns a lazy sequence, so this call used to
        // return normally and throw at the caller's first enumeration - past the try/catch entirely.
        var exception = Should.Throw<Exception>(() => integration.Evaluate(ThrowsAtEvaluationTime, patient));

        // Assert
        exception.Message.ShouldStartWith("Failed to evaluate FHIRPath expression");
        exception.InnerException.ShouldNotBeNull();
    }

    [Fact]
    public void GivenAnExpressionThatThrowsDuringEvaluation_WhenEvaluating_ThenTheEvaluationErrorTypeSurvivesWrapping()
    {
        // Arrange
        var integration = new FhirPathIntegration();
        var patient = new TestTypedElement("Patient");

        // Act
        var exception = Should.Throw<FhirPathEvaluationException>(
            () => integration.Evaluate(ThrowsAtEvaluationTime, patient));

        // Assert: the contextual message is preserved, and so is the caller's ability to tell a
        // spec-mandated FHIRPath error from an engine defect.
        exception.Message.ShouldBe($"Failed to evaluate FHIRPath expression: {ThrowsAtEvaluationTime}");
        exception.InnerException.ShouldBeOfType<FhirPathEvaluationException>();
    }

    [Fact]
    public void GivenAnExpressionThatExceedsAnEvaluationLimit_WhenEvaluating_ThenTheLimitTypeSurvivesWrapping()
    {
        // Arrange - the lookahead forces the backtracking engine and (a+)+ inside it is exponential on a run of
        // 'a' with no 'b', so the match times out (one second): a resource limit, not an ill-formed expression.
        var integration = new FhirPathIntegration();
        var patient = new TestTypedElement("Patient");
        var expression = $"'{new string('a', 40)}'.matches('(?=(a+)+b)')";

        // Act
        var exception = Should.Throw<FhirPathResourceLimitExceededException>(() => integration.Evaluate(expression, patient));

        // Assert - guard (new behaviour, not a regression): the wrapper rewrapped this as a plain FhirPathEvaluationException, so a host could no
        // longer report it as too costly.
        exception.Message.ShouldBe($"Failed to evaluate FHIRPath expression: {expression}");
        exception.InnerException.ShouldBeOfType<FhirPathResourceLimitExceededException>();
    }

    [Fact]
    public void GivenALoweredStepBudget_WhenEvaluating_ThenTheIntegrationEnforcesItsLimits()
    {
        // Arrange - MappingEvaluator builds the integration with its FhirPathTimeout as the time limit; any limit
        // passed in is enforced.
        var integration = new FhirPathIntegration(limits: FhirPathEvaluationLimits.Default with { MaxSteps = 100 });
        var patient = new TestTypedElement("Patient");

        // Act & Assert
        Should.Throw<FhirPathResourceLimitExceededException>(
            () => integration.Evaluate("(1|2|3|4|5|6|7|8|9|10).select((1|2|3|4|5|6|7|8|9|10)).count()", patient));
    }

    [Fact]
    public void GivenAMappingContextWhoseTokenIsCancelled_WhenEvaluating_ThenTheCancellationSurfacesUnwrapped()
    {
        // Arrange - MappingEvaluator publishes the transformation's token on the context while it runs.
        var integration = new FhirPathIntegration();
        var patient = new TestTypedElement("Patient");
        var mappingContext = new MappingContext { CancellationToken = new CancellationToken(canceled: true) };

        // Act & Assert - neither swallowed nor rewrapped as a failed evaluation.
        Should.Throw<OperationCanceledException>(() => integration.Evaluate("Patient", patient, mappingContext));
    }

    [Fact]
    public void GivenAnUnparseableExpression_WhenEvaluating_ThenItStillSurfacesAsPlainInvalidOperationException()
    {
        // Arrange
        var integration = new FhirPathIntegration();
        var patient = new TestTypedElement("Patient");

        // Act
        var exception = Should.Throw<InvalidOperationException>(() => integration.Evaluate("(((", patient));

        // Assert: everything that is not a FHIRPath evaluation error keeps its previous shape exactly.
        exception.ShouldNotBeOfType<FhirPathEvaluationException>();
        exception.Message.ShouldBe("Failed to evaluate FHIRPath expression: (((");
        exception.InnerException.ShouldNotBeNull();
    }

    [Fact]
    public void GivenMoreDistinctExpressionsThanCapacity_WhenEvaluating_ThenCacheStaysBounded()
    {
        // Arrange
        var integration = new FhirPathIntegration(cacheCapacity: 8);
        var patient = new TestTypedElement("Patient");

        // Act
        for (var i = 0; i < 100; i++)
        {
            integration.Evaluate($"family | 'distinct{i}'", patient).ToList();
        }

        // Assert
        integration.CachedExpressionCount.ShouldBeLessThanOrEqualTo(8);
    }
}
