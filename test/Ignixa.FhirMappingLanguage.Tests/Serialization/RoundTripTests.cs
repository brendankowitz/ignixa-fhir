/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Round-trip serialization tests for FHIR Mapping Language.
 * Verifies lossless conversion between FML text, AST, and StructureMap resources.
 */

using FluentAssertions;
using Ignixa.FhirMappingLanguage;
using Ignixa.FhirMappingLanguage.Expressions;
using Ignixa.FhirMappingLanguage.Serialization;
using System.Text.Json.Nodes;
using Ignixa.FhirMappingLanguage.Parser;
using Xunit;

namespace Ignixa.FhirMappingLanguage.Tests.Serialization;

/// <summary>
/// Tests that verify round-trip conversion preserves semantic meaning across:
/// - FML text → AST → FML text
/// - StructureMap → AST → StructureMap
/// - FML → AST → StructureMap → AST → FML
/// </summary>
public class RoundTripTests
{
    private readonly MappingParser _parser = new();
    private readonly FmlSerializer _serializer = new();
    private readonly StructureMapBuilder _builder = new();
    private readonly StructureMapParser _structureMapParser = new();

    #region FML → AST → FML Round-Trip

    [Fact]
    public void GivenSimpleMap_WhenRoundTripping_ThenSemanticsPreserved()
    {
        // Arrange
        var originalFml = """
            map 'http://example.org/test' = 'TestMap'

            group Main(source src : Patient, target tgt : Bundle) {
            }
            """;

        // Act
        var ast = _parser.Parse(originalFml);
        var serializedFml = _serializer.Serialize(ast);
        var roundTrippedAst = _parser.Parse(serializedFml);

        // Assert
        AssertAstEquivalent(ast, roundTrippedAst);
        roundTrippedAst.Url.Should().Be("http://example.org/test");
        roundTrippedAst.Identifier.Should().Be("TestMap");
        roundTrippedAst.Groups.Should().HaveCount(1);
        roundTrippedAst.Groups[0].Name.Should().Be("Main");
    }

    [Fact]
    public void GivenMapWithUses_WhenRoundTripping_ThenModesPreserved()
    {
        // Arrange
        var originalFml = """
            map 'http://example.org/test' = 'TestMap'
            uses 'http://hl7.org/fhir/StructureDefinition/Patient' alias Patient as source
            uses 'http://hl7.org/fhir/StructureDefinition/Bundle' alias Bundle as target

            group Main(source src : Patient, target tgt : Bundle) {
            }
            """;

        // Act
        var ast = _parser.Parse(originalFml);
        var serializedFml = _serializer.Serialize(ast);
        var roundTrippedAst = _parser.Parse(serializedFml);

        // Assert
        AssertAstEquivalent(ast, roundTrippedAst);
        roundTrippedAst.Uses.Should().HaveCount(2);
        roundTrippedAst.Uses[0].Url.Should().Be("http://hl7.org/fhir/StructureDefinition/Patient");
        roundTrippedAst.Uses[0].Alias.Should().Be("Patient");
        roundTrippedAst.Uses[0].Mode.Should().Be(ModelMode.Source);
        roundTrippedAst.Uses[1].Url.Should().Be("http://hl7.org/fhir/StructureDefinition/Bundle");
        roundTrippedAst.Uses[1].Alias.Should().Be("Bundle");
        roundTrippedAst.Uses[1].Mode.Should().Be(ModelMode.Target);
    }

    [Fact]
    public void GivenMapWithImports_WhenRoundTripping_ThenUrlsPreserved()
    {
        // Arrange
        var originalFml = """
            map 'http://example.org/test' = 'TestMap'
            imports 'http://example.org/helper'
            imports 'http://example.org/common'

            group Main(source src : Patient, target tgt : Bundle) {
            }
            """;

        // Act
        var ast = _parser.Parse(originalFml);
        var serializedFml = _serializer.Serialize(ast);
        var roundTrippedAst = _parser.Parse(serializedFml);

        // Assert
        AssertAstEquivalent(ast, roundTrippedAst);
        roundTrippedAst.Imports.Should().HaveCount(2);
        roundTrippedAst.Imports[0].Url.Should().Be("http://example.org/helper");
        roundTrippedAst.Imports[1].Url.Should().Be("http://example.org/common");
    }

