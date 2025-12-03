/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Tests for version-aware StructureMapBuilder.
 * Ensures builder correctly sets FhirVersion and uses version-appropriate serialization.
 */

using FluentAssertions;
using Ignixa.Abstractions;
using Ignixa.FhirMappingLanguage;
using Ignixa.FhirMappingLanguage.Expressions;
using Ignixa.FhirMappingLanguage.Parser;
using Ignixa.FhirMappingLanguage.Serialization;
using Ignixa.Serialization;
using Ignixa.Serialization.Extensions;
using Xunit;

namespace Ignixa.FhirMappingLanguage.Tests.Serialization;

/// <summary>
/// Tests that verify StructureMapBuilder correctly handles FHIR version-specific features.
/// </summary>
public class StructureMapBuilderVersionTests
{
    private readonly MappingParser _parser = new();

    [Fact]
    public void GivenR5Builder_WhenBuildingMap_ThenSetsFhirVersionR5()
    {
        // Arrange
        var fml = """
            map 'http://example.org/test' = 'TestMap'

            group Main(source src : Patient, target tgt : Bundle) {
            }
            """;
        var ast = _parser.Parse(fml);
        var builder = new StructureMapBuilder(FhirVersion.R5);

        // Act
        var structureMap = builder.Build(ast);

        // Assert
        structureMap.FhirVersion.Should().Be(FhirVersion.R5);
    }

    [Fact]
    public void GivenR4Builder_WhenBuildingMap_ThenSetsFhirVersionR4()
    {
        // Arrange
        var fml = """
            map 'http://example.org/test' = 'TestMap'

            group Main(source src : Patient, target tgt : Bundle) {
            }
            """;
        var ast = _parser.Parse(fml);
        var builder = new StructureMapBuilder(FhirVersion.R4);

        // Act
        var structureMap = builder.Build(ast);

        // Assert
        structureMap.FhirVersion.Should().Be(FhirVersion.R4);
    }

    [Fact]
    public void GivenDefaultBuilder_WhenBuildingMap_ThenDefaultsToR5()
    {
        // Arrange
        var fml = """
            map 'http://example.org/test' = 'TestMap'

            group Main(source src : Patient, target tgt : Bundle) {
            }
            """;
        var ast = _parser.Parse(fml);
        var builder = new StructureMapBuilder(); // No version specified

        // Act
        var structureMap = builder.Build(ast);

        // Assert
        structureMap.FhirVersion.Should().Be(FhirVersion.R5);
    }

    [Fact]
    public void GivenR5Builder_WhenBuildingMapWithDependentGroupCall_ThenUsesParameterProperty()
    {
        // Arrange
        var fml = """
            map 'http://example.org/test' = 'TestMap'

            group Main(source src : Patient, target tgt : Bundle) {
                src -> tgt then Helper(src);
            }

            group Helper(source src : Patient) {
            }
            """;
        var ast = _parser.Parse(fml);
        var builder = new StructureMapBuilder(FhirVersion.R5);

        // Act
        var structureMap = builder.Build(ast);

        // Assert
        var rule = structureMap.Group[0].Rule[0];
        rule.Dependent.Should().HaveCount(1);
        var dependent = rule.Dependent[0];
        dependent.Name.Should().Be("Helper");

        // R5 should use Parameter property (not Variable)
        dependent.Parameter.Should().HaveCount(1);
        dependent.Parameter[0].GetValueAs<string>().Should().Be("src");
    }

    [Fact]
    public void GivenR4Builder_WhenBuildingMapWithDependentGroupCall_ThenUsesVariableProperty()
    {
        // Arrange
        var fml = """
            map 'http://example.org/test' = 'TestMap'

            group Main(source src : Patient, target tgt : Bundle) {
                src -> tgt then Helper(src);
            }

            group Helper(source src : Patient) {
            }
            """;
        var ast = _parser.Parse(fml);
        var builder = new StructureMapBuilder(FhirVersion.R4);

        // Act
        var structureMap = builder.Build(ast);

        // Assert
        var rule = structureMap.Group[0].Rule[0];
        rule.Dependent.Should().HaveCount(1);
        var dependent = rule.Dependent[0];
        dependent.Name.Should().Be("Helper");

        // R4 should use Variable property (not Parameter)
        dependent.Variable.Should().HaveCount(1);
        dependent.Variable.Should().Contain("src");
    }

    [Fact]
    public void GivenR5Builder_WhenBuildingMapWithDefaultValue_ThenUsesStringDefaultValue()
    {
        // Arrange
        var fml = """
            map 'http://example.org/test' = 'TestMap'

            group Main(source src : Patient, target tgt : Bundle) {
                src.name default 'Unknown' -> tgt.entry;
            }
            """;
        var ast = _parser.Parse(fml);
        var builder = new StructureMapBuilder(FhirVersion.R5);

        // Act
        var structureMap = builder.Build(ast);

        // Assert
        var source = structureMap.Group[0].Rule[0].Source[0];

        // R5 should use DefaultValue property
        source.DefaultValue.Should().Be("'Unknown'");
    }

