/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Unit tests for basic type validator.
 */

using FluentAssertions;
using Ignixa.FhirMappingLanguage.TypeSystem;
using Ignixa.Abstractions;
using Xunit;

namespace Ignixa.FhirMappingLanguage.Tests.TypeSystem;

public class BasicTypeValidatorTests
{
    #region Helper Classes

    private class TestTypedElement : IElement
    {
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
        public IType? Definition => null;

        public IReadOnlyList<IElement> Children(string? name) => new List<IElement>();

        public T? Meta<T>() where T : class => null;
    }

    #endregion

    #region Type Resolution Tests

    [Theory]
    [InlineData("string", TypeCategory.Primitive)]
    [InlineData("integer", TypeCategory.Primitive)]
    [InlineData("decimal", TypeCategory.Primitive)]
    [InlineData("boolean", TypeCategory.Primitive)]
    [InlineData("date", TypeCategory.Primitive)]
    [InlineData("dateTime", TypeCategory.Primitive)]
    [InlineData("code", TypeCategory.Primitive)]
    [InlineData("uri", TypeCategory.Primitive)]
    [InlineData("url", TypeCategory.Primitive)]
    [InlineData("id", TypeCategory.Primitive)]
    public void GivenPrimitiveType_WhenResolving_ThenReturnsPrimitiveCategory(string typeName, TypeCategory expected)
    {
        // Arrange
        var validator = new BasicTypeValidator();

        // Act
        var typeInfo = validator.ResolveType(typeName);

        // Assert
        typeInfo.Should().NotBeNull();
        typeInfo!.Category.Should().Be(expected);
        typeInfo.IsPrimitive.Should().BeTrue();
    }

    [Theory]
    [InlineData("HumanName", TypeCategory.Complex)]
    [InlineData("Address", TypeCategory.Complex)]
    [InlineData("CodeableConcept", TypeCategory.Complex)]
    [InlineData("Coding", TypeCategory.Complex)]
    [InlineData("Identifier", TypeCategory.Complex)]
    [InlineData("ContactPoint", TypeCategory.Complex)]
    [InlineData("Quantity", TypeCategory.Complex)]
    [InlineData("Period", TypeCategory.Complex)]
    [InlineData("Reference", TypeCategory.Complex)]
    public void GivenComplexType_WhenResolving_ThenReturnsComplexCategory(string typeName, TypeCategory expected)
    {
        // Arrange
        var validator = new BasicTypeValidator();

        // Act
        var typeInfo = validator.ResolveType(typeName);

        // Assert
        typeInfo.Should().NotBeNull();
        typeInfo!.Category.Should().Be(expected);
        typeInfo.IsComplex.Should().BeTrue();
    }

    [Theory]
    [InlineData("Patient", TypeCategory.Resource)]
    [InlineData("Observation", TypeCategory.Resource)]
    [InlineData("Practitioner", TypeCategory.Resource)]
    [InlineData("Organization", TypeCategory.Resource)]
    [InlineData("Encounter", TypeCategory.Resource)]
    [InlineData("Bundle", TypeCategory.Resource)]
    [InlineData("StructureDefinition", TypeCategory.Resource)]
    [InlineData("StructureMap", TypeCategory.Resource)]
    public void GivenResourceType_WhenResolving_ThenReturnsResourceCategory(string typeName, TypeCategory expected)
    {
        // Arrange
        var validator = new BasicTypeValidator();

        // Act
        var typeInfo = validator.ResolveType(typeName);

        // Assert
        typeInfo.Should().NotBeNull();
        typeInfo!.Category.Should().Be(expected);
        typeInfo.IsResource.Should().BeTrue();
    }

    [Fact]
    public void GivenUnknownType_WhenResolving_ThenReturnsUnknownCategory()
    {
        // Arrange
        var validator = new BasicTypeValidator();

        // Act
        var typeInfo = validator.ResolveType("CustomProfile");

        // Assert
        typeInfo.Should().NotBeNull();
        typeInfo!.Category.Should().Be(TypeCategory.Unknown);
    }

    [Fact]
    public void GivenNullOrEmptyType_WhenResolving_ThenReturnsNull()
    {
        // Arrange
        var validator = new BasicTypeValidator();

        // Act & Assert
        validator.ResolveType(null!).Should().BeNull();
        validator.ResolveType("").Should().BeNull();
        validator.ResolveType("   ").Should().BeNull();
    }

    #endregion

    #region Type Compatibility Tests

    [Theory]
    [InlineData("string", "string", true)]
    [InlineData("integer", "integer", true)]
    [InlineData("Patient", "Patient", true)]
    public void GivenExactTypeMatch_WhenCheckingCompatibility_ThenReturnsTrue(
        string sourceType, string targetType, bool expected)
    {
        // Arrange
        var validator = new BasicTypeValidator();

        // Act
        var isCompatible = validator.IsTypeCompatible(sourceType, targetType);

        // Assert
        isCompatible.Should().Be(expected);
    }