    [Fact]
    public void GivenGroupWithParameters_WhenRoundTripping_ThenTypesPreserved()
    {
        // Arrange
        var originalFml = """
            map 'http://example.org/test' = 'TestMap'

            group Main(source src : Patient, target tgt : Bundle, source context) {
            }
            """;

        // Act
        var ast = _parser.Parse(originalFml);
        var serializedFml = _serializer.Serialize(ast);
        var roundTrippedAst = _parser.Parse(serializedFml);

        // Assert
        AssertAstEquivalent(ast, roundTrippedAst);
        var group = roundTrippedAst.Groups[0];
        group.Parameters.Should().HaveCount(3);
        group.Parameters[0].Mode.Should().Be(ParameterMode.Source);
        group.Parameters[0].Name.Should().Be("src");
        group.Parameters[0].Type.Should().Be("Patient");
        group.Parameters[1].Mode.Should().Be(ParameterMode.Target);
        group.Parameters[1].Name.Should().Be("tgt");
        group.Parameters[1].Type.Should().Be("Bundle");
        group.Parameters[2].Mode.Should().Be(ParameterMode.Source);
        group.Parameters[2].Name.Should().Be("context");
        group.Parameters[2].Type.Should().BeNull();
    }

    [Fact]
    public void GivenGroupWithExtends_WhenRoundTripping_ThenBasePreserved()
    {
        // Arrange
        var originalFml = """
            map 'http://example.org/test' = 'TestMap'

            group Base(source src : Patient, target tgt : Bundle) {
            }

            group Main(source src : Patient, target tgt : Bundle) extends Base {
            }
            """;

        // Act
        var ast = _parser.Parse(originalFml);
        var serializedFml = _serializer.Serialize(ast);
        var roundTrippedAst = _parser.Parse(serializedFml);

        // Assert
        AssertAstEquivalent(ast, roundTrippedAst);
        roundTrippedAst.Groups.Should().HaveCount(2);
        roundTrippedAst.Groups[0].Name.Should().Be("Base");
        roundTrippedAst.Groups[0].Extends.Should().BeNull();
        roundTrippedAst.Groups[1].Name.Should().Be("Main");
        roundTrippedAst.Groups[1].Extends.Should().Be("Base");
    }

    [Fact]
    public void GivenRuleWithTransform_WhenRoundTripping_ThenTransformPreserved()
    {
        // Arrange
        var originalFml = """
            map 'http://example.org/test' = 'TestMap'

            group Main(source src : Patient, target tgt : Bundle) {
              src.gender -> tgt.gender = create('code');
              src.name -> tgt.name = copy(src.name);
            }
            """;

        // Act
        var ast = _parser.Parse(originalFml);
        var serializedFml = _serializer.Serialize(ast);
        var roundTrippedAst = _parser.Parse(serializedFml);

        // Assert
        AssertAstEquivalent(ast, roundTrippedAst);
        var rules = roundTrippedAst.Groups[0].Rules;
        rules.Should().HaveCount(2);

        // First rule: create transform
        rules[0].Sources.Should().HaveCount(1);
        rules[0].Targets.Should().HaveCount(1);
        rules[0].Targets[0].Transform.Should().BeOfType<TransformExpression>();
        var createTransform = rules[0].Targets[0].Transform as TransformExpression;
        createTransform!.FunctionName.Should().Be("create");
        createTransform.Arguments.Should().HaveCount(1);

        // Second rule: copy transform
        rules[1].Targets[0].Transform.Should().BeOfType<TransformExpression>();
        var copyTransform = rules[1].Targets[0].Transform as TransformExpression;
        copyTransform!.FunctionName.Should().Be("copy");
    }

    [Fact]
    public void GivenRuleWithWhereClause_WhenRoundTripping_ThenConditionPreserved()
    {
        // Arrange
        var originalFml = """
            map 'http://example.org/test' = 'TestMap'

            group Main(source src : Patient, target tgt : Bundle) {
              src.active where active.exists() -> tgt.active = copy(src.active);
            }
            """;

        // Act
        var ast = _parser.Parse(originalFml);
        var serializedFml = _serializer.Serialize(ast);
        var roundTrippedAst = _parser.Parse(serializedFml);

        // Assert
        AssertAstEquivalent(ast, roundTrippedAst);
        var rule = roundTrippedAst.Groups[0].Rules[0];
        rule.Sources.Should().HaveCount(1);
        rule.Sources[0].Condition.Should().NotBeNull();
        rule.Sources[0].Condition.Should().BeOfType<FhirPathExpression>();
    }