    [Fact]
    public void GivenR4Builder_WhenBuildingMapWithDefaultValue_ThenUsesDefaultValueString()
    {
        // Arrange
        var fml = """
            map 'http://example.org/test' = 'TestMap'

            group Main(source src : Patient, target tgt : Bundle) {
                src.name default 'Unknown' -> tgt.entry;
            }
            """;
        var ast = _parser.Parse(fml);
        var builder = new StructureMapBuilder(FhirVersion.R4);

        // Act
        var structureMap = builder.Build(ast);

        // Assert
        var source = structureMap.Group[0].Rule[0].Source[0];

        // R4 should use defaultValueString in underlying JSON
        source.MutableNode.ContainsKey("defaultValueString").Should().BeTrue();
        source.MutableNode["defaultValueString"]!.GetValue<string>().Should().Be("'Unknown'");
    }

    [Fact]
    public void GivenR5Builder_WhenBuildingMap_ThenCanAccessR5Properties()
    {
        // Arrange
        var fml = """
            map 'http://example.org/test' = 'TestMap'

            group Main(source src : Patient, target tgt : Bundle) {
            }
            """;
        var ast = _parser.Parse(fml);
        var builder = new StructureMapBuilder(FhirVersion.R5);

        // Act
        var structureMap = builder.Build(ast);

        // Assert - These should NOT throw
        structureMap.VersionAlgorithmString = "semver";
        structureMap.CopyrightLabel = "© 2025";
        structureMap.VersionAlgorithmString.Should().Be("semver");
        structureMap.CopyrightLabel.Should().Be("© 2025");
    }

    [Fact]
    public void GivenR4Builder_WhenBuildingMap_ThenCannotAccessR5Properties()
    {
        // Arrange
        var fml = """
            map 'http://example.org/test' = 'TestMap'

            group Main(source src : Patient, target tgt : Bundle) {
            }
            """;
        var ast = _parser.Parse(fml);
        var builder = new StructureMapBuilder(FhirVersion.R4);

        // Act
        var structureMap = builder.Build(ast);

        // Assert - These SHOULD throw
        Assert.Throws<NotSupportedException>(() => structureMap.VersionAlgorithmString = "semver");
        Assert.Throws<NotSupportedException>(() => _ = structureMap.CopyrightLabel);
    }

    [Fact]
    public void GivenR5Builder_WhenBuildingMap_ThenGroupTypeModeCanBeOptional()
    {
        // Arrange
        var fml = """
            map 'http://example.org/test' = 'TestMap'

            group Main(source src : Patient, target tgt : Bundle) {
            }
            """;
        var ast = _parser.Parse(fml);
        var builder = new StructureMapBuilder(FhirVersion.R5);

        // Act
        var structureMap = builder.Build(ast);
        var group = structureMap.Group[0];

        // Assert - TypeMode can be set to null in R5
        group.TypeMode = null;
        group.TypeMode.Should().BeNull();
    }

    [Fact]
    public void GivenR4Builder_WhenBuildingMap_ThenGroupTypeModeIsRequired()
    {
        // Arrange
        var fml = """
            map 'http://example.org/test' = 'TestMap'

            group Main(source src : Patient, target tgt : Bundle) {
            }
            """;
        var ast = _parser.Parse(fml);
        var builder = new StructureMapBuilder(FhirVersion.R4);

        // Act
        var structureMap = builder.Build(ast);
        var group = structureMap.Group[0];

        // Assert - TypeMode cannot be set to null in R4
        Assert.Throws<ArgumentNullException>(() => group.TypeMode = null);
    }

    [Fact]
    public void GivenR5Builder_WhenUsingExtensionMethods_ThenSupportsConstantsIsTrue()
    {
        // Arrange
        var fml = """
            map 'http://example.org/test' = 'TestMap'

            group Main(source src : Patient, target tgt : Bundle) {
            }
            """;
        var ast = _parser.Parse(fml);
        var builder = new StructureMapBuilder(FhirVersion.R5);

        // Act
        var structureMap = builder.Build(ast);

        // Assert
        structureMap.SupportsConstants().Should().BeTrue();
    }

    [Fact]
    public void GivenR4Builder_WhenUsingExtensionMethods_ThenSupportsConstantsIsFalse()
    {
        // Arrange
        var fml = """
            map 'http://example.org/test' = 'TestMap'

            group Main(source src : Patient, target tgt : Bundle) {
            }
            """;
        var ast = _parser.Parse(fml);
        var builder = new StructureMapBuilder(FhirVersion.R4);

        // Act
        var structureMap = builder.Build(ast);

        // Assert
        structureMap.SupportsConstants().Should().BeFalse();
    }
}
