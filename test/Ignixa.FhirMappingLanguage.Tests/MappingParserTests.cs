/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Unit tests for FHIR Mapping Language parser.
 */

using Shouldly;
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
        result.ShouldNotBeNull();
        result.Url.ShouldBe("http://example.org/fhir/StructureMap/Example");
        result.Identifier.ShouldBe("ExampleMap");
        result.Groups.Count.ShouldBe(1);
        result.Groups[0].Name.ShouldBe("PatientToBundle");
        result.Groups[0].Parameters.Count.ShouldBe(2);
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
        result.Uses.Count.ShouldBe(2);
        result.Uses[0].Url.ShouldBe("http://hl7.org/fhir/StructureDefinition/Patient");
        result.Uses[0].Alias.ShouldBe("Patient");
        result.Uses[0].Mode.ShouldBe(ModelMode.Source);
        result.Uses[1].Url.ShouldBe("http://hl7.org/fhir/StructureDefinition/Bundle");
        result.Uses[1].Alias.ShouldBe("Bundle");
        result.Uses[1].Mode.ShouldBe(ModelMode.Target);
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
        result.Imports.Count.ShouldBe(1);
        result.Imports[0].Url.ShouldBe("http://example.org/fhir/StructureMap/Helpers");
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
        result.Groups[0].Rules.Count.ShouldBe(1);
        var rule = result.Groups[0].Rules[0];
        rule.Sources.Count.ShouldBe(1);
        rule.Sources[0].Variable.ShouldBe("vn");
        rule.Targets.Count.ShouldBe(1);
        rule.Targets[0].Variable.ShouldBe("entry");
    }

    [Fact]
    public void GivenInvalidMapping_WhenParsing_ThenThrowsParseException()
    {
        // Arrange
        var mappingText = "invalid mapping text";
        var compiler = new MappingParser();

        // Act & Assert
        var act = () => compiler.Parse(mappingText);
        Should.Throw<ParseException>(act);
    }
}