    [Fact]
    public void GivenRuleWithCardinality_WhenRoundTripping_ThenCardinalityPreserved()
    {
        // Arrange
        var originalFml = """
            map 'http://example.org/test' = 'TestMap'

            group Main(source src : Patient, target tgt : Bundle) {
              src.identifier 0..1 -> tgt.identifier;
              src.name 1..* -> tgt.name;
              src.contact 0..* -> tgt.contact;
            }
            """;

        // Act
        var ast = _parser.Parse(originalFml);
        var serializedFml = _serializer.Serialize(ast);
        var roundTrippedAst = _parser.Parse(serializedFml);

        // Assert
        AssertAstEquivalent(ast, roundTrippedAst);
        var rules = roundTrippedAst.Groups[0].Rules;
        rules.Should().HaveCount(3);

        rules[0].Sources[0].Cardinality.Should().NotBeNull();
        rules[0].Sources[0].Cardinality!.Min.Should().Be(0);
        rules[0].Sources[0].Cardinality.Max.Should().Be(1);

        rules[1].Sources[0].Cardinality.Should().NotBeNull();
        rules[1].Sources[0].Cardinality!.Min.Should().Be(1);
        rules[1].Sources[0].Cardinality.Max.Should().BeNull(); // * means unbounded

        rules[2].Sources[0].Cardinality.Should().NotBeNull();
        rules[2].Sources[0].Cardinality!.Min.Should().Be(0);
        rules[2].Sources[0].Cardinality.Max.Should().BeNull();
    }

    [Fact]
    public void GivenRuleWithListMode_WhenRoundTripping_ThenListModePreserved()
    {
        // Arrange
        var originalFml = """
            map 'http://example.org/test' = 'TestMap'

            group Main(source src : Patient, target tgt : Bundle) {
              src.name -> tgt.name = create('HumanName') first;
              src.identifier -> tgt.identifier share;
            }
            """;

        // Act
        var ast = _parser.Parse(originalFml);
        var serializedFml = _serializer.Serialize(ast);
        var roundTrippedAst = _parser.Parse(serializedFml);

        // Assert
        AssertAstEquivalent(ast, roundTrippedAst);
        var rules = roundTrippedAst.Groups[0].Rules;
        rules.Should().HaveCount(2);

        rules[0].Targets[0].ListMode.Should().Be(ListMode.First);
        rules[1].Targets[0].ListMode.Should().Be(ListMode.Share);
    }

    [Fact]
    public void GivenNestedRules_WhenRoundTripping_ThenNestingPreserved()
    {
        // Arrange
        var originalFml = """
            map 'http://example.org/test' = 'TestMap'

            group Main(source src : Patient, target tgt : Bundle) {
              src.name as n -> tgt.entry as e then {
                n.family -> e.family;
                n.given -> e.given;
              };
            }
            """;

        // Act
        var ast = _parser.Parse(originalFml);
        var serializedFml = _serializer.Serialize(ast);
        var roundTrippedAst = _parser.Parse(serializedFml);

        // Assert
        AssertAstEquivalent(ast, roundTrippedAst);
        var rule = roundTrippedAst.Groups[0].Rules[0];
        rule.Dependent.Should().BeOfType<RuleSetExpression>();
        var ruleSet = rule.Dependent as RuleSetExpression;
        ruleSet!.Rules.Should().HaveCount(2);
    }

