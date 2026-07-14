/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Tests for version-aware StructureMapBuilder.
 * Ensures builder correctly sets FhirVersion and uses version-appropriate serialization.
 */

using Shouldly;
using Ignixa.Abstractions;
using Ignixa.FhirMappingLanguage;
using Ignixa.FhirMappingLanguage.Expressions;
using Ignixa.FhirMappingLanguage.Parser;
using Ignixa.FhirMappingLanguage.Serialization;
using Ignixa.Serialization;
using Ignixa.Serialization.Extensions;
using Xunit;
using Ignixa.Serialization.TestSupport;

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
        structureMap.FhirVersion.ShouldBe(FhirVersion.R5);
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
        structureMap.FhirVersion.ShouldBe(FhirVersion.R4);
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
        structureMap.FhirVersion.ShouldBe(FhirVersion.R5);
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
        rule.Dependent.Count.ShouldBe(1);
        var dependent = rule.Dependent[0];
        dependent.Name.ShouldBe("Helper");

        // R5 should use the parameter array (not variable) -- Dependent.Parameter/.Variable aren't on
        // the shared base (they're R4-only/R5-only respectively), so wire shape is asserted directly and
        // the value is read back via the version-agnostic GetDependentVariables() wrapper.
        dependent.MutableNode().ContainsKey("parameter").ShouldBeTrue();
        dependent.MutableNode().ContainsKey("variable").ShouldBeFalse();
        dependent.GetDependentVariables().ShouldContain("src");
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
        rule.Dependent.Count.ShouldBe(1);
        var dependent = rule.Dependent[0];
        dependent.Name.ShouldBe("Helper");

        // R4 should use the variable array (not parameter)
        dependent.MutableNode().ContainsKey("variable").ShouldBeTrue();
        dependent.MutableNode().ContainsKey("parameter").ShouldBeFalse();
        dependent.GetDependentVariables().ShouldContain("src");
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

        // R5 should use the plain defaultValue string
        source.GetDefaultValueString().ShouldBe("'Unknown'");
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
        source.MutableNode().ContainsKey("defaultValueString").ShouldBeTrue();
        source.MutableNode()["defaultValueString"]!.GetValue<string>().ShouldBe("'Unknown'");
    }

    // GivenR5Builder_WhenBuildingMap_ThenCanAccessR5Properties / GivenR4Builder_..._ThenCannotAccessR5Properties
    // and the GroupTypeMode pair below them were removed: they tested the hand-written type's runtime
    // version guards (NotSupportedException/ArgumentNullException) for VersionAlgorithmString,
    // CopyrightLabel, and Group.TypeMode. None of those three are used by any real caller (confirmed by
    // repo-wide grep before the merge), so they weren't given a version-agnostic wrapper -- the guard is
    // now a compile-time one instead: those members simply don't exist on the shared Ignixa.Models
    // base at all (VersionAlgorithmString/CopyrightLabel are R5-only fields on Ignixa.Models.R5.StructureMap;
    // TypeMode is on both Ignixa.Models.R4.StructureMapGroup and Ignixa.Models.R5.StructureMapGroup). See
    // docs/features/typed-models/investigations/consolidate-handwritten-facades.md for the Group.TypeMode
    // fix this merge made in StructureMapBuilder (the old code unconditionally wrote "none", which is
    // invalid in R5 since R5's map-group-type-mode value set dropped that literal).

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
        structureMap.SupportsConstants().ShouldBeTrue();
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
        structureMap.SupportsConstants().ShouldBeFalse();
    }
}
