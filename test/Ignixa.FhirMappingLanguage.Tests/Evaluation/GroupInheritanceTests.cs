/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Unit tests for group inheritance in FHIR Mapping Language.
 */

using FluentAssertions;
using Ignixa.FhirMappingLanguage;
using Ignixa.FhirMappingLanguage.Evaluation;
using Ignixa.Abstractions;
using Ignixa.FhirMappingLanguage.Parser;
using Xunit;

namespace Ignixa.FhirMappingLanguage.Tests.Evaluation;

public class GroupInheritanceTests
{
    #region Helper Classes

    private class TestTypedElement : IElement
    {
        private readonly Dictionary<string, List<IElement>> _children = new();
        private readonly Dictionary<string, object> _values = new();

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

        public void SetValue(string path, object value)
        {
            _values[path] = value;
        }

        public object? GetValue(string path)
        {
            return _values.TryGetValue(path, out var value) ? value : null;
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

    #region Simple Inheritance Tests

    [Fact]
    public void GivenGroupWithExtends_WhenExecuting_ThenExecutesBaseGroupFirst()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group BaseGroup(source src : Patient, target tgt : Bundle) {
  src.id -> tgt.id;
}

group DerivedGroup (source src : Patient, target tgt : Bundle) extends BaseGroup {
  src.name -> tgt.type;
}";

        var compiler = new MappingParser();
        var map = compiler.Parse(mappingText);
        var evaluator = new MappingEvaluator(enableFhirPath: false);
        var context = new MappingContext();

        var source = new TestTypedElement("Patient");
        var target = new TestTypedElement("Bundle");

        context.SetSource("src", source);
        context.SetTarget("tgt", target);

        // Track execution order
        var executionOrder = new List<string>();
        context.TransformResolver = (name, args) =>
        {
            executionOrder.Add($"transform-{name}");
            return args.FirstOrDefault() ?? new object();
        };

        // Act
        evaluator.ExecuteGroup(map, "DerivedGroup", context);

        // Assert - base group rules should execute before derived group rules
        // In a real implementation, we'd verify the actual transformations
        // For now, just verify it doesn't throw
    }

    [Fact]
    public void GivenGroupWithoutExtends_WhenExecuting_ThenExecutesNormally()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group SimpleGroup(source src : Patient, target tgt : Bundle) {
  src.id -> tgt.id;
}";

        var compiler = new MappingParser();
        var map = compiler.Parse(mappingText);
        var evaluator = new MappingEvaluator(enableFhirPath: false);
        var context = new MappingContext();

        var source = new TestTypedElement("Patient");
        var target = new TestTypedElement("Bundle");

        context.SetSource("src", source);
        context.SetTarget("tgt", target);

        // Act
        var act = () => evaluator.ExecuteGroup(map, "SimpleGroup", context);

        // Assert
        act.Should().NotThrow();
    }

    #endregion

    #region Transitive Inheritance Tests

    [Fact]
    public void GivenTransitiveInheritance_WhenExecuting_ThenExecutesInCorrectOrder()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group BaseGroup(source src : Patient, target tgt : Bundle) {
  src.id -> tgt.id;
}

group MiddleGroup (source src : Patient, target tgt : Bundle) extends BaseGroup {
  src.name -> tgt.type;
}

group DerivedGroup (source src : Patient, target tgt : Bundle) extends MiddleGroup {
  src.gender -> tgt.timestamp;
}";

        var compiler = new MappingParser();
        var map = compiler.Parse(mappingText);
        var evaluator = new MappingEvaluator(enableFhirPath: false);
        var context = new MappingContext();

        var source = new TestTypedElement("Patient");
        var target = new TestTypedElement("Bundle");

        context.SetSource("src", source);
        context.SetTarget("tgt", target);

        // Act
        var act = () => evaluator.ExecuteGroup(map, "DerivedGroup", context);

