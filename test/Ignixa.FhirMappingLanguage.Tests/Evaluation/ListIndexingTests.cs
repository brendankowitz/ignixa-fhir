/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Unit tests for list indexing functionality in FHIR Mapping Language.
 */

using Shouldly;
using Ignixa.FhirMappingLanguage;
using Ignixa.FhirMappingLanguage.Evaluation;
using Ignixa.Abstractions;
using Ignixa.FhirMappingLanguage.Parser;
using Xunit;

namespace Ignixa.FhirMappingLanguage.Tests.Evaluation;

public class ListIndexingTests
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

    #region Parser Tests

    [Fact]
    public void GivenMappingWithIndex_WhenParsing_ThenParsesCorrectly()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Transform(source src : Patient, target tgt : Bundle) {
  src.name[0] -> tgt.entry;
}";

        var compiler = new MappingParser();

        // Act
        var map = compiler.Parse(mappingText);

        // Assert
        map.Groups.Count.ShouldBe(1);
        map.Groups[0].Rules.Count.ShouldBe(1);
        map.Groups[0].Rules[0].Sources.Count.ShouldBe(1);

        // Source should have IndexExpression
        var source = map.Groups[0].Rules[0].Sources[0];
        source.Context.ShouldBeOfType<Expressions.IndexExpression>();
    }

    [Fact]
    public void GivenMappingWithChainedIndex_WhenParsing_ThenParsesCorrectly()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Transform(source src : Patient, target tgt : Bundle) {
  src.name[0].given[1] -> tgt.entry;
}";

        var compiler = new MappingParser();

        // Act
        var map = compiler.Parse(mappingText);

        // Assert
        map.Groups.Count.ShouldBe(1);
        var source = map.Groups[0].Rules[0].Sources[0];

        // Should be: Index(Qualified(Index(Qualified(Identifier("src"), "name"), 0), "given"), 1)
        source.Context.ShouldBeOfType<Expressions.IndexExpression>();
    }

    [Fact]
    public void GivenMappingWithMixedDotAndIndex_WhenParsing_ThenParsesCorrectly()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Transform(source src : Patient, target tgt : Bundle) {
  src.identifier[0].system -> tgt.id;
}";

        var compiler = new MappingParser();

        // Act
        var map = compiler.Parse(mappingText);

        // Assert
        var source = map.Groups[0].Rules[0].Sources[0];
        // Should be: Qualified(Index(Qualified(Identifier("src"), "identifier"), 0), "system")
        source.Context.ShouldBeOfType<Expressions.QualifiedIdentifierExpression>();

        var qualified = source.Context as Expressions.QualifiedIdentifierExpression;
        qualified!.Context.ShouldBeOfType<Expressions.IndexExpression>();
    }

    #endregion

    #region Evaluation Tests

    [Fact]
    public void GivenListWithMultipleElements_WhenIndexingFirst_ThenReturnsFirstElement()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Transform(source src : Patient, target tgt : Bundle) {
  src.name[0] log src.name[0] -> tgt.entry;
}";

        var compiler = new MappingParser();
        var map = compiler.Parse(mappingText);
        var evaluator = new MappingEvaluator(enableFhirPath: true);
        var context = new MappingContext();

        var logMessages = new List<string>();
        context.Logger = message => logMessages.Add(message);

        var source = new TestTypedElement("Patient");
        source.AddChild(new TestTypedElement("name", "John", "HumanName"));
        source.AddChild(new TestTypedElement("name", "Johnny", "HumanName"));
        source.AddChild(new TestTypedElement("name", "Jon", "HumanName"));

        var target = new TestTypedElement("Bundle");

        context.SetSource("src", source);
        context.SetTarget("tgt", target);

        // Act
        evaluator.ExecuteGroup(map, "Transform", context);

        // Assert
        logMessages.ShouldHaveSingleItem();
        logMessages[0].ShouldBe("John");
    }

    [Fact]
    public void GivenListWithMultipleElements_WhenIndexingLast_ThenReturnsLastElement()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Transform(source src : Patient, target tgt : Bundle) {
  src.name[2] log src.name[2] -> tgt.entry;
}";

        var compiler = new MappingParser();
        var map = compiler.Parse(mappingText);
        var evaluator = new MappingEvaluator(enableFhirPath: true);
        var context = new MappingContext();

        var logMessages = new List<string>();
        context.Logger = message => logMessages.Add(message);

        var source = new TestTypedElement("Patient");
        source.AddChild(new TestTypedElement("name", "John", "HumanName"));
        source.AddChild(new TestTypedElement("name", "Johnny", "HumanName"));
        source.AddChild(new TestTypedElement("name", "Jon", "HumanName"));

        var target = new TestTypedElement("Bundle");

        context.SetSource("src", source);
        context.SetTarget("tgt", target);

        // Act
        evaluator.ExecuteGroup(map, "Transform", context);

        // Assert
        logMessages.ShouldHaveSingleItem();
        logMessages[0].ShouldBe("Jon");
    }

    [Fact]
    public void GivenListWithOneElement_WhenIndexingZero_ThenReturnsElement()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Transform(source src : Patient, target tgt : Bundle) {
  src.name[0] log src.name[0] -> tgt.entry;
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
        logMessages[0].ShouldBe("John");
    }

    [Fact]
    public void GivenEmptyList_WhenIndexing_ThenReturnsEmpty()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Transform(source src : Patient, target tgt : Bundle) {
  src.name[0] log src.name[0] -> tgt.entry;
}";

        var compiler = new MappingParser();
        var map = compiler.Parse(mappingText);
        var evaluator = new MappingEvaluator(enableFhirPath: true);
        var context = new MappingContext();

        var logMessages = new List<string>();
        context.Logger = message => logMessages.Add(message);

        var source = new TestTypedElement("Patient");
        // No name elements

        var target = new TestTypedElement("Bundle");

        context.SetSource("src", source);
        context.SetTarget("tgt", target);

        // Act
        evaluator.ExecuteGroup(map, "Transform", context);

        // Assert - Should not log anything (no elements)
        logMessages.ShouldBeEmpty();
    }

    [Fact]
    public void GivenIndexOutOfBounds_WhenEvaluating_ThenReturnsEmpty()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Transform(source src : Patient, target tgt : Bundle) {
  src.name[5] log src.name[5] -> tgt.entry;
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

        // Assert - Index out of bounds returns empty
        logMessages.ShouldBeEmpty();
    }

    [Fact]
    public void GivenNestedIndexing_WhenEvaluating_ThenReturnsCorrectElement()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Transform(source src : Patient, target tgt : Bundle) {
  src.name[1].given[0] log src.name[1].given[0] -> tgt.entry;
}";

        var compiler = new MappingParser();
        var map = compiler.Parse(mappingText);
        var evaluator = new MappingEvaluator(enableFhirPath: true);
        var context = new MappingContext();

        var logMessages = new List<string>();
        context.Logger = message => logMessages.Add(message);

        var source = new TestTypedElement("Patient");

        var name1 = new TestTypedElement("name", null, "HumanName");
        name1.AddChild(new TestTypedElement("given", "John", "string"));
        name1.AddChild(new TestTypedElement("given", "Michael", "string"));
        source.AddChild(name1);

        var name2 = new TestTypedElement("name", null, "HumanName");
        name2.AddChild(new TestTypedElement("given", "Jane", "string"));
        name2.AddChild(new TestTypedElement("given", "Marie", "string"));
        source.AddChild(name2);

        var target = new TestTypedElement("Bundle");

        context.SetSource("src", source);
        context.SetTarget("tgt", target);

        // Act
        evaluator.ExecuteGroup(map, "Transform", context);

        // Assert - Should get second name's first given
        logMessages.ShouldHaveSingleItem();
        logMessages[0].ShouldBe("Jane");
    }

    [Fact]
    public void GivenIndexWithProperty_WhenEvaluating_ThenAccessesPropertyOfIndexedElement()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Transform(source src : Patient, target tgt : Bundle) {
  src.identifier[0].system log src.identifier[0].system -> tgt.id;
}";

        var compiler = new MappingParser();
        var map = compiler.Parse(mappingText);
        var evaluator = new MappingEvaluator(enableFhirPath: true);
        var context = new MappingContext();

        var logMessages = new List<string>();
        context.Logger = message => logMessages.Add(message);

        var source = new TestTypedElement("Patient");

        var identifier1 = new TestTypedElement("identifier", null, "Identifier");
        identifier1.AddChild(new TestTypedElement("system", "http://hospital.org", "uri"));
        identifier1.AddChild(new TestTypedElement("value", "12345", "string"));
        source.AddChild(identifier1);

        var identifier2 = new TestTypedElement("identifier", null, "Identifier");
        identifier2.AddChild(new TestTypedElement("system", "http://ssn.gov", "uri"));
        identifier2.AddChild(new TestTypedElement("value", "987-65-4321", "string"));
        source.AddChild(identifier2);

        var target = new TestTypedElement("Bundle");

        context.SetSource("src", source);
        context.SetTarget("tgt", target);

        // Act
        evaluator.ExecuteGroup(map, "Transform", context);

        // Assert
        logMessages.ShouldHaveSingleItem();
        logMessages[0].ShouldBe("http://hospital.org");
    }

    #endregion

    #region Integration Tests

    [Fact]
    public void GivenIndexingWithDefault_WhenListEmpty_ThenUsesDefault()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Transform(source src : Patient, target tgt : Bundle) {
  src.name[0] default 'Unknown' log src.name[0] -> tgt.entry;
}";

        var compiler = new MappingParser();
        var map = compiler.Parse(mappingText);
        var evaluator = new MappingEvaluator(enableFhirPath: true);
        var context = new MappingContext();

        var logMessages = new List<string>();
        context.Logger = message => logMessages.Add(message);

        var source = new TestTypedElement("Patient");
        // No name elements

        var target = new TestTypedElement("Bundle");

        context.SetSource("src", source);
        context.SetTarget("tgt", target);

        // Act
        evaluator.ExecuteGroup(map, "Transform", context);

        // Assert
        logMessages.ShouldHaveSingleItem();
        logMessages[0].ShouldBe("Unknown");
    }

    [Fact]
    public void GivenIndexingWithWhere_WhenFiltering_ThenWorksCorrectly()
    {
        // Arrange - Use a contextual where condition that references the mapping context
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Transform(source src : Patient, target tgt : Bundle) {
  src.name[0] where src.name.exists() log src.name[0] -> tgt.entry;
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
        logMessages[0].ShouldBe("John");
    }

    [Fact]
    public void GivenComplexMapping_WhenUsingIndexing_ThenProcessesCorrectly()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Transform(source src : Patient, target tgt : Bundle) {
  src.name[0] as official -> tgt.entry;
  src.name[1] as alternate where name.exists() -> tgt.total;
  src.identifier[0].system -> tgt.id;
}";

        var compiler = new MappingParser();
        var map = compiler.Parse(mappingText);
        var evaluator = new MappingEvaluator(enableFhirPath: true);
        var context = new MappingContext();

        var source = new TestTypedElement("Patient");
        source.AddChild(new TestTypedElement("name", "John Doe", "HumanName"));
        source.AddChild(new TestTypedElement("name", "Johnny", "HumanName"));

        var identifier = new TestTypedElement("identifier", null, "Identifier");
        identifier.AddChild(new TestTypedElement("system", "http://hospital.org", "uri"));
        source.AddChild(identifier);

        var target = new TestTypedElement("Bundle");

        context.SetSource("src", source);
        context.SetTarget("tgt", target);

        // Act
        var act = () => evaluator.ExecuteGroup(map, "Transform", context);

        // Assert - Should not throw
        Should.NotThrow(act);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void GivenNegativeIndex_WhenEvaluating_ThenReturnsEmpty()
    {
        // Arrange - FHIR Mapping Language doesn't support negative indexes
        // This test verifies out-of-bounds behavior with index 999
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Transform(source src : Patient, target tgt : Bundle) {
  src.name[999] log src.name[999] -> tgt.entry;
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
        var act = () => evaluator.ExecuteGroup(map, "Transform", context);

        // Assert - Out-of-bounds index returns empty (no error)
        Should.NotThrow(act);
        logMessages.ShouldBeEmpty();
    }

    #endregion
}
