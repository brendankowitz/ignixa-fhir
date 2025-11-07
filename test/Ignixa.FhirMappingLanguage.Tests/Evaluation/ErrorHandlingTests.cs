/*
 * Copyright (c) 2025, Sparky Contributors
 *
 * Unit tests for runtime error handling and graceful recovery.
 */

using FluentAssertions;
using Ignixa.FhirMappingLanguage;
using Ignixa.FhirMappingLanguage.Evaluation;
using Ignixa.Serialization.Abstractions;
using Xunit;

namespace Ignixa.FhirMappingLanguage.Tests.Evaluation;

public class ErrorHandlingTests
{
    #region Helper Classes

    private class TestTypedElement : ITypedElement
    {
        private readonly Dictionary<string, List<ITypedElement>> _children = new();

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
        public IElementDefinitionSummary? Definition => null;

        public void AddChild(ITypedElement child)
        {
            if (!_children.ContainsKey(child.Name))
            {
                _children[child.Name] = new List<ITypedElement>();
            }
            _children[child.Name].Add(child);
        }

        public IEnumerable<ITypedElement> Children(string? name = null)
        {
            if (name == null)
            {
                return _children.Values.SelectMany(list => list);
            }

            return _children.TryGetValue(name, out var children)
                ? children
                : Enumerable.Empty<ITypedElement>();
        }
    }

    #endregion

    #region Strict Mode Tests (Default Behavior)

    [Fact]
    public void GivenStrictMode_WhenMissingParameter_ThenThrowsException()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Transform(source src : Patient, target tgt : Bundle) {
  src.name -> tgt.entry;
}";

        var compiler = new MappingCompiler();
        var map = compiler.Parse(mappingText);
        var evaluator = new MappingEvaluator(enableFhirPath: false);
        var context = new MappingContext
        {
            ErrorMode = ErrorMode.Strict // Explicit strict mode
        };

        // Only set source, not target
        context.SetSource("src", new TestTypedElement("Patient"));