    [Fact]
    public void GivenGroupInvocation_WhenRoundTripping_ThenInvocationPreserved()
    {
        // Arrange
        var originalFml = """
            map 'http://example.org/test' = 'TestMap'

            group ProcessName(source src, target tgt) {
              src.family -> tgt.family;
            }

            group Main(source src : Patient, target tgt : Bundle) {
              src.name as n -> tgt.entry as e then ProcessName(n, e);
            }
            """;

        // Act
        var ast = _parser.Parse(originalFml);
        var serializedFml = _serializer.Serialize(ast);
        var roundTrippedAst = _parser.Parse(serializedFml);

        // Assert
        AssertAstEquivalent(ast, roundTrippedAst);
        var rule = roundTrippedAst.Groups[1].Rules[0];
        rule.Dependent.Should().BeOfType<GroupInvocationExpression>();
        var invocation = rule.Dependent as GroupInvocationExpression;
        invocation!.GroupName.Should().Be("ProcessName");
        invocation.Arguments.Should().HaveCount(2);
    }

    [Fact]
    public void GivenComplexMap_WhenRoundTripping_ThenAllFeaturesPreserved()
    {
        // Arrange - comprehensive example with all features
        var originalFml = """
            map 'http://example.org/test' = 'TestMap'
            uses 'http://hl7.org/fhir/StructureDefinition/Patient' alias Patient as source
            uses 'http://hl7.org/fhir/StructureDefinition/Bundle' alias Bundle as target
            imports 'http://example.org/helper'

            group ProcessName(source src, target tgt) {
              src.family -> tgt.family;
              src.given -> tgt.given;
            }

            group Base(source src : Patient, target tgt : Bundle) {
              src.id -> tgt.id;
            }

            group Main(source src : Patient, target tgt : Bundle) extends Base {
              src.name as n -> tgt.entry as e then ProcessName(n, e);
              src.identifier 0..1 -> tgt.identifier;
              src.active where active.exists() -> tgt.active = copy(src.active);
              src.gender -> tgt.gender = create('code') first;
            }
            """;

        // Act
        var ast = _parser.Parse(originalFml);
        var serializedFml = _serializer.Serialize(ast);
        var roundTrippedAst = _parser.Parse(serializedFml);

        // Assert
        AssertAstEquivalent(ast, roundTrippedAst);

        // Verify top-level structure
        roundTrippedAst.Url.Should().Be("http://example.org/test");
        roundTrippedAst.Identifier.Should().Be("TestMap");
        roundTrippedAst.Uses.Should().HaveCount(2);
        roundTrippedAst.Imports.Should().HaveCount(1);
        roundTrippedAst.Groups.Should().HaveCount(3);

        // Verify groups
        roundTrippedAst.Groups[0].Name.Should().Be("ProcessName");
        roundTrippedAst.Groups[1].Name.Should().Be("Base");
        roundTrippedAst.Groups[2].Name.Should().Be("Main");
        roundTrippedAst.Groups[2].Extends.Should().Be("Base");
    }

    #endregion

    #region StructureMap → AST → StructureMap Round-Trip