    [Theory]
    [InlineData("integer", "decimal", true)] // Integer can convert to decimal
    [InlineData("string", "code", true)] // String can convert to code
    [InlineData("string", "id", true)] // String can convert to id
    [InlineData("string", "uri", true)] // String can convert to uri
    [InlineData("string", "url", true)] // String can convert to url
    [InlineData("code", "string", true)] // Code can convert to string
    [InlineData("id", "string", true)] // ID can convert to string
    [InlineData("uri", "string", true)] // URI can convert to string
    [InlineData("url", "string", true)] // URL can convert to string
    public void GivenCompatiblePrimitiveTypes_WhenCheckingCompatibility_ThenReturnsTrue(
        string sourceType, string targetType, bool expected)
    {
        // Arrange
        var validator = new BasicTypeValidator();

        // Act
        var isCompatible = validator.IsTypeCompatible(sourceType, targetType);

        // Assert
        isCompatible.Should().Be(expected);
    }

    [Theory]
    [InlineData("Patient", "Resource", true)] // Resource type compatible with base Resource
    [InlineData("Observation", "Resource", true)]
    [InlineData("Bundle", "Resource", true)]
    public void GivenResourceAndBaseType_WhenCheckingCompatibility_ThenReturnsTrue(
        string sourceType, string targetType, bool expected)
    {
        // Arrange
        var validator = new BasicTypeValidator();

        // Act
        var isCompatible = validator.IsTypeCompatible(sourceType, targetType);

        // Assert
        isCompatible.Should().Be(expected);
    }

    [Theory]
    [InlineData("HumanName", "HumanName", true)] // Same complex type
    [InlineData("Address", "Address", true)]
    [InlineData("CodeableConcept", "CodeableConcept", true)]
    public void GivenSameComplexType_WhenCheckingCompatibility_ThenReturnsTrue(
        string sourceType, string targetType, bool expected)
    {
        // Arrange
        var validator = new BasicTypeValidator();

        // Act
        var isCompatible = validator.IsTypeCompatible(sourceType, targetType);

        // Assert
        isCompatible.Should().Be(expected);
    }

    #endregion

    #region Element Validation Tests

    [Fact]
    public void GivenElementWithMatchingType_WhenValidating_ThenReturnsNull()
    {
        // Arrange
        var validator = new BasicTypeValidator();
        var element = new TestTypedElement("name", "John", "string");

        // Act
        var error = validator.ValidateElement(element, "string");

        // Assert
        error.Should().BeNull();
    }

    [Fact]
    public void GivenElementWithCompatibleType_WhenValidating_ThenReturnsNull()
    {
        // Arrange
        var validator = new BasicTypeValidator();
        var element = new TestTypedElement("code", "active", "code");

        // Act
        var error = validator.ValidateElement(element, "string");

        // Assert
        error.Should().BeNull();
    }

    [Fact]
    public void GivenNullElement_WhenValidating_ThenReturnsError()
    {
        // Arrange
        var validator = new BasicTypeValidator();

        // Act
        var error = validator.ValidateElement(null!, "string");

        // Assert
        error.Should().NotBeNull();
        error!.Message.Should().Contain("null");
        error.Message.Should().Contain("string");
    }

    [Fact]
    public void GivenElementWithNoType_WhenValidating_ThenReturnsError()
    {
        // Arrange
        var validator = new BasicTypeValidator();
        var element = new TestTypedElement("value", "test", "");

        // Act
        var error = validator.ValidateElement(element, "string");

        // Assert
        error.Should().NotBeNull();
        error!.Message.Should().Contain("no type information");
    }

    #endregion

    #region Map Validation Tests

    [Fact]
    public void GivenValidMapping_WhenValidating_ThenReturnsNoErrors()
    {
        // Arrange
        var validator = new BasicTypeValidator();
        var compiler = new MappingCompiler(typeValidator: validator);
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'
uses 'http://hl7.org/fhir/StructureDefinition/Patient' alias Patient as source
uses 'http://hl7.org/fhir/StructureDefinition/Bundle' alias Bundle as target

group PatientToBundle(source src : Patient, target bundle : Bundle) {
  src.id -> bundle.id;
}";

        // Act
        var map = compiler.Parse(mappingText);
        var errors = validator.ValidateMap(map).ToList();

        // Assert
        errors.Should().BeEmpty();
    }

    [Fact]
    public void GivenMappingWithUnknownParameterType_WhenValidating_ThenReturnsError()
    {
        // Arrange
        var validator = new BasicTypeValidator();
        var compiler = new MappingCompiler(typeValidator: validator);
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Transform(source src : UnknownType, target tgt : Bundle) {
  src.id -> tgt.id;
}";

        // Act
        var map = compiler.Parse(mappingText);
        var errors = validator.ValidateMap(map).ToList();

        // Assert
        errors.Should().NotBeEmpty();
        errors.Should().Contain(e => e.Message.Contains("unknown type"));
        errors.Should().Contain(e => e.Message.Contains("UnknownType"));
    }

