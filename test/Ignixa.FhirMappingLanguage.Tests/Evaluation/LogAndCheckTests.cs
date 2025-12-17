/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Unit tests for log and check statement execution in FHIR Mapping Language.
 */

using Shouldly;
using Ignixa.FhirMappingLanguage;
using Ignixa.FhirMappingLanguage.Evaluation;
using Ignixa.Abstractions;
using Ignixa.FhirMappingLanguage.Parser;
using Xunit;

namespace Ignixa.FhirMappingLanguage.Tests.Evaluation;

public class LogAndCheckTests
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

        var compiler = new MappingParser();
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
        Should.NotThrow(act);
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

        var compiler = new MappingParser();
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
        Should.Throw<InvalidOperationException>(act).Message.ShouldContain("Check condition failed");
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

        var compiler = new MappingParser();
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
        Should.NotThrow(act);
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

        var compiler = new MappingParser();
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
        Should.Throw<InvalidOperationException>(act).Message.ShouldContain("FhirPathEvaluator not configured");
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

        var compiler = new MappingParser();
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
        logMessages.ShouldHaveSingleItem();
        logMessages[0].ShouldBe("Processing patient");
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

        var compiler = new MappingParser();
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
        logMessages.ShouldHaveSingleItem();
        logMessages[0].ShouldBe("patient-123");
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

        var compiler = new MappingParser();
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
        Should.NotThrow(act);
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

        var compiler = new MappingParser();
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
        logMessages.Count.ShouldBe(2);
        logMessages.ShouldAllBe(x => x == "Processing name");
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

        var compiler = new MappingParser();
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
        logMessages.ShouldHaveSingleItem();
        logMessages[0].ShouldBe("(empty)");
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

        var compiler = new MappingParser();
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
        Should.Throw<InvalidOperationException>(act).Message.ShouldContain("FhirPathEvaluator not configured");
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

        var compiler = new MappingParser();
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
        logMessages.ShouldHaveSingleItem();
        logMessages[0].ShouldBe("Processing names");
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

        var compiler = new MappingParser();
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
        logMessages.ShouldBeEmpty();
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

        var compiler = new MappingParser();

        // Act
        var map = compiler.Parse(mappingText);

        // Assert
        map.Groups.Count.ShouldBe(1);
        map.Groups[0].Rules.Count.ShouldBe(1);
        map.Groups[0].Rules[0].Sources.Count.ShouldBe(1);
        map.Groups[0].Rules[0].Sources[0].Check.ShouldNotBeNull();
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

        var compiler = new MappingParser();

        // Act
        var map = compiler.Parse(mappingText);

        // Assert
        map.Groups.Count.ShouldBe(1);
        map.Groups[0].Rules.Count.ShouldBe(1);
        map.Groups[0].Rules[0].Sources.Count.ShouldBe(1);
        map.Groups[0].Rules[0].Sources[0].Log.ShouldNotBeNull();
    }

    #endregion
}
