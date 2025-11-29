/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Unit tests for FHIR Mapping Language parser.
 */

using FluentAssertions;
using Ignixa.FhirMappingLanguage.Expressions;
using Ignixa.FhirMappingLanguage.Parser;
using Xunit;

namespace Ignixa.FhirMappingLanguage.Tests;

public class MappingParserTests
{
    [Fact]
    public void GivenSimpleMap_WhenParsing_ThenReturnsMapExpression()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Example' = 'ExampleMap'

group PatientToBundle(source src : Patient, target bundle : Bundle) {
}
";
        var compiler = new MappingParser();

        // Act
        var result = compiler.Parse(mappingText);

        // Assert
        result.Should().NotBeNull();
        result.Url.Should().Be("http://example.org/fhir/StructureMap/Example");
        result.Identifier.Should().Be("ExampleMap");
        result.Groups.Should().HaveCount(1);
        result.Groups[0].Name.Should().Be("PatientToBundle");
        result.Groups[0].Parameters.Should().HaveCount(2);
    }

    [Fact]
    public void GivenMapWithUses_WhenParsing_ThenReturnsUsesExpressions()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Example' = 'ExampleMap'

uses 'http://hl7.org/fhir/StructureDefinition/Patient' alias Patient as source
uses 'http://hl7.org/fhir/StructureDefinition/Bundle' alias Bundle as target

group PatientToBundle(source src : Patient, target bundle : Bundle) {
}
";
        var compiler = new MappingParser();

        // Act
        var result = compiler.Parse(mappingText);

        // Assert
        result.Uses.Should().HaveCount(2);
        result.Uses[0].Url.Should().Be("http://hl7.org/fhir/StructureDefinition/Patient");
        result.Uses[0].Alias.Should().Be("Patient");
        result.Uses[0].Mode.Should().Be(ModelMode.Source);
        result.Uses[1].Url.Should().Be("http://hl7.org/fhir/StructureDefinition/Bundle");
        result.Uses[1].Alias.Should().Be("Bundle");
        result.Uses[1].Mode.Should().Be(ModelMode.Target);
    }

    [Fact]
    public void GivenMapWithImports_WhenParsing_ThenReturnsImportsExpressions()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Example' = 'ExampleMap'

imports 'http://example.org/fhir/StructureMap/Helpers'

group PatientToBundle(source src : Patient, target bundle : Bundle) {
}
";
        var compiler = new MappingParser();

        // Act
        var result = compiler.Parse(mappingText);

        // Assert
        result.Imports.Should().HaveCount(1);
        result.Imports[0].Url.Should().Be("http://example.org/fhir/StructureMap/Helpers");
    }

    [Fact]
    public void GivenMapWithRule_WhenParsing_ThenReturnsRuleExpression()
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Example' = 'ExampleMap'

group PatientToBundle(source src : Patient, target bundle : Bundle) {
  src.name as vn -> bundle.entry as entry;
}
";
        var compiler = new MappingParser();

        // Act
        var result = compiler.Parse(mappingText);

        // Assert
        result.Groups[0].Rules.Should().HaveCount(1);
        var rule = result.Groups[0].Rules[0];
        rule.Sources.Should().HaveCount(1);
        rule.Sources[0].Variable.Should().Be("vn");
        rule.Targets.Should().HaveCount(1);
        rule.Targets[0].Variable.Should().Be("entry");
    }

    [Fact]
    public void GivenInvalidMapping_WhenParsing_ThenThrowsParseException()
    {
        // Arrange
        var mappingText = "invalid mapping text";
        var compiler = new MappingParser();

        // Act & Assert
        var act = () => compiler.Parse(mappingText);
        act.Should().Throw<ParseException>();
    }
}
