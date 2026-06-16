/*
 * Tests for instance selector validation behavior.
 * Tests what happens with invalid property names, unknown fields, etc.
 */

using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Parser;
using Shouldly;
using Xunit;

namespace Ignixa.FhirPath.Tests.Evaluation;

public class InstanceSelectorValidationTests
{
    private readonly FhirPathParser _parser = new();
    private readonly FhirPathEvaluator _evaluator = new();

    private static IElement CreateIntegerElement() => new PrimitiveElement(1, "integer");

    [Fact]
    public void GivenUnknownPropertyName_WhenInstanceSelector_ThenCreatesPropertyAnyway()
    {
        // Arrange - using a made-up property name that doesn't exist in FHIR
        var expression = "Patient { completelyMadeUpField: 'value', anotherInvalidOne: 123 }";

        // Act
        var ast = _parser.Parse(expression);
        var root = CreateIntegerElement();
        var result = _evaluator.Evaluate(root, ast).ToList();

        // Assert - no schema validation, so it creates the object with these properties
        result.Count.ShouldBe(1);
        result[0].InstanceType.ShouldBe("Patient");

        var unknownField = result[0].Children("completelyMadeUpField").SingleOrDefault();
        unknownField.ShouldNotBeNull();
        unknownField.Value.ShouldBe("value");

        var anotherField = result[0].Children("anotherInvalidOne").SingleOrDefault();
        anotherField.ShouldNotBeNull();
        anotherField.Value.ShouldBe(123);
    }

    [Fact]
    public void GivenPropertyWithHyphen_WhenInstanceSelector_ThenParseFailsBecauseNotValidIdentifier()
    {
        // Arrange - hyphens are not valid in unquoted identifiers
        var expression = "Patient { birth-date: @2020-01-01 }";

        // Act & Assert - should fail at parse time because 'birth-date' is not a valid identifier
        // The parser sees: birth, minus operator, date (three separate tokens)
        Should.Throw<FormatException>(() => _parser.Parse(expression));
    }

    [Fact]
    public void GivenDelimitedIdentifierWithHyphen_WhenInstanceSelector_ThenWorks()
    {
        // Arrange - using backticks to allow special characters in property name
        var expression = "Patient { `birth-date`: @2020-01-01 }";

        // Act
        var ast = _parser.Parse(expression);
        var root = CreateIntegerElement();
        var result = _evaluator.Evaluate(root, ast).ToList();

        // Assert - delimited identifiers allow any characters
        result.Count.ShouldBe(1);
        result[0].InstanceType.ShouldBe("Patient");

        // The property name should be preserved (without backticks)
        var birthDate = result[0].Children("birth-date").SingleOrDefault();
        birthDate.ShouldNotBeNull();
        birthDate.Value.ShouldBe("2020-01-01");
    }

    [Fact]
    public void GivenPropertyWithSpecialChars_WhenInstanceSelector_ThenParseFailsForUnquoted()
    {
        // Arrange - special characters like @ are not valid in identifiers
        var expression = "Patient { field@name: 'value' }";

        // Act & Assert
        Should.Throw<FormatException>(() => _parser.Parse(expression));
    }

    [Fact]
    public void GivenDelimitedIdentifierWithSpecialChars_WhenInstanceSelector_ThenWorks()
    {
        // Arrange - backticks allow special characters
        var expression = "Patient { `field@name`: 'value', `field.with.dots`: 123 }";

        // Act
        var ast = _parser.Parse(expression);
        var root = CreateIntegerElement();
        var result = _evaluator.Evaluate(root, ast).ToList();

        // Assert
        result.Count.ShouldBe(1);
        result[0].InstanceType.ShouldBe("Patient");

        result[0].Children("field@name").SingleOrDefault()?.Value.ShouldBe("value");
        result[0].Children("field.with.dots").SingleOrDefault()?.Value.ShouldBe(123);
    }

    [Fact]
    public void GivenUnknownTypeName_WhenInstanceSelector_ThenCreatesTypeAnyway()
    {
        // Arrange - using a completely made-up type name
        var expression = "CompletelyMadeUpType { field: 'value' }";

        // Act
        var ast = _parser.Parse(expression);
        var root = CreateIntegerElement();
        var result = _evaluator.Evaluate(root, ast).ToList();

        // Assert - no type validation, creates the object anyway
        result.Count.ShouldBe(1);
        result[0].InstanceType.ShouldBe("CompletelyMadeUpType");
        result[0].Children("field").SingleOrDefault()?.Value.ShouldBe("value");
    }

    [Fact]
    public void GivenFhirTypeWithCorrectProperty_WhenInstanceSelector_ThenWorks()
    {
        // Arrange - using actual FHIR types/properties (no validation, but documenting expected usage)
        var expression = "Coding { system: 'http://loinc.org', code: '1234-5', display: 'Test Code' }";

        // Act
        var ast = _parser.Parse(expression);
        var root = CreateIntegerElement();
        var result = _evaluator.Evaluate(root, ast).ToList();

        // Assert
        result.Count.ShouldBe(1);
        result[0].InstanceType.ShouldBe("Coding");
        result[0].Children("system").SingleOrDefault()?.Value.ShouldBe("http://loinc.org");
        result[0].Children("code").SingleOrDefault()?.Value.ShouldBe("1234-5");
        result[0].Children("display").SingleOrDefault()?.Value.ShouldBe("Test Code");
    }

    private class PrimitiveElement : IElement
    {
        public PrimitiveElement(object value, string type)
        {
            Value = value;
            InstanceType = type;
        }

        public string Name => string.Empty;
        public string InstanceType { get; }
        public object Value { get; }
        public string Location => string.Empty;
        public IType? Type => null;
        public bool HasPrimitiveValue => true;
        public IReadOnlyList<IElement> Children(string? name = null) => [];
        public T? Meta<T>() where T : class => null;
    }
}
