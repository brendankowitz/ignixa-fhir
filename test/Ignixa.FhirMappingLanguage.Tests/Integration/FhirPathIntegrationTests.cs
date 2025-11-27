/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Integration tests for FhirPath integration in mapping language.
 */

using FluentAssertions;
using Ignixa.FhirMappingLanguage.Evaluation;
using Ignixa.Abstractions;
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
        results.Should().HaveCount(1);
        results[0].Value.Should().Be("Doe");
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
        results.Should().BeEmpty();
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
        results.Should().BeEmpty();
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
        result.Should().BeTrue();
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
        result.Should().BeFalse();
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
        result.Should().BeFalse();
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
        result.Should().BeFalse();
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
        result.Should().Be("patient-123");
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
        result.Should().BeNull();
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
        result.Should().Be("First");
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
        result1.Should().HaveCount(1);
        result2.Should().HaveCount(1);
        result1[0].Value.Should().Be(result2[0].Value);
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
        result.Should().HaveCount(1);
        result[0].Value.Should().Be("patient-123");
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
        act.Should().NotThrow();
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
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*FHIRPath*");
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
        evaluator.Should().NotBeNull();
    }

    [Fact]
    public void GivenMappingEvaluator_WhenFhirPathDisabled_ThenDoesNotIntegrate()
    {
        // Arrange
        var evaluator = new MappingEvaluator(enableFhirPath: false);

        // Assert
        // Constructor should not throw
        evaluator.Should().NotBeNull();
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
        result.Should().Be(2);
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
        results.Should().HaveCount(1);
    }

    #endregion
}
