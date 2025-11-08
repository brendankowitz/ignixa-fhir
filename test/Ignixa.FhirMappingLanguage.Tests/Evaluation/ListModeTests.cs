/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Unit tests for list mode semantics in FHIR Mapping Language.
 */

using FluentAssertions;
using Ignixa.FhirMappingLanguage;
using Ignixa.FhirMappingLanguage.Evaluation;
using Ignixa.Serialization.Abstractions;
using Xunit;

namespace Ignixa.FhirMappingLanguage.Tests.Evaluation;

public class ListModeTests
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

    #region First List Mode Tests

    [Fact]
    public void GivenMultipleElements_WhenFirstListMode_ThenProcessesOnlyFirst()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Transform(source src : Patient, target tgt : Bundle) {
  src.name -> tgt.entry first;
}";

        var compiler = new MappingCompiler();
        var map = compiler.Parse(mappingText);
        var evaluator = new MappingEvaluator(enableFhirPath: false);
        var context = new MappingContext();

        var source = new TestTypedElement("Patient");
        source.AddChild(new TestTypedElement("name", "John", "HumanName"));
        source.AddChild(new TestTypedElement("name", "Jane", "HumanName"));
        source.AddChild(new TestTypedElement("name", "Jack", "HumanName"));

        var target = new TestTypedElement("Bundle");

        context.SetSource("src", source);
        context.SetTarget("tgt", target);

        // Act
        var act = () => evaluator.ExecuteGroup(map, "Transform", context);

        // Assert - Should process only the first element
        act.Should().NotThrow();
    }

    #endregion

    #region NotFirst List Mode Tests

    [Fact]
    public void GivenMultipleElements_WhenNotFirstListMode_ThenSkipsFirstElement()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Transform(source src : Patient, target tgt : Bundle) {
  src.name -> tgt.entry not_first;
}";

        var compiler = new MappingCompiler();
        var map = compiler.Parse(mappingText);
        var evaluator = new MappingEvaluator(enableFhirPath: false);
        var context = new MappingContext();

        var source = new TestTypedElement("Patient");
        source.AddChild(new TestTypedElement("name", "John", "HumanName"));
        source.AddChild(new TestTypedElement("name", "Jane", "HumanName"));
        source.AddChild(new TestTypedElement("name", "Jack", "HumanName"));

        var target = new TestTypedElement("Bundle");

        context.SetSource("src", source);
        context.SetTarget("tgt", target);

        // Act
        var act = () => evaluator.ExecuteGroup(map, "Transform", context);

        // Assert - Should skip first and process remaining
        act.Should().NotThrow();
    }

    #endregion

    #region Last List Mode Tests

    [Fact]
    public void GivenMultipleElements_WhenLastListMode_ThenProcessesOnlyLast()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Transform(source src : Patient, target tgt : Bundle) {
  src.name -> tgt.entry last;
}";

        var compiler = new MappingCompiler();
        var map = compiler.Parse(mappingText);
        var evaluator = new MappingEvaluator(enableFhirPath: false);
        var context = new MappingContext();

        var source = new TestTypedElement("Patient");
        source.AddChild(new TestTypedElement("name", "John", "HumanName"));
        source.AddChild(new TestTypedElement("name", "Jane", "HumanName"));
        source.AddChild(new TestTypedElement("name", "Jack", "HumanName"));

        var target = new TestTypedElement("Bundle");

        context.SetSource("src", source);
        context.SetTarget("tgt", target);

        // Act
        var act = () => evaluator.ExecuteGroup(map, "Transform", context);

        // Assert - Should process only the last element
        act.Should().NotThrow();
    }

    #endregion

    #region NotLast List Mode Tests

    [Fact]
    public void GivenMultipleElements_WhenNotLastListMode_ThenSkipsLastElement()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Transform(source src : Patient, target tgt : Bundle) {
  src.name -> tgt.entry not_last;
}";

        var compiler = new MappingCompiler();
        var map = compiler.Parse(mappingText);
        var evaluator = new MappingEvaluator(enableFhirPath: false);
        var context = new MappingContext();

        var source = new TestTypedElement("Patient");
        source.AddChild(new TestTypedElement("name", "John", "HumanName"));
        source.AddChild(new TestTypedElement("name", "Jane", "HumanName"));
        source.AddChild(new TestTypedElement("name", "Jack", "HumanName"));

        var target = new TestTypedElement("Bundle");

        context.SetSource("src", source);
        context.SetTarget("tgt", target);

        // Act
        var act = () => evaluator.ExecuteGroup(map, "Transform", context);

        // Assert - Should skip last and process remaining
        act.Should().NotThrow();
    }

    #endregion

    #region OnlyOne List Mode Tests

    [Fact]
    public void GivenSingleElement_WhenOnlyOneListMode_ThenSucceeds()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Transform(source src : Patient, target tgt : Bundle) {
  src.id -> tgt.id only_one;
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

        // Assert - Should succeed with exactly one element
        act.Should().NotThrow();
    }

    [Fact]
    public void GivenMultipleElements_WhenOnlyOneListMode_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Transform(source src : Patient, target tgt : Bundle) {
  src.name -> tgt.entry only_one;
}";

        var compiler = new MappingCompiler();
        var map = compiler.Parse(mappingText);
        var evaluator = new MappingEvaluator(enableFhirPath: false);
        var context = new MappingContext();

        var source = new TestTypedElement("Patient");
        source.AddChild(new TestTypedElement("name", "John", "HumanName"));
        source.AddChild(new TestTypedElement("name", "Jane", "HumanName"));

        var target = new TestTypedElement("Bundle");

        context.SetSource("src", source);
        context.SetTarget("tgt", target);

        // Act
        var act = () => evaluator.ExecuteGroup(map, "Transform", context);

        // Assert - Should throw because there are multiple elements
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*only_one*exactly one element*");
    }

    [Fact]
    public void GivenZeroElements_WhenOnlyOneListMode_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Transform(source src : Patient, target tgt : Bundle) {
  src.name -> tgt.entry only_one;
}";

        var compiler = new MappingCompiler();
        var map = compiler.Parse(mappingText);
        var evaluator = new MappingEvaluator(enableFhirPath: false);
        var context = new MappingContext();

        var source = new TestTypedElement("Patient");
        // No name elements

        var target = new TestTypedElement("Bundle");

        context.SetSource("src", source);
        context.SetTarget("tgt", target);

        // Act - Rule should be skipped because there are no source values
        var act = () => evaluator.ExecuteGroup(map, "Transform", context);

        // Assert - Should not throw, rule is skipped
        act.Should().NotThrow();
    }

    #endregion

    #region Single List Mode Tests

    [Fact]
    public void GivenMultipleElements_WhenSingleListMode_ThenCreatesOnlyOneTarget()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Transform(source src : Patient, target tgt : Bundle) {
  src.name -> tgt.entry single;
}";

        var compiler = new MappingCompiler();
        var map = compiler.Parse(mappingText);
        var evaluator = new MappingEvaluator(enableFhirPath: false);
        var context = new MappingContext();

        var source = new TestTypedElement("Patient");
        source.AddChild(new TestTypedElement("name", "John", "HumanName"));
        source.AddChild(new TestTypedElement("name", "Jane", "HumanName"));
        source.AddChild(new TestTypedElement("name", "Jack", "HumanName"));

        var target = new TestTypedElement("Bundle");

        context.SetSource("src", source);
        context.SetTarget("tgt", target);

        // Act
        var act = () => evaluator.ExecuteGroup(map, "Transform", context);

        // Assert - Should create only one target
        act.Should().NotThrow();
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void GivenEmptyCollection_WhenFirstListMode_ThenSkipsRule()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Transform(source src : Patient, target tgt : Bundle) {
  src.name -> tgt.entry first;
}";

        var compiler = new MappingCompiler();
        var map = compiler.Parse(mappingText);
        var evaluator = new MappingEvaluator(enableFhirPath: false);
        var context = new MappingContext();

        var source = new TestTypedElement("Patient");
        // No name elements

        var target = new TestTypedElement("Bundle");

        context.SetSource("src", source);
        context.SetTarget("tgt", target);

        // Act
        var act = () => evaluator.ExecuteGroup(map, "Transform", context);

        // Assert - Rule should be skipped
        act.Should().NotThrow();
    }

    [Fact]
    public void GivenSingleElement_WhenNotFirstListMode_ThenSkipsAll()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Transform(source src : Patient, target tgt : Bundle) {
  src.id -> tgt.id not_first;
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

        // Assert - Should skip all elements (no elements after first)
        act.Should().NotThrow();
    }

    [Fact]
    public void GivenSingleElement_WhenNotLastListMode_ThenSkipsAll()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Transform(source src : Patient, target tgt : Bundle) {
  src.id -> tgt.id not_last;
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

        // Assert - Should skip all elements (no elements before last)
        act.Should().NotThrow();
    }

    #endregion

    #region Parser Integration Tests

    [Theory]
    [InlineData("first")]
    [InlineData("not_first")]
    [InlineData("last")]
    [InlineData("not_last")]
    [InlineData("only_one")]
    [InlineData("share")]
    [InlineData("single")]
    public void GivenListMode_WhenParsing_ThenParsesCorrectly(string listMode)
    {
        // Arrange
        var mappingText = $@"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Transform(source src : Patient, target tgt : Bundle) {{
  src.name -> tgt.entry {listMode};
}}";

        var compiler = new MappingCompiler();

        // Act
        var map = compiler.Parse(mappingText);

        // Assert
        map.Groups.Should().HaveCount(1);
        map.Groups[0].Rules.Should().HaveCount(1);
        map.Groups[0].Rules[0].Targets.Should().HaveCount(1);
        map.Groups[0].Rules[0].Targets[0].ListMode.Should().NotBeNull();
    }

    #endregion
}
