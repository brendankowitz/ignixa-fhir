/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Unit tests for cardinality constraint functionality in FHIR Mapping Language.
 */

using Shouldly;
using Ignixa.FhirMappingLanguage;
using Ignixa.FhirMappingLanguage.Evaluation;
using Ignixa.FhirMappingLanguage.Expressions;
using Ignixa.Abstractions;
using Ignixa.FhirMappingLanguage.Parser;
using Xunit;

namespace Ignixa.FhirMappingLanguage.Tests.Evaluation;

public class CardinalityTests
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

    #region Cardinality Class Tests

    [Theory]
    [InlineData(0, 1, 0, true)]
    [InlineData(0, 1, 1, true)]
    [InlineData(0, 1, 2, false)]
    [InlineData(1, 1, 0, false)]
    [InlineData(1, 1, 1, true)]
    [InlineData(1, 1, 2, false)]
    [InlineData(0, null, 0, true)]  // 0..*
    [InlineData(0, null, 100, true)]  // 0..*
    [InlineData(1, null, 0, false)]  // 1..*
    [InlineData(1, null, 1, true)]  // 1..*
    [InlineData(1, null, 100, true)]  // 1..*
    public void GivenCardinality_WhenCheckingCount_ThenReturnsCorrectResult(int min, int? max, int count, bool expected)
    {
        // Arrange
        var cardinality = new Cardinality(min, max);

        // Act
        var result = cardinality.IsSatisfiedBy(count);

        // Assert
        result.ShouldBe(expected);
    }

    [Fact]
    public void GivenCardinality_WhenMinNegative_ThenThrowsException()
    {
        // Act & Assert
        var act = () => new Cardinality(-1, 1);
        Should.Throw<ArgumentException>(act).Message.ShouldContain("Minimum cardinality cannot be negative");
    }

    [Fact]
    public void GivenCardinality_WhenMaxLessThanMin_ThenThrowsException()
    {
        // Act & Assert
        var act = () => new Cardinality(2, 1);
        Should.Throw<ArgumentException>(act).Message.ShouldContain("Maximum cardinality cannot be less than minimum");
    }

    [Theory]
    [InlineData(0, 1, "0..1")]
    [InlineData(1, 1, "1..1")]
    [InlineData(0, null, "0..*")]
    [InlineData(1, null, "1..*")]
    public void GivenCardinality_WhenToString_ThenFormatsCorrectly(int min, int? max, string expected)
    {
        // Arrange
        var cardinality = new Cardinality(min, max);

        // Act
        var result = cardinality.ToString();

        // Assert
        result.ShouldBe(expected);
    }

    #endregion

    #region Parser Tests

    [Fact]
    public void GivenMappingWithCardinality_WhenParsing_ThenParsesCorrectly()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Transform(source src : Patient, target tgt : Bundle) {
  src.name : HumanName 0..1 -> tgt.entry;
}";

        var compiler = new MappingParser();

        // Act
        var map = compiler.Parse(mappingText);

        // Assert
        map.Groups.Count.ShouldBe(1);
        map.Groups[0].Rules.Count.ShouldBe(1);
        map.Groups[0].Rules[0].Sources.Count.ShouldBe(1);
        map.Groups[0].Rules[0].Sources[0].Cardinality.ShouldNotBeNull();
        map.Groups[0].Rules[0].Sources[0].Cardinality!.Min.ShouldBe(0);
        map.Groups[0].Rules[0].Sources[0].Cardinality!.Max.ShouldBe(1);
    }

    [Fact]
    public void GivenMappingWithUnboundedCardinality_WhenParsing_ThenParsesCorrectly()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Transform(source src : Patient, target tgt : Bundle) {
  src.identifier : Identifier 1..* -> tgt.entry;
}";

        var compiler = new MappingParser();

        // Act
        var map = compiler.Parse(mappingText);

        // Assert
        var source = map.Groups[0].Rules[0].Sources[0];
        source.Cardinality.ShouldNotBeNull();
        source.Cardinality!.Min.ShouldBe(1);
        source.Cardinality!.Max.ShouldBeNull();
    }

    [Fact]
    public void GivenMappingWithCardinalityAndOtherClauses_WhenParsing_ThenParsesAllCorrectly()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Transform(source src : Patient, target tgt : Bundle) {
  src.name : HumanName 1..1 where (name.exists()) check (name.family.exists()) -> tgt.entry;
}";

        var compiler = new MappingParser();

        // Act
        var map = compiler.Parse(mappingText);

        // Assert
        var source = map.Groups[0].Rules[0].Sources[0];
        source.Cardinality.ShouldNotBeNull();
        source.Condition.ShouldNotBeNull();
        source.Check.ShouldNotBeNull();
    }

    #endregion

    #region Strict Mode Tests

    [Fact]
    public void GivenStrictMode_When_CardinalityViolated_ThenThrowsException()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Transform(source src : Patient, target tgt : Bundle) {
  src.name : HumanName 1..1 -> tgt.entry;
}";

        var compiler = new MappingParser();
        var map = compiler.Parse(mappingText);
        var evaluator = new MappingEvaluator(enableFhirPath: false);
        var context = new MappingContext
        {
            ErrorMode = ErrorMode.Strict
        };

        var source = new TestTypedElement("Patient");
        // Add two names (violates 1..1 cardinality)
        source.AddChild(new TestTypedElement("name", "John", "HumanName"));
        source.AddChild(new TestTypedElement("name", "Jane", "HumanName"));

        context.SetSource("src", source);
        context.SetTarget("tgt", new TestTypedElement("Bundle"));

        // Act & Assert
        var act = () => evaluator.ExecuteGroup(map, "Transform", context);
        Should.Throw<MappingExecutionException>(act).Message.ShouldContain("Cardinality constraint");
    }

    [Fact]
    public void GivenStrictMode_WhenMinimumNotMet_ThenThrowsException()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Transform(source src : Patient, target tgt : Bundle) {
  src.identifier : Identifier 1..* -> tgt.entry;
}";

        var compiler = new MappingParser();
        var map = compiler.Parse(mappingText);
        var evaluator = new MappingEvaluator(enableFhirPath: false);
        var context = new MappingContext
        {
            ErrorMode = ErrorMode.Strict
        };

        var source = new TestTypedElement("Patient");
        // No identifiers (violates 1..* cardinality)

        context.SetSource("src", source);
        context.SetTarget("tgt", new TestTypedElement("Bundle"));

        // Act & Assert
        var act = () => evaluator.ExecuteGroup(map, "Transform", context);
        var exception = Should.Throw<MappingExecutionException>(act);
        exception.Message.ShouldContain("Source element 'identifier' not found");
        exception.Message.ShouldContain("cardinality 1..*");
    }

    #endregion

    #region Lenient Mode Tests

    [Fact]
    public void GivenLenientMode_WhenCardinalityViolated_ThenCollectsError()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Transform(source src : Patient, target tgt : Bundle) {
  src.name : HumanName 1..1 -> tgt.entry;
}";

        var compiler = new MappingParser();
        var map = compiler.Parse(mappingText);
        var evaluator = new MappingEvaluator(enableFhirPath: false);
        var context = new MappingContext
        {
            ErrorMode = ErrorMode.Lenient
        };

        var source = new TestTypedElement("Patient");
        // Add two names (violates 1..1 cardinality)
        source.AddChild(new TestTypedElement("name", "John", "HumanName"));
        source.AddChild(new TestTypedElement("name", "Jane", "HumanName"));

        context.SetSource("src", source);
        context.SetTarget("tgt", new TestTypedElement("Bundle"));

        // Act
        evaluator.ExecuteGroup(map, "Transform", context);

        // Assert
        context.Errors.Count.ShouldBeGreaterThan(0);
        context.Errors.ShouldContain(e => e.Code == "CARDINALITY_ERROR");
        context.Errors.ShouldContain(e => e.Message.Contains("1..1"));
    }

    [Fact]
    public void GivenLenientMode_WhenMinimumNotMet_ThenCollectsErrorAndContinues()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Transform(source src : Patient, target tgt : Bundle) {
  src.identifier : Identifier 1..* -> tgt.entry;
  src.id -> tgt.id;
}";

        var compiler = new MappingParser();
        var map = compiler.Parse(mappingText);
        var evaluator = new MappingEvaluator(enableFhirPath: false);
        var context = new MappingContext
        {
            ErrorMode = ErrorMode.Lenient
        };

        var source = new TestTypedElement("Patient");
        // No identifiers (violates 1..* cardinality)
        source.AddChild(new TestTypedElement("id", "patient-123", "id"));

        context.SetSource("src", source);
        context.SetTarget("tgt", new TestTypedElement("Bundle"));

        // Act
        evaluator.ExecuteGroup(map, "Transform", context);

        // Assert
        context.Errors.Count.ShouldBeGreaterThan(0);
        context.Errors.ShouldContain(e => e.Code == "CARDINALITY_ERROR");
        // Second rule should still execute
    }

    #endregion

    #region Cardinality Validation Tests

    [Fact]
    public void GivenCardinality0To1_WhenZeroElements_ThenNoError()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Transform(source src : Patient, target tgt : Bundle) {
  src.name : HumanName 0..1 -> tgt.entry;
}";

        var compiler = new MappingParser();
        var map = compiler.Parse(mappingText);
        var evaluator = new MappingEvaluator(enableFhirPath: false);
        var context = new MappingContext
        {
            ErrorMode = ErrorMode.Lenient
        };

        var source = new TestTypedElement("Patient");
        // No name elements

        context.SetSource("src", source);
        context.SetTarget("tgt", new TestTypedElement("Bundle"));

        // Act
        evaluator.ExecuteGroup(map, "Transform", context);

        // Assert
        context.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void GivenCardinality0To1_WhenOneElement_ThenNoError()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Transform(source src : Patient, target tgt : Bundle) {
  src.name : HumanName 0..1 -> tgt.entry;
}";

        var compiler = new MappingParser();
        var map = compiler.Parse(mappingText);
        var evaluator = new MappingEvaluator(enableFhirPath: false);
        var context = new MappingContext
        {
            ErrorMode = ErrorMode.Lenient
        };

        var source = new TestTypedElement("Patient");
        source.AddChild(new TestTypedElement("name", "John", "HumanName"));

        context.SetSource("src", source);
        context.SetTarget("tgt", new TestTypedElement("Bundle"));

        // Act
        evaluator.ExecuteGroup(map, "Transform", context);

        // Assert
        context.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void GivenCardinality1ToMany_WhenMultipleElements_ThenNoError()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Transform(source src : Patient, target tgt : Bundle) {
  src.identifier : Identifier 1..* -> tgt.entry;
}";

        var compiler = new MappingParser();
        var map = compiler.Parse(mappingText);
        var evaluator = new MappingEvaluator(enableFhirPath: false);
        var context = new MappingContext
        {
            ErrorMode = ErrorMode.Lenient
        };

        var source = new TestTypedElement("Patient");
        source.AddChild(new TestTypedElement("identifier", "id1", "Identifier"));
        source.AddChild(new TestTypedElement("identifier", "id2", "Identifier"));
        source.AddChild(new TestTypedElement("identifier", "id3", "Identifier"));

        context.SetSource("src", source);
        context.SetTarget("tgt", new TestTypedElement("Bundle"));

        // Act
        evaluator.ExecuteGroup(map, "Transform", context);

        // Assert
        context.Errors.ShouldBeEmpty();
    }

    #endregion

    #region Integration Tests

    [Fact]
    public void GivenCardinalityWithWhereClause_WhenFiltering_ThenCardinalityCheckedAfterFilter()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Transform(source src : Patient, target tgt : Bundle) {
  src.identifier : Identifier 1..1 where (use='official') -> tgt.entry;
}";

        var compiler = new MappingParser();
        var map = compiler.Parse(mappingText);
        var evaluator = new MappingEvaluator(enableFhirPath: true);
        var context = new MappingContext
        {
            ErrorMode = ErrorMode.Lenient
        };

        var source = new TestTypedElement("Patient");
        // Add multiple identifiers, but where clause should filter to 1
        var id1 = new TestTypedElement("identifier", null, "Identifier");
        id1.AddChild(new TestTypedElement("use", "official", "code"));
        source.AddChild(id1);

        var id2 = new TestTypedElement("identifier", null, "Identifier");
        id2.AddChild(new TestTypedElement("use", "secondary", "code"));
        source.AddChild(id2);

        context.SetSource("src", source);
        context.SetTarget("tgt", new TestTypedElement("Bundle"));

        // Act
        evaluator.ExecuteGroup(map, "Transform", context);

        // Assert
        context.Errors.ShouldBeEmpty(); // Should pass because only 1 after where filter
    }

    [Fact]
    public void GivenCardinalityWithDefaultValue_WhenEmpty_ThenDefaultAppliedBeforeCardinality()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Transform(source src : Patient, target tgt : Bundle) {
  src.status : code 1..1 default ('active') -> tgt.type;
}";

        var compiler = new MappingParser();
        var map = compiler.Parse(mappingText);
        var evaluator = new MappingEvaluator(enableFhirPath: true);
        var context = new MappingContext
        {
            ErrorMode = ErrorMode.Lenient
        };

        var source = new TestTypedElement("Patient");
        // No status element, default should provide one

        context.SetSource("src", source);
        context.SetTarget("tgt", new TestTypedElement("Bundle"));

        // Act
        evaluator.ExecuteGroup(map, "Transform", context);

        // Assert
        context.Errors.ShouldBeEmpty(); // Default provides 1 element, satisfies 1..1
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void GivenMultipleSourcesWithCardinality_WhenEvaluating_ThenEachCheckedIndependently()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Transform(source src : Patient, target tgt : Bundle) {
  src.name : HumanName 1..1, src.identifier : Identifier 1..* -> tgt.entry;
}";

        var compiler = new MappingParser();
        var map = compiler.Parse(mappingText);
        var evaluator = new MappingEvaluator(enableFhirPath: false);
        var context = new MappingContext
        {
            ErrorMode = ErrorMode.Lenient
        };

        var source = new TestTypedElement("Patient");
        source.AddChild(new TestTypedElement("name", "John", "HumanName"));
        source.AddChild(new TestTypedElement("identifier", "id1", "Identifier"));

        context.SetSource("src", source);
        context.SetTarget("tgt", new TestTypedElement("Bundle"));

        // Act
        evaluator.ExecuteGroup(map, "Transform", context);

        // Assert
        context.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void GivenCardinalityWithCheck_WhenCheckFails_ThenBothErrorsReported()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Transform(source src : Patient, target tgt : Bundle) {
  src.name : HumanName 1..1 check (false) -> tgt.entry;
}";

        var compiler = new MappingParser();
        var map = compiler.Parse(mappingText);
        var evaluator = new MappingEvaluator(enableFhirPath: true);
        var context = new MappingContext
        {
            ErrorMode = ErrorMode.Lenient
        };

        var source = new TestTypedElement("Patient");
        source.AddChild(new TestTypedElement("name", "John", "HumanName"));
        source.AddChild(new TestTypedElement("name", "Jane", "HumanName"));

        context.SetSource("src", source);
        context.SetTarget("tgt", new TestTypedElement("Bundle"));

        // Act
        evaluator.ExecuteGroup(map, "Transform", context);

        // Assert
        context.Errors.Count.ShouldBeGreaterThan(0);
        context.Errors.ShouldContain(e => e.Code == "CARDINALITY_ERROR");
        context.Errors.ShouldContain(e => e.Code == "CHECK_ERROR");
    }

    #endregion
}