    [Fact]
    public void GivenStructureMap_WhenRoundTripping_ThenResourcePreserved()
    {
        // Arrange - create a minimal StructureMap JSON
        var structureMap = new JsonObject
        {
            ["resourceType"] = "StructureMap",
            ["url"] = "http://example.org/test",
            ["name"] = "TestMap",
            ["status"] = "draft",
            ["group"] = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = "Main",
                    ["typeMode"] = "none",
                    ["input"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["name"] = "src",
                            ["type"] = "Patient",
                            ["mode"] = "source"
                        },
                        new JsonObject
                        {
                            ["name"] = "tgt",
                            ["type"] = "Bundle",
                            ["mode"] = "target"
                        }
                    }
                }
            }
        };

        // Act
        var ast = _structureMapParser.Parse(structureMap);
        var rebuiltStructureMap = _builder.Build(ast);
        var roundTrippedAst = _structureMapParser.Parse(rebuiltStructureMap);

        // Assert
        AssertAstEquivalent(ast, roundTrippedAst);
        rebuiltStructureMap.Url.Should().Be("http://example.org/test");
        rebuiltStructureMap.Name.Should().Be("TestMap");
    }

    [Fact]
    public void GivenStructureMapWithUses_WhenRoundTripping_ThenStructuresPreserved()
    {
        // Arrange
        var structureMap = new JsonObject
        {
            ["resourceType"] = "StructureMap",
            ["url"] = "http://example.org/test",
            ["name"] = "TestMap",
            ["status"] = "draft",
            ["structure"] = new JsonArray
            {
                new JsonObject
                {
                    ["url"] = "http://hl7.org/fhir/StructureDefinition/Patient",
                    ["alias"] = "Patient",
                    ["mode"] = "source"
                },
                new JsonObject
                {
                    ["url"] = "http://hl7.org/fhir/StructureDefinition/Bundle",
                    ["alias"] = "Bundle",
                    ["mode"] = "target"
                }
            },
            ["group"] = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = "Main",
                    ["typeMode"] = "none",
                    ["input"] = new JsonArray()
                }
            }
        };

        // Act
        var ast = _structureMapParser.Parse(structureMap);
        var rebuiltStructureMap = _builder.Build(ast);

        // Assert
        ast.Uses.Should().HaveCount(2);
        rebuiltStructureMap.Structure.Should().HaveCount(2);
    }

    #endregion

    #region Full Chain: FML → AST → StructureMap → AST → FML

    [Fact]
    public void GivenFml_WhenConvertedToStructureMapAndBack_ThenSemanticsPreserved()
    {
        // Arrange
        var originalFml = """
            map 'http://example.org/test' = 'TestMap'
            uses 'http://hl7.org/fhir/StructureDefinition/Patient' alias Patient as source
            uses 'http://hl7.org/fhir/StructureDefinition/Bundle' alias Bundle as target

            group Main(source src : Patient, target tgt : Bundle) {
              src.name as n -> tgt.entry as e;
            }
            """;

        // Act - Full pipeline: FML → AST → StructureMap → AST → FML
        var ast1 = _parser.Parse(originalFml);
        var structureMap = _builder.Build(ast1);
        var ast2 = _structureMapParser.Parse(structureMap);
        var finalFml = _serializer.Serialize(ast2);

        // Assert - semantic equivalence across full pipeline
        AssertAstEquivalent(ast1, ast2);

        var finalAst = _parser.Parse(finalFml);
        finalAst.Url.Should().Be("http://example.org/test");
        finalAst.Identifier.Should().Be("TestMap");
        finalAst.Uses.Should().HaveCount(2);
        finalAst.Groups.Should().HaveCount(1);
        finalAst.Groups[0].Rules.Should().HaveCount(1);
    }

    [Fact]
    public void GivenFmlWithComplexRules_WhenConvertedToStructureMapAndBack_ThenRulesPreserved()
    {
        // Arrange
        var originalFml = """
            map 'http://example.org/test' = 'TestMap'

            group Main(source src : Patient, target tgt : Bundle) {
              src.identifier 0..1 -> tgt.identifier;
              src.active where active.exists() -> tgt.active = copy(src.active);
              src.gender -> tgt.gender = create('code') first;
            }
            """;

        // Act
        var ast1 = _parser.Parse(originalFml);
        var structureMap = _builder.Build(ast1);
        var ast2 = _structureMapParser.Parse(structureMap);

        // Assert
        AssertAstEquivalent(ast1, ast2);
        ast2.Groups[0].Rules.Should().HaveCount(3);

        // Verify cardinality preserved
        ast2.Groups[0].Rules[0].Sources[0].Cardinality.Should().NotBeNull();
        ast2.Groups[0].Rules[0].Sources[0].Cardinality!.Min.Should().Be(0);
        ast2.Groups[0].Rules[0].Sources[0].Cardinality.Max.Should().Be(1);

        // Verify condition preserved
        ast2.Groups[0].Rules[1].Sources[0].Condition.Should().NotBeNull();

        // Verify transform and list mode preserved
        ast2.Groups[0].Rules[2].Targets[0].Transform.Should().NotBeNull();
        ast2.Groups[0].Rules[2].Targets[0].ListMode.Should().Be(ListMode.First);
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Compares two ASTs for semantic equivalence, ignoring location information.
    /// </summary>
    private static void AssertAstEquivalent(MapExpression expected, MapExpression actual)
    {
        // Compare top-level properties
        actual.Url.Should().Be(expected.Url);
        actual.Identifier.Should().Be(expected.Identifier);

        // Compare uses
        actual.Uses.Should().HaveCount(expected.Uses.Count);
        for (int i = 0; i < expected.Uses.Count; i++)
        {
            actual.Uses[i].Url.Should().Be(expected.Uses[i].Url);
            actual.Uses[i].Alias.Should().Be(expected.Uses[i].Alias);
            actual.Uses[i].Mode.Should().Be(expected.Uses[i].Mode);
        }

        // Compare imports
        actual.Imports.Should().HaveCount(expected.Imports.Count);
        for (int i = 0; i < expected.Imports.Count; i++)
        {
            actual.Imports[i].Url.Should().Be(expected.Imports[i].Url);
        }

        // Compare groups
        actual.Groups.Should().HaveCount(expected.Groups.Count);
        for (int i = 0; i < expected.Groups.Count; i++)
        {
            AssertGroupEquivalent(expected.Groups[i], actual.Groups[i]);
        }
    }

    private static void AssertGroupEquivalent(GroupExpression expected, GroupExpression actual)
    {
        actual.Name.Should().Be(expected.Name);
        actual.Extends.Should().Be(expected.Extends);

        // Compare parameters
        actual.Parameters.Should().HaveCount(expected.Parameters.Count);
        for (int i = 0; i < expected.Parameters.Count; i++)
        {
            actual.Parameters[i].Mode.Should().Be(expected.Parameters[i].Mode);
            actual.Parameters[i].Name.Should().Be(expected.Parameters[i].Name);
            actual.Parameters[i].Type.Should().Be(expected.Parameters[i].Type);
        }

        // Compare rules
        actual.Rules.Should().HaveCount(expected.Rules.Count);
        for (int i = 0; i < expected.Rules.Count; i++)
        {
            AssertRuleEquivalent(expected.Rules[i], actual.Rules[i]);
        }
    }

    private static void AssertRuleEquivalent(RuleExpression expected, RuleExpression actual)
    {
        actual.Name.Should().Be(expected.Name);

        // Compare sources
        actual.Sources.Should().HaveCount(expected.Sources.Count);
        for (int i = 0; i < expected.Sources.Count; i++)
        {
            AssertSourceEquivalent(expected.Sources[i], actual.Sources[i]);
        }

        // Compare targets
        actual.Targets.Should().HaveCount(expected.Targets.Count);
        for (int i = 0; i < expected.Targets.Count; i++)
        {
            AssertTargetEquivalent(expected.Targets[i], actual.Targets[i]);
        }

        // Compare dependent (simplified - could be more thorough)
        if (expected.Dependent == null)
        {
            actual.Dependent.Should().BeNull();
        }
        else
        {
            actual.Dependent.Should().NotBeNull();
            actual.Dependent!.GetType().Should().Be(expected.Dependent.GetType());
        }
    }

    private static void AssertSourceEquivalent(SourceExpression expected, SourceExpression actual)
    {
        actual.Variable.Should().Be(expected.Variable);
        actual.Type.Should().Be(expected.Type);

        // Compare cardinality
        if (expected.Cardinality == null)
        {
            actual.Cardinality.Should().BeNull();
        }
        else
        {
            actual.Cardinality.Should().NotBeNull();
            actual.Cardinality!.Min.Should().Be(expected.Cardinality.Min);
            actual.Cardinality.Max.Should().Be(expected.Cardinality.Max);
        }

        // Compare condition (simplified)
        if (expected.Condition == null)
        {
            actual.Condition.Should().BeNull();
        }
        else
        {
            actual.Condition.Should().NotBeNull();
            actual.Condition!.GetType().Should().Be(expected.Condition.GetType());
        }
    }

    private static void AssertTargetEquivalent(TargetExpression expected, TargetExpression actual)
    {
        actual.Variable.Should().Be(expected.Variable);
        actual.ListMode.Should().Be(expected.ListMode);

        // Compare transform (simplified)
        if (expected.Transform == null)
        {
            actual.Transform.Should().BeNull();
        }
        else
        {
            actual.Transform.Should().NotBeNull();
            actual.Transform!.GetType().Should().Be(expected.Transform.GetType());

            if (expected.Transform is TransformExpression expectedTransform &&
                actual.Transform is TransformExpression actualTransform)
            {
                actualTransform.FunctionName.Should().Be(expectedTransform.FunctionName);
                actualTransform.Arguments.Should().HaveCount(expectedTransform.Arguments.Count);
            }
        }
    }

    #endregion
}