    [Fact]
    public void GivenMappingWithCreatePrimitiveType_WhenValidating_ThenReturnsError()
    {
        // Arrange
        var validator = new BasicTypeValidator();
        var compiler = new MappingCompiler(typeValidator: validator);
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Transform(source src : Patient, target tgt : Bundle) {
  src -> tgt.entry = create('string');
}";

        // Act
        var map = compiler.Parse(mappingText);
        var errors = validator.ValidateMap(map).ToList();

        // Assert
        errors.Should().NotBeEmpty();
        errors.Should().Contain(e => e.Message.Contains("cannot create primitive type"));
    }

    [Fact]
    public void GivenMappingWithCreateUnknownType_WhenValidating_ThenReturnsError()
    {
        // Arrange
        var validator = new BasicTypeValidator();
        var compiler = new MappingCompiler(typeValidator: validator);
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Transform(source src : Patient, target tgt : Bundle) {
  src -> tgt.entry = create('UnknownResource');
}";

        // Act
        var map = compiler.Parse(mappingText);
        var errors = validator.ValidateMap(map).ToList();

        // Assert
        errors.Should().NotBeEmpty();
        errors.Should().Contain(e => e.Message.Contains("unknown type"));
        errors.Should().Contain(e => e.Message.Contains("UnknownResource"));
    }

    [Fact]
    public void GivenMappingWithValidCreateType_WhenValidating_ThenReturnsNoErrors()
    {
        // Arrange
        var validator = new BasicTypeValidator();
        var compiler = new MappingCompiler(typeValidator: validator);
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Transform(source src : Patient, target tgt : Bundle) {
  src -> tgt.entry = create('Patient');
}";

        // Act
        var map = compiler.Parse(mappingText);
        var errors = validator.ValidateMap(map).ToList();

        // Assert
        errors.Should().BeEmpty();
    }

    #endregion

    #region Compiler Integration Tests

    [Fact]
    public void GivenCompilerWithValidator_WhenCompilingWithValidation_ThenValidatesTypes()
    {
        // Arrange
        var validator = new BasicTypeValidator();
        var compiler = new MappingCompiler(typeValidator: validator);
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Transform(source src : UnknownType, target tgt : Bundle) {
  src.id -> tgt.id;
}";

        // Act
        var act = () => compiler.Compile(mappingText, validateTypes: true);

        // Assert
        act.Should().Throw<TypeValidationException>()
            .WithMessage("*Type validation failed*")
            .Which.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public void GivenCompilerWithoutValidator_WhenCompilingWithValidation_ThenDoesNotThrow()
    {
        // Arrange
        var compiler = new MappingCompiler(); // No validator
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Transform(source src : UnknownType, target tgt : Bundle) {
  src.id -> tgt.id;
}";

        // Act
        var act = () => compiler.Compile(mappingText, validateTypes: true);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void GivenCompilerWithValidator_WhenCompilingWithoutValidation_ThenDoesNotValidate()
    {
        // Arrange
        var validator = new BasicTypeValidator();
        var compiler = new MappingCompiler(typeValidator: validator);
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Transform(source src : UnknownType, target tgt : Bundle) {
  src.id -> tgt.id;
}";

        // Act
        var act = () => compiler.Compile(mappingText, validateTypes: false);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void GivenTypeValidationException_WhenFormatting_ThenIncludesAllErrors()
    {
        // Arrange
        var errors = new List<TypeValidationError>
        {
            new TypeValidationError("Error 1"),
            new TypeValidationError("Error 2"),
            new TypeValidationError("Error 3")
        };

        // Act
        var exception = new TypeValidationException("Multiple errors", errors);

        // Assert
        exception.Message.Should().Contain("Error 1");
        exception.Message.Should().Contain("Error 2");
        exception.Message.Should().Contain("Error 3");
        exception.Errors.Should().HaveCount(3);
    }

    #endregion

    #region Type Casing Tests

    [Theory]
    [InlineData("String", "string")]
    [InlineData("STRING", "string")]
    [InlineData("Patient", "patient")]
    [InlineData("PATIENT", "patient")]
    public void GivenTypeWithDifferentCasing_WhenResolving_ThenIsCaseInsensitive(string input, string canonical)
    {
        // Arrange
        var validator = new BasicTypeValidator();

        // Act
        var typeInfo1 = validator.ResolveType(input);
        var typeInfo2 = validator.ResolveType(canonical);

        // Assert
        typeInfo1.Should().NotBeNull();
        typeInfo2.Should().NotBeNull();
        typeInfo1!.Category.Should().Be(typeInfo2!.Category);
    }

    [Theory]
    [InlineData("String", "STRING", true)]
    [InlineData("integer", "INTEGER", true)]
    [InlineData("Patient", "PATIENT", true)]
    public void GivenTypesWithDifferentCasing_WhenCheckingCompatibility_ThenIsCaseInsensitive(
        string sourceType, string targetType, bool expected)
    {
        // Arrange
        var validator = new BasicTypeValidator();

        // Act
        var isCompatible = validator.IsTypeCompatible(sourceType, targetType);

        // Assert
        isCompatible.Should().Be(expected);
    }

    #endregion
}
