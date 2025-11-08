/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Unit tests for log and check statement execution in FHIR Mapping Language.
 */

using FluentAssertions;
using Ignixa.FhirMappingLanguage;
using Ignixa.FhirMappingLanguage.Evaluation;
using Ignixa.Serialization.Abstractions;
using Xunit;

namespace Ignixa.FhirMappingLanguage.Tests.Evaluation;

public class LogAndCheckTests
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

    #region Check Statement Tests

    [Fact]
    public void GivenCheckConditionTrue_WhenEvaluating_ThenSucceeds()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Transform(source src : Patient, target tgt : Bundle) {
  src.id check (src.id.exists()) -> tgt.id;
}";

        var compiler = new MappingCompiler();
        var map = compiler.Parse(mappingText);
        var evaluator = new MappingEvaluator(enableFhirPath: true);
        var context = new MappingContext();

        var source = new TestTypedElement("Patient");
        source.AddChild(new TestTypedElement("id", "patient-123", "id"));

        var target = new TestTypedElement("Bundle");

        context.SetSource("src", source);
        context.SetTarget("tgt", target);

        // Act
        var act = () => evaluator.ExecuteGroup(map, "Transform", context);

        // Assert - Should succeed
        act.Should().NotThrow();
    }

    [Fact]
    public void GivenCheckConditionFalse_WhenEvaluating_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Transform(source src : Patient, target tgt : Bundle) {
  src.id check (false) -> tgt.id;
}";

        var compiler = new MappingCompiler();
        var map = compiler.Parse(mappingText);
        var evaluator = new MappingEvaluator(enableFhirPath: true);
        var context = new MappingContext();

        var source = new TestTypedElement("Patient");
        source.AddChild(new TestTypedElement("id", "patient-123", "id"));

        var target = new TestTypedElement("Bundle");

        context.SetSource("src", source);
        context.SetTarget("tgt", target);

        // Act
        var act = () => evaluator.ExecuteGroup(map, "Transform", context);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Check condition failed*");
    }

    [Fact]
    public void GivenCheckWithComplexExpression_WhenEvaluatingTrue_ThenSucceeds()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Transform(source src : Patient, target tgt : Bundle) {
  src.name check (src.name.count() > 0) -> tgt.entry;
}";

        var compiler = new MappingCompiler();
        var map = compiler.Parse(mappingText);
        var evaluator = new MappingEvaluator(enableFhirPath: true);
        var context = new MappingContext();

        var source = new TestTypedElement("Patient");
        source.AddChild(new TestTypedElement("name", "John", "HumanName"));

        var target = new TestTypedElement("Bundle");

        context.SetSource("src", source);
        context.SetTarget("tgt", target);

        // Act
        var act = () => evaluator.ExecuteGroup(map, "Transform", context);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void GivenCheckWithoutFhirPath_WhenEvaluating_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Transform(source src : Patient, target tgt : Bundle) {
  src.id check (true) -> tgt.id;
}";

        var compiler = new MappingCompiler();
        var map = compiler.Parse(mappingText);
        var evaluator = new MappingEvaluator(enableFhirPath: false);
        var context = new MappingContext();

        var source = new TestTypedElement("Patient");
        source.AddChild(new TestTypedElement("id", "patient-123", "id"));

        var target = new TestTypedElement("Bundle");

        context.SetSource("src", source);
        context.SetTarget("tgt", target);

        // Act
        var act = () => evaluator.ExecuteGroup(map, "Transform", context);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*FhirPathEvaluator not configured*");
    }

    #endregion

    #region Log Statement Tests

    [Fact]
    public void GivenLogStatement_WhenEvaluating_ThenCallsLogger()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Transform(source src : Patient, target tgt : Bundle) {
  src.id log ('Processing patient') -> tgt.id;
}";

        var compiler = new MappingCompiler();
        var map = compiler.Parse(mappingText);
        var evaluator = new MappingEvaluator(enableFhirPath: true);
        var context = new MappingContext();

        var logMessages = new List<string>();
        context.Logger = message => logMessages.Add(message);

        var source = new TestTypedElement("Patient");
        source.AddChild(new TestTypedElement("id", "patient-123", "id"));

        var target = new TestTypedElement("Bundle");

        context.SetSource("src", source);
        context.SetTarget("tgt", target);

        // Act
        evaluator.ExecuteGroup(map, "Transform", context);

        // Assert
        logMessages.Should().ContainSingle();
        logMessages[0].Should().Be("Processing patient");
    }

    [Fact]
    public void GivenLogWithExpression_WhenEvaluating_ThenLogsEvaluatedResult()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Transform(source src : Patient, target tgt : Bundle) {
  src.id log (src.id) -> tgt.id;
}";

        var compiler = new MappingCompiler();
        var map = compiler.Parse(mappingText);
        var evaluator = new MappingEvaluator(enableFhirPath: true);
        var context = new MappingContext();

        var logMessages = new List<string>();
        context.Logger = message => logMessages.Add(message);

        var source = new TestTypedElement("Patient");
        source.AddChild(new TestTypedElement("id", "patient-123", "id"));

        var target = new TestTypedElement("Bundle");

        context.SetSource("src", source);
        context.SetTarget("tgt", target);

        // Act
        evaluator.ExecuteGroup(map, "Transform", context);

        // Assert
        logMessages.Should().ContainSingle();
        logMessages[0].Should().Be("patient-123");
    }

    [Fact]
    public void GivenLogWithoutLogger_WhenEvaluating_ThenDoesNotThrow()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Transform(source src : Patient, target tgt : Bundle) {
  src.id log ('No logger configured') -> tgt.id;
}";

        var compiler = new MappingCompiler();
        var map = compiler.Parse(mappingText);
        var evaluator = new MappingEvaluator(enableFhirPath: true);
        var context = new MappingContext();
        // No logger configured

        var source = new TestTypedElement("Patient");
        source.AddChild(new TestTypedElement("id", "patient-123", "id"));

        var target = new TestTypedElement("Bundle");

        context.SetSource("src", source);
        context.SetTarget("tgt", target);

        // Act
        var act = () => evaluator.ExecuteGroup(map, "Transform", context);

        // Assert - Should not throw, just silently skip logging
        act.Should().NotThrow();
    }

    [Fact]
    public void GivenLogWithMultipleElements_WhenEvaluating_ThenLogsAll()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Transform(source src : Patient, target tgt : Bundle) {
  src.name log ('Processing name') -> tgt.entry;
}";

        var compiler = new MappingCompiler();
        var map = compiler.Parse(mappingText);
        var evaluator = new MappingEvaluator(enableFhirPath: true);
        var context = new MappingContext();

        var logMessages = new List<string>();
        context.Logger = message => logMessages.Add(message);

        var source = new TestTypedElement("Patient");
        source.AddChild(new TestTypedElement("name", "John", "HumanName"));
        source.AddChild(new TestTypedElement("name", "Jane", "HumanName"));

        var target = new TestTypedElement("Bundle");

        context.SetSource("src", source);
        context.SetTarget("tgt", target);

        // Act
        evaluator.ExecuteGroup(map, "Transform", context);

        // Assert - Should log once for each element
        logMessages.Should().HaveCount(2);
        logMessages.Should().AllBe("Processing name");
    }

    [Fact]
    public void GivenLogWithEmptyResult_WhenEvaluating_ThenLogsEmptyMessage()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Transform(source src : Patient, target tgt : Bundle) {
  src.id log (src.nonexistent) -> tgt.id;
}";

        var compiler = new MappingCompiler();
        var map = compiler.Parse(mappingText);
        var evaluator = new MappingEvaluator(enableFhirPath: true);
        var context = new MappingContext();

        var logMessages = new List<string>();
        context.Logger = message => logMessages.Add(message);

        var source = new TestTypedElement("Patient");
        source.AddChild(new TestTypedElement("id", "patient-123", "id"));

        var target = new TestTypedElement("Bundle");

        context.SetSource("src", source);
        context.SetTarget("tgt", target);

        // Act
        evaluator.ExecuteGroup(map, "Transform", context);

        // Assert
        logMessages.Should().ContainSingle();
        logMessages[0].Should().Be("(empty)");
    }

    [Fact]
    public void GivenLogWithoutFhirPath_WhenEvaluating_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Transform(source src : Patient, target tgt : Bundle) {
  src.id log ('message') -> tgt.id;
}";

        var compiler = new MappingCompiler();
        var map = compiler.Parse(mappingText);
        var evaluator = new MappingEvaluator(enableFhirPath: false);
        var context = new MappingContext();

        var source = new TestTypedElement("Patient");
        source.AddChild(new TestTypedElement("id", "patient-123", "id"));

        var target = new TestTypedElement("Bundle");

        context.SetSource("src", source);
        context.SetTarget("tgt", target);

        // Act
        var act = () => evaluator.ExecuteGroup(map, "Transform", context);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*FhirPathEvaluator not configured*");
    }

    #endregion

    #region Combined Where, Check, and Log Tests

    [Fact]
    public void GivenWhereCheckAndLog_WhenEvaluating_ThenExecutesInOrder()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Transform(source src : Patient, target tgt : Bundle) {
  src.name where (src.name.exists()) check (src.name.count() > 0) log ('Processing names') -> tgt.entry;
}";

        var compiler = new MappingCompiler();
        var map = compiler.Parse(mappingText);
        var evaluator = new MappingEvaluator(enableFhirPath: true);
        var context = new MappingContext();

        var logMessages = new List<string>();
        context.Logger = message => logMessages.Add(message);

        var source = new TestTypedElement("Patient");
        source.AddChild(new TestTypedElement("name", "John", "HumanName"));

        var target = new TestTypedElement("Bundle");

        context.SetSource("src", source);
        context.SetTarget("tgt", target);

        // Act
        evaluator.ExecuteGroup(map, "Transform", context);

        // Assert
        logMessages.Should().ContainSingle();
        logMessages[0].Should().Be("Processing names");
    }

    [Fact]
    public void GivenWhereFiltersOutElements_WhenCheckAndLog_ThenNotExecuted()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Transform(source src : Patient, target tgt : Bundle) {
  src.name where (false) check (true) log ('Should not log') -> tgt.entry;
}";

        var compiler = new MappingCompiler();
        var map = compiler.Parse(mappingText);
        var evaluator = new MappingEvaluator(enableFhirPath: true);
        var context = new MappingContext();

        var logMessages = new List<string>();
        context.Logger = message => logMessages.Add(message);

        var source = new TestTypedElement("Patient");
        source.AddChild(new TestTypedElement("name", "John", "HumanName"));

        var target = new TestTypedElement("Bundle");

        context.SetSource("src", source);
        context.SetTarget("tgt", target);

        // Act
        evaluator.ExecuteGroup(map, "Transform", context);

        // Assert - Where filters everything out, so check and log should not execute
        logMessages.Should().BeEmpty();
    }

    #endregion

    #region Parser Integration Tests

    [Fact]
    public void GivenMappingWithCheck_WhenParsing_ThenParsesCorrectly()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Transform(source src : Patient, target tgt : Bundle) {
  src.id check (src.id.exists()) -> tgt.id;
}";

        var compiler = new MappingCompiler();

        // Act
        var map = compiler.Parse(mappingText);

        // Assert
        map.Groups.Should().HaveCount(1);
        map.Groups[0].Rules.Should().HaveCount(1);
        map.Groups[0].Rules[0].Sources.Should().HaveCount(1);
        map.Groups[0].Rules[0].Sources[0].Check.Should().NotBeNull();
    }

    [Fact]
    public void GivenMappingWithLog_WhenParsing_ThenParsesCorrectly()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Transform(source src : Patient, target tgt : Bundle) {
  src.id log ('message') -> tgt.id;
}";

        var compiler = new MappingCompiler();

        // Act
        var map = compiler.Parse(mappingText);

        // Assert
        map.Groups.Should().HaveCount(1);
        map.Groups[0].Rules.Should().HaveCount(1);
        map.Groups[0].Rules[0].Sources.Should().HaveCount(1);
        map.Groups[0].Rules[0].Sources[0].Log.Should().NotBeNull();
    }

    #endregion
}