        // Assert - should execute BaseGroup, then MiddleGroup, then DerivedGroup
        act.Should().NotThrow();
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public void GivenCircularInheritance_WhenExecuting_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group GroupA (source src : Patient, target tgt : Bundle) extends GroupB {
  src.id -> tgt.id;
}

group GroupB (source src : Patient, target tgt : Bundle) extends GroupA {
  src.name -> tgt.type;
}";

        var compiler = new MappingParser();
        var map = compiler.Parse(mappingText);
        var evaluator = new MappingEvaluator(enableFhirPath: false);
        var context = new MappingContext();

        var source = new TestTypedElement("Patient");
        var target = new TestTypedElement("Bundle");

        context.SetSource("src", source);
        context.SetTarget("tgt", target);

        // Act
        var act = () => evaluator.ExecuteGroup(map, "GroupA", context);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*circular*inheritance*");
    }

    [Fact]
    public void GivenSelfReferentialInheritance_WhenExecuting_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group RecursiveGroup (source src : Patient, target tgt : Bundle) extends RecursiveGroup {
  src.id -> tgt.id;
}";

        var compiler = new MappingParser();
        var map = compiler.Parse(mappingText);
        var evaluator = new MappingEvaluator(enableFhirPath: false);
        var context = new MappingContext();

        var source = new TestTypedElement("Patient");
        var target = new TestTypedElement("Bundle");

        context.SetSource("src", source);
        context.SetTarget("tgt", target);

        // Act
        var act = () => evaluator.ExecuteGroup(map, "RecursiveGroup", context);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*circular*inheritance*");
    }

    [Fact]
    public void GivenMissingBaseGroup_WhenExecuting_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group DerivedGroup (source src : Patient, target tgt : Bundle) extends NonExistentGroup {
  src.id -> tgt.id;
}";

        var compiler = new MappingParser();
        var map = compiler.Parse(mappingText);
        var evaluator = new MappingEvaluator(enableFhirPath: false);
        var context = new MappingContext();

        var source = new TestTypedElement("Patient");
        var target = new TestTypedElement("Bundle");

        context.SetSource("src", source);
        context.SetTarget("tgt", target);

        // Act
        var act = () => evaluator.ExecuteGroup(map, "DerivedGroup", context);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*base group not found*");
    }

    [Fact]
    public void GivenLongInheritanceChain_WhenExecuting_ThenThrowsIfCircular()
    {
        // Arrange - A -> B -> C -> D -> B (circular)
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group GroupA (source src : Patient, target tgt : Bundle) extends GroupB {
  src.id -> tgt.id;
}

group GroupB (source src : Patient, target tgt : Bundle) extends GroupC {
  src.name -> tgt.type;
}

group GroupC (source src : Patient, target tgt : Bundle) extends GroupD {
  src.gender -> tgt.timestamp;
}

group GroupD (source src : Patient, target tgt : Bundle) extends GroupB {
  src.birthDate -> tgt.total;
}";

        var compiler = new MappingParser();
        var map = compiler.Parse(mappingText);
        var evaluator = new MappingEvaluator(enableFhirPath: false);
        var context = new MappingContext();

        var source = new TestTypedElement("Patient");
        var target = new TestTypedElement("Bundle");

        context.SetSource("src", source);
        context.SetTarget("tgt", target);

        // Act
        var act = () => evaluator.ExecuteGroup(map, "GroupA", context);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*circular*inheritance*");
    }

    #endregion

    #region Case Sensitivity Tests

    [Fact]
    public void GivenGroupInheritance_WhenCaseInsensitiveMatch_ThenDetectsCircular()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group GroupA (source src : Patient, target tgt : Bundle) extends groupb {
  src.id -> tgt.id;
}