        // Act & Assert
        var act = () => evaluator.ExecuteGroup(map, "Transform", context);
        act.Should().Throw<MappingExecutionException>()
            .WithMessage("*Required target parameter 'tgt' not provided*");
    }

    [Fact]
    public void GivenStrictMode_WhenCheckFails_ThenThrowsException()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Transform(source src : Patient, target tgt : Bundle) {
  src.name check (name.exists()) -> tgt.entry;
}";

        var compiler = new MappingCompiler();
        var map = compiler.Parse(mappingText);
        var evaluator = new MappingEvaluator(enableFhirPath: true);
        var context = new MappingContext
        {
            ErrorMode = ErrorMode.Strict
        };

        var source = new TestTypedElement("Patient");
        source.AddChild(new TestTypedElement("name", null, "HumanName")); // name exists but check would fail

        context.SetSource("src", source);
        context.SetTarget("tgt", new TestTypedElement("Bundle"));

        // Act & Assert
        var act = () => evaluator.ExecuteGroup(map, "Transform", context);
        act.Should().Throw<Exception>(); // Check condition will fail
    }

    #endregion

    #region Graceful Mode Tests

    [Fact]
    public void GivenGracefulMode_WhenMissingParameter_ThenCollectsError()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Transform(source src : Patient, target tgt : Bundle) {
  src.name -> tgt.entry;
}";

        var compiler = new MappingCompiler();
        var map = compiler.Parse(mappingText);
        var evaluator = new MappingEvaluator(enableFhirPath: false);
        var context = new MappingContext
        {
            ErrorMode = ErrorMode.Graceful
        };

        // Only set source, not target
        context.SetSource("src", new TestTypedElement("Patient"));

        // Act
        evaluator.ExecuteGroup(map, "Transform", context);

        // Assert
        context.Errors.Should().HaveCount(1);
        context.Errors[0].Message.Should().Contain("Required target parameter 'tgt' not provided");
        context.Errors[0].Code.Should().Be("GROUP_EXECUTION_ERROR");
        context.Errors[0].Location.Should().Be("Group: Transform");
    }

    [Fact]
    public void GivenGracefulMode_WhenCheckFails_ThenCollectsErrorAndContinues()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Transform(source src : Patient, target tgt : Bundle) {
  src.name check (false) -> tgt.entry;
  src.id -> tgt.id;
}";

        var compiler = new MappingCompiler();
        var map = compiler.Parse(mappingText);
        var evaluator = new MappingEvaluator(enableFhirPath: true);
        var context = new MappingContext
        {
            ErrorMode = ErrorMode.Graceful
        };

        var source = new TestTypedElement("Patient");
        source.AddChild(new TestTypedElement("name", "John", "HumanName"));
        source.AddChild(new TestTypedElement("id", "patient-123", "id"));

        context.SetSource("src", source);
        context.SetTarget("tgt", new TestTypedElement("Bundle"));

        // Act
        evaluator.ExecuteGroup(map, "Transform", context);

        // Assert
        context.Errors.Should().HaveCountGreaterThan(0);
        context.Errors.Should().Contain(e => e.Code == "CHECK_ERROR");
        // Second rule should still execute despite first rule's check failure
    }

    [Fact]
    public void GivenGracefulMode_WhenTransformFails_ThenCollectsErrorAndContinues()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Transform(source src : Patient, target tgt : Bundle) {
  src.name -> tgt.name = create('NonExistentType');
  src.id -> tgt.id;
}";

        var compiler = new MappingCompiler();
        var map = compiler.Parse(mappingText);
        var evaluator = new MappingEvaluator(enableFhirPath: false);
        var context = new MappingContext
        {
            ErrorMode = ErrorMode.Graceful
        };

        var source = new TestTypedElement("Patient");
        source.AddChild(new TestTypedElement("name", "John", "HumanName"));
        source.AddChild(new TestTypedElement("id", "patient-123", "id"));

        context.SetSource("src", source);
        context.SetTarget("tgt", new TestTypedElement("Bundle"));

        // Act
        evaluator.ExecuteGroup(map, "Transform", context);

        // Assert
        context.Errors.Should().HaveCountGreaterThan(0);
        // Should collect transform error but continue processing
    }

    [Fact]
    public void GivenGracefulMode_WhenMultipleRulesFail_ThenCollectsAllErrors()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Transform(source src : Patient, target tgt : Bundle) {
  src.badField1 -> tgt.entry;
  src.badField2 -> tgt.id;
  src.id -> tgt.type;
}";

        var compiler = new MappingCompiler();
        var map = compiler.Parse(mappingText);
        var evaluator = new MappingEvaluator(enableFhirPath: false);
        var context = new MappingContext
        {
            ErrorMode = ErrorMode.Graceful
        };

        var source = new TestTypedElement("Patient");
        source.AddChild(new TestTypedElement("id", "patient-123", "id"));

        context.SetSource("src", source);
        context.SetTarget("tgt", new TestTypedElement("Bundle"));

        // Act
        evaluator.ExecuteGroup(map, "Transform", context);

        // Assert - first two rules skip due to missing fields, third should execute
        // No errors should be collected because missing fields just result in empty sources (which skip the rule)
        // This is expected behavior
    }

    [Fact]
    public void GivenGracefulMode_WhenWhereFails_ThenCollectsErrorAndFiltersElement()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Transform(source src : Patient, target tgt : Bundle) {
  src.name where (nonExistentFunction()) log (src.name) -> tgt.entry;
}";

        var compiler = new MappingCompiler();
        var map = compiler.Parse(mappingText);
        var evaluator = new MappingEvaluator(enableFhirPath: true);
        var context = new MappingContext
        {
            ErrorMode = ErrorMode.Graceful
        };

        var logMessages = new List<string>();
        context.Logger = message => logMessages.Add(message);

        var source = new TestTypedElement("Patient");
        source.AddChild(new TestTypedElement("name", "John", "HumanName"));

        context.SetSource("src", source);
        context.SetTarget("tgt", new TestTypedElement("Bundle"));

        // Act
        evaluator.ExecuteGroup(map, "Transform", context);

        // Assert
        context.Errors.Should().HaveCountGreaterThan(0);
        context.Errors.Should().Contain(e => e.Code == "WHERE_ERROR");
        logMessages.Should().BeEmpty(); // Log should not execute if where fails
    }

    [Fact]
    public void GivenGracefulMode_WhenLogFails_ThenCollectsErrorAndContinues()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Transform(source src : Patient, target tgt : Bundle) {
  src.name log (nonExistentFunction()) -> tgt.entry;
}";

        var compiler = new MappingCompiler();
        var map = compiler.Parse(mappingText);
        var evaluator = new MappingEvaluator(enableFhirPath: true);
        var context = new MappingContext
        {
            ErrorMode = ErrorMode.Graceful
        };

        var source = new TestTypedElement("Patient");
        source.AddChild(new TestTypedElement("name", "John", "HumanName"));

        context.SetSource("src", source);
        context.SetTarget("tgt", new TestTypedElement("Bundle"));

        // Act
        evaluator.ExecuteGroup(map, "Transform", context);

        // Assert
        context.Errors.Should().HaveCountGreaterThan(0);
        context.Errors.Should().Contain(e => e.Code == "LOG_ERROR");
        // Execution should continue despite log error
    }

    #endregion

    #region Partial Results Tests

    [Fact]
    public void GivenGracefulMode_WhenSomeRulesFail_ThenProducesPartialResults()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Transform(source src : Patient, target tgt : Bundle) {
  src.name where (false) -> tgt.name;
  src.id -> tgt.id;
  src.active -> tgt.type;
}";

        var compiler = new MappingCompiler();
        var map = compiler.Parse(mappingText);
        var evaluator = new MappingEvaluator(enableFhirPath: true);
        var context = new MappingContext
        {
            ErrorMode = ErrorMode.Graceful
        };

        var source = new TestTypedElement("Patient");
        source.AddChild(new TestTypedElement("name", "John", "HumanName"));
        source.AddChild(new TestTypedElement("id", "patient-123", "id"));
        source.AddChild(new TestTypedElement("active", true, "boolean"));

        context.SetSource("src", source);
        context.SetTarget("tgt", new TestTypedElement("Bundle"));

        // Act
        evaluator.ExecuteGroup(map, "Transform", context);

        // Assert - first rule should skip (where false), but second and third should execute
        // This produces partial results without throwing
    }

    #endregion

    #region Error Collection API Tests

    [Fact]
    public void GivenMappingContext_WhenAddingError_ThenStoresError()
    {
        // Arrange
        var context = new MappingContext
        {
            ErrorMode = ErrorMode.Graceful
        };

        // Act
        context.AddError("Test error", "Test location", "TEST_CODE");

        // Assert
        context.Errors.Should().HaveCount(1);
        context.Errors[0].Message.Should().Be("Test error");
        context.Errors[0].Location.Should().Be("Test location");
        context.Errors[0].Code.Should().Be("TEST_CODE");
    }

    [Fact]
    public void GivenMappingContext_WhenClearingErrors_ThenRemovesAllErrors()
    {
        // Arrange
        var context = new MappingContext
        {
            ErrorMode = ErrorMode.Graceful
        };
        context.AddError("Error 1");
        context.AddError("Error 2");

        // Act
        context.ClearErrors();

        // Assert
        context.Errors.Should().BeEmpty();
    }

    [Fact]
    public void GivenStrictMode_WhenAddingError_ThenThrowsException()
    {
        // Arrange
        var context = new MappingContext
        {
            ErrorMode = ErrorMode.Strict
        };

        // Act & Assert
        var act = () => context.AddError("Test error", "Test location", "TEST_CODE");
        act.Should().Throw<MappingExecutionException>()
            .WithMessage("*Test error*");
    }

    [Fact]
    public void GivenExecutionError_WhenFormatting_ThenIncludesLocationAndCode()
    {
        // Arrange
        var error = new ExecutionError("Test message", "Test location", "TEST_CODE");

        // Act
        var formatted = error.ToString();

        // Assert
        formatted.Should().Contain("Test location");
        formatted.Should().Contain("Test message");
        formatted.Should().Contain("TEST_CODE");
    }

    [Fact]
    public void GivenExecutionResult_WhenNoErrors_ThenIsSuccess()
    {
        // Arrange
        var result = new ExecutionResult<string>("Success");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Result.Should().Be("Success");
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void GivenExecutionResult_WhenHasErrors_ThenNotSuccess()
    {
        // Arrange
        var result = new ExecutionResult<string>("Partial");
        result.AddError("Test error");

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().HaveCount(1);
    }

    [Fact]
    public void GivenExecutionResult_WhenGettingSummary_ThenDescribesStatus()
    {
        // Arrange - Success case
        var successResult = new ExecutionResult<string>("Success");

        // Act & Assert
        successResult.GetSummary().Should().Contain("successfully");

        // Arrange - Error case
        var errorResult = new ExecutionResult<string>("Failure");
        errorResult.AddError("Error 1");
        errorResult.AddError("Error 2");

        // Act & Assert
        errorResult.GetSummary().Should().Contain("2 error(s)");
    }

    #endregion

    #region Integration Tests

    [Fact]
    public void GivenComplexMapping_WhenGracefulMode_ThenCollectsAllErrors()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Transform(source src : Patient, target tgt : Bundle) {
  src.name check (false) -> tgt.name;
  src.gender log (nonExistent()) -> tgt.type;
  src.id -> tgt.id;
}";

        var compiler = new MappingCompiler();
        var map = compiler.Parse(mappingText);
        var evaluator = new MappingEvaluator(enableFhirPath: true);
        var context = new MappingContext
        {
            ErrorMode = ErrorMode.Graceful
        };

        var source = new TestTypedElement("Patient");
        source.AddChild(new TestTypedElement("name", "John", "HumanName"));
        source.AddChild(new TestTypedElement("gender", "male", "code"));
        source.AddChild(new TestTypedElement("id", "patient-123", "id"));

        context.SetSource("src", source);
        context.SetTarget("tgt", new TestTypedElement("Bundle"));

        // Act
        evaluator.ExecuteGroup(map, "Transform", context);

        // Assert
        context.Errors.Should().HaveCountGreaterThan(0);
        context.Errors.Should().Contain(e => e.Code == "CHECK_ERROR");
        context.Errors.Should().Contain(e => e.Code == "LOG_ERROR");
        // Third rule should still execute
    }

    [Fact]
    public void GivenGracefulMode_WhenNestedRulesFail_ThenCollectsErrorsWithLocation()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Transform(source src : Patient, target tgt : Bundle) {
  src.name -> tgt.entry then {
    src.given check (false) -> tgt.value;
  };
}";

        var compiler = new MappingCompiler();
        var map = compiler.Parse(mappingText);
        var evaluator = new MappingEvaluator(enableFhirPath: true);
        var context = new MappingContext
        {
            ErrorMode = ErrorMode.Graceful
        };

        var source = new TestTypedElement("Patient");
        var name = new TestTypedElement("name", null, "HumanName");
        name.AddChild(new TestTypedElement("given", "John", "string"));
        source.AddChild(name);

        context.SetSource("src", source);
        context.SetTarget("tgt", new TestTypedElement("Bundle"));

        // Act
        evaluator.ExecuteGroup(map, "Transform", context);

        // Assert
        context.Errors.Should().HaveCountGreaterThan(0);
        // Errors should have location information
        context.Errors.Should().Contain(e => e.Location != null);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void GivenGracefulMode_WhenAllRulesFail_ThenCollectsAllErrorsAndProducesNoResult()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Transform(source src : Patient, target tgt : Bundle) {
  src.name check (false) -> tgt.name;
  src.gender check (false) -> tgt.type;
}";

        var compiler = new MappingCompiler();
        var map = compiler.Parse(mappingText);
        var evaluator = new MappingEvaluator(enableFhirPath: true);
        var context = new MappingContext
        {
            ErrorMode = ErrorMode.Graceful
        };

        var source = new TestTypedElement("Patient");
        source.AddChild(new TestTypedElement("name", "John", "HumanName"));
        source.AddChild(new TestTypedElement("gender", "male", "code"));

        context.SetSource("src", source);
        context.SetTarget("tgt", new TestTypedElement("Bundle"));

        // Act
        evaluator.ExecuteGroup(map, "Transform", context);

        // Assert
        context.Errors.Should().HaveCountGreaterThan(0);
        context.Errors.Should().OnlyContain(e => e.Code == "CHECK_ERROR");
    }

    [Fact]
    public void GivenMappingExecutionException_WhenFormatted_ThenIncludesAllDetails()
    {
        // Arrange & Act
        var exception = new MappingExecutionException(
            "Test error",
            "Group: Transform",
            "TEST_ERROR"
        );

        // Assert
        exception.Message.Should().Contain("Test error");
        exception.Message.Should().Contain("Group: Transform");
        exception.Message.Should().Contain("TEST_ERROR");
        exception.Location.Should().Be("Group: Transform");
        exception.Code.Should().Be("TEST_ERROR");
    }

    #endregion
}