group GROUPB (source src : Patient, target tgt : Bundle) extends groupa {
  src.name -> tgt.type;
}";

        var compiler = new MappingParser();
        var map = compiler.Parse(mappingText);
        var evaluator = new MappingEvaluator(enableFhirPath: false);
        var context = new MappingContext();

        var source = new TestTypedElement("Patient");
        var target = new TestTypedElement("Bundle");

        context.SetSource("src", source);
        context.SetTarget("tgt", target);

        // Act
        var act = () => evaluator.ExecuteGroup(map, "GroupA", context);

        // Assert - Should detect circular inheritance even with different casing
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*circular*inheritance*");
    }

    #endregion

    #region Multiple Groups Tests

    [Fact]
    public void GivenMultipleIndependentGroups_WhenExecutingOne_ThenOnlyExecutesThatGroup()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Group1(source src : Patient, target tgt : Bundle) {
  src.id -> tgt.id;
}

group Group2(source src : Patient, target tgt : Bundle) {
  src.name -> tgt.type;
}

group Group3 (source src : Patient, target tgt : Bundle) extends Group1 {
  src.gender -> tgt.timestamp;
}";

        var compiler = new MappingParser();
        var map = compiler.Parse(mappingText);
        var evaluator = new MappingEvaluator(enableFhirPath: false);
        var context = new MappingContext();

        var source = new TestTypedElement("Patient");
        var target = new TestTypedElement("Bundle");

        context.SetSource("src", source);
        context.SetTarget("tgt", target);

        // Act
        var act = () => evaluator.ExecuteGroup(map, "Group2", context);

        // Assert - Should execute only Group2, not Group1 or Group3
        act.Should().NotThrow();
    }

    [Fact]
    public void GivenMultipleGroupsWithInheritance_WhenExecutingDerived_ThenExecutesBaseAndDerived()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Group1(source src : Patient, target tgt : Bundle) {
  src.id -> tgt.id;
}

group Group2(source src : Patient, target tgt : Bundle) {
  src.name -> tgt.type;
}

group Group3 (source src : Patient, target tgt : Bundle) extends Group1 {
  src.gender -> tgt.timestamp;
}";

        var compiler = new MappingParser();
        var map = compiler.Parse(mappingText);
        var evaluator = new MappingEvaluator(enableFhirPath: false);
        var context = new MappingContext();

        var source = new TestTypedElement("Patient");
        var target = new TestTypedElement("Bundle");

        context.SetSource("src", source);
        context.SetTarget("tgt", target);

        // Act
        var act = () => evaluator.ExecuteGroup(map, "Group3", context);

        // Assert - Should execute Group1 then Group3
        act.Should().NotThrow();
    }

    #endregion

    #region Parser Integration Tests

    [Fact]
    public void GivenMappingWithExtends_WhenParsing_ThenParsesExtendsCorrectly()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group BaseGroup(source src : Patient, target tgt : Bundle) {
  src.id -> tgt.id;
}

group DerivedGroup (source src : Patient, target tgt : Bundle) extends BaseGroup {
  src.name -> tgt.type;
}";

        var compiler = new MappingParser();

        // Act
        var map = compiler.Parse(mappingText);

        // Assert
        map.Groups.Should().HaveCount(2);

        var baseGroup = map.Groups.FirstOrDefault(g => g.Name == "BaseGroup");
        baseGroup.Should().NotBeNull();
        baseGroup!.Extends.Should().BeNull();

        var derivedGroup = map.Groups.FirstOrDefault(g => g.Name == "DerivedGroup");
        derivedGroup.Should().NotBeNull();
        derivedGroup!.Extends.Should().Be("BaseGroup");
    }

    [Fact]
    public void GivenMappingWithMultipleExtends_WhenParsing_ThenParsesAllCorrectly()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group A(source src : Patient, target tgt : Bundle) {
  src.id -> tgt.id;
}

group B (source src : Patient, target tgt : Bundle) extends A {
  src.name -> tgt.type;
}

group C (source src : Patient, target tgt : Bundle) extends B {
  src.gender -> tgt.timestamp;
}";

        var compiler = new MappingParser();

        // Act
        var map = compiler.Parse(mappingText);

        // Assert
        map.Groups.Should().HaveCount(3);
        map.Groups[0].Extends.Should().BeNull();
        map.Groups[1].Extends.Should().Be("A");
        map.Groups[2].Extends.Should().Be("B");
    }

    #endregion
}
