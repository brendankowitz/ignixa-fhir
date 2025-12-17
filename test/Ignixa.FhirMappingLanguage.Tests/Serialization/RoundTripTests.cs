/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Round-trip serialization tests for FHIR Mapping Language.
 * Verifies lossless conversion between FML text, AST, and StructureMap resources.
 */

using Shouldly;
using Ignixa.FhirMappingLanguage;
using Ignixa.FhirMappingLanguage.Expressions;
using Ignixa.FhirMappingLanguage.Serialization;
using System.Text.Json.Nodes;
using Ignixa.FhirMappingLanguage.Parser;
using Ignixa.Serialization.Models;
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
        roundTrippedAst.Url.ShouldBe("http://example.org/test");
        roundTrippedAst.Identifier.ShouldBe("TestMap");
        roundTrippedAst.Groups.Count.ShouldBe(1);
        roundTrippedAst.Groups[0].Name.ShouldBe("Main");
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
        roundTrippedAst.Uses.Count.ShouldBe(2);
        roundTrippedAst.Uses[0].Url.ShouldBe("http://hl7.org/fhir/StructureDefinition/Patient");
        roundTrippedAst.Uses[0].Alias.ShouldBe("Patient");
        roundTrippedAst.Uses[0].Mode.ShouldBe(ModelMode.Source);
        roundTrippedAst.Uses[1].Url.ShouldBe("http://hl7.org/fhir/StructureDefinition/Bundle");
        roundTrippedAst.Uses[1].Alias.ShouldBe("Bundle");
        roundTrippedAst.Uses[1].Mode.ShouldBe(ModelMode.Target);
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
        roundTrippedAst.Imports.Count.ShouldBe(2);
        roundTrippedAst.Imports[0].Url.ShouldBe("http://example.org/helper");
        roundTrippedAst.Imports[1].Url.ShouldBe("http://example.org/common");
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
        group.Parameters.Count.ShouldBe(3);
        group.Parameters[0].Mode.ShouldBe(ParameterMode.Source);
        group.Parameters[0].Name.ShouldBe("src");
        group.Parameters[0].Type.ShouldBe("Patient");
        group.Parameters[1].Mode.ShouldBe(ParameterMode.Target);
        group.Parameters[1].Name.ShouldBe("tgt");
        group.Parameters[1].Type.ShouldBe("Bundle");
        group.Parameters[2].Mode.ShouldBe(ParameterMode.Source);
        group.Parameters[2].Name.ShouldBe("context");
        group.Parameters[2].Type.ShouldBeNull();
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
        roundTrippedAst.Groups.Count.ShouldBe(2);
        roundTrippedAst.Groups[0].Name.ShouldBe("Base");
        roundTrippedAst.Groups[0].Extends.ShouldBeNull();
        roundTrippedAst.Groups[1].Name.ShouldBe("Main");
        roundTrippedAst.Groups[1].Extends.ShouldBe("Base");
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
        rules.Count.ShouldBe(2);

        // First rule: create transform
        rules[0].Sources.Count.ShouldBe(1);
        rules[0].Targets.Count.ShouldBe(1);
        rules[0].Targets[0].Transform.ShouldBeOfType<TransformExpression>();
        var createTransform = rules[0].Targets[0].Transform as TransformExpression;
        createTransform!.FunctionName.ShouldBe("create");
        createTransform.Arguments.Count.ShouldBe(1);

        // Second rule: copy transform
        rules[1].Targets[0].Transform.ShouldBeOfType<TransformExpression>();
        var copyTransform = rules[1].Targets[0].Transform as TransformExpression;
        copyTransform!.FunctionName.ShouldBe("copy");
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
        rule.Sources.Count.ShouldBe(1);
        rule.Sources[0].Condition.ShouldNotBeNull();
        rule.Sources[0].Condition.ShouldBeOfType<FhirPathExpression>();
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
        rules.Count.ShouldBe(3);

        rules[0].Sources[0].Cardinality.ShouldNotBeNull();
        rules[0].Sources[0].Cardinality!.Min.ShouldBe(0);
        rules[0].Sources[0].Cardinality.Max.ShouldBe(1);

        rules[1].Sources[0].Cardinality.ShouldNotBeNull();
        rules[1].Sources[0].Cardinality!.Min.ShouldBe(1);
        rules[1].Sources[0].Cardinality.Max.ShouldBeNull(); // * means unbounded

        rules[2].Sources[0].Cardinality.ShouldNotBeNull();
        rules[2].Sources[0].Cardinality!.Min.ShouldBe(0);
        rules[2].Sources[0].Cardinality.Max.ShouldBeNull();
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
        rules.Count.ShouldBe(2);

        rules[0].Targets[0].ListMode.ShouldBe(ListMode.First);
        rules[1].Targets[0].ListMode.ShouldBe(ListMode.Share);
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
        rule.Dependent.ShouldBeOfType<RuleSetExpression>();
        var ruleSet = rule.Dependent as RuleSetExpression;
        ruleSet!.Rules.Count.ShouldBe(2);
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
        rule.Dependent.ShouldBeOfType<GroupInvocationExpression>();
        var invocation = rule.Dependent as GroupInvocationExpression;
        invocation!.GroupName.ShouldBe("ProcessName");
        invocation.Arguments.Count.ShouldBe(2);
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
        roundTrippedAst.Url.ShouldBe("http://example.org/test");
        roundTrippedAst.Identifier.ShouldBe("TestMap");
        roundTrippedAst.Uses.Count.ShouldBe(2);
        roundTrippedAst.Imports.Count.ShouldBe(1);
        roundTrippedAst.Groups.Count.ShouldBe(3);

        // Verify groups
        roundTrippedAst.Groups[0].Name.ShouldBe("ProcessName");
        roundTrippedAst.Groups[1].Name.ShouldBe("Base");
        roundTrippedAst.Groups[2].Name.ShouldBe("Main");
        roundTrippedAst.Groups[2].Extends.ShouldBe("Base");
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
        var structureMapNode = new StructureMapJsonNode(structureMap);
        var ast = _structureMapParser.Parse(structureMapNode);
        var rebuiltStructureMap = _builder.Build(ast);
        var roundTrippedAst = _structureMapParser.Parse(rebuiltStructureMap);

        // Assert
        AssertAstEquivalent(ast, roundTrippedAst);
        rebuiltStructureMap.Url.ShouldBe("http://example.org/test");
        rebuiltStructureMap.Name.ShouldBe("TestMap");
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
        var structureMapNode = new StructureMapJsonNode(structureMap);
        var ast = _structureMapParser.Parse(structureMapNode);
        var rebuiltStructureMap = _builder.Build(ast);

        // Assert
        ast.Uses.Count.ShouldBe(2);
        rebuiltStructureMap.Structure.Count.ShouldBe(2);
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
        finalAst.Url.ShouldBe("http://example.org/test");
        finalAst.Identifier.ShouldBe("TestMap");
        finalAst.Uses.Count.ShouldBe(2);
        finalAst.Groups.Count.ShouldBe(1);
        finalAst.Groups[0].Rules.Count.ShouldBe(1);
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
        ast2.Groups[0].Rules.Count.ShouldBe(3);

        // Verify cardinality preserved
        ast2.Groups[0].Rules[0].Sources[0].Cardinality.ShouldNotBeNull();
        ast2.Groups[0].Rules[0].Sources[0].Cardinality!.Min.ShouldBe(0);
        ast2.Groups[0].Rules[0].Sources[0].Cardinality.Max.ShouldBe(1);

        // Verify condition preserved
        ast2.Groups[0].Rules[1].Sources[0].Condition.ShouldNotBeNull();

        // Verify transform and list mode preserved
        ast2.Groups[0].Rules[2].Targets[0].Transform.ShouldNotBeNull();
        ast2.Groups[0].Rules[2].Targets[0].ListMode.ShouldBe(ListMode.First);
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Compares two ASTs for semantic equivalence, ignoring location information.
    /// </summary>
    private static void AssertAstEquivalent(MapExpression expected, MapExpression actual)
    {
        // Compare top-level properties
        actual.Url.ShouldBe(expected.Url);
        actual.Identifier.ShouldBe(expected.Identifier);

        // Compare uses
        actual.Uses.Count.ShouldBe(expected.Uses.Count);
        for (int i = 0; i < expected.Uses.Count; i++)
        {
            actual.Uses[i].Url.ShouldBe(expected.Uses[i].Url);
            actual.Uses[i].Alias.ShouldBe(expected.Uses[i].Alias);
            actual.Uses[i].Mode.ShouldBe(expected.Uses[i].Mode);
        }

        // Compare imports
        actual.Imports.Count.ShouldBe(expected.Imports.Count);
        for (int i = 0; i < expected.Imports.Count; i++)
        {
            actual.Imports[i].Url.ShouldBe(expected.Imports[i].Url);
        }

        // Compare groups
        actual.Groups.Count.ShouldBe(expected.Groups.Count);
        for (int i = 0; i < expected.Groups.Count; i++)
        {
            AssertGroupEquivalent(expected.Groups[i], actual.Groups[i]);
        }
    }

    private static void AssertGroupEquivalent(GroupExpression expected, GroupExpression actual)
    {
        actual.Name.ShouldBe(expected.Name);
        actual.Extends.ShouldBe(expected.Extends);

        // Compare parameters
        actual.Parameters.Count.ShouldBe(expected.Parameters.Count);
        for (int i = 0; i < expected.Parameters.Count; i++)
        {
            actual.Parameters[i].Mode.ShouldBe(expected.Parameters[i].Mode);
            actual.Parameters[i].Name.ShouldBe(expected.Parameters[i].Name);
            actual.Parameters[i].Type.ShouldBe(expected.Parameters[i].Type);
        }

        // Compare rules
        actual.Rules.Count.ShouldBe(expected.Rules.Count);
        for (int i = 0; i < expected.Rules.Count; i++)
        {
            AssertRuleEquivalent(expected.Rules[i], actual.Rules[i]);
        }
    }

    private static void AssertRuleEquivalent(RuleExpression expected, RuleExpression actual)
    {
        actual.Name.ShouldBe(expected.Name);

        // Compare sources
        actual.Sources.Count.ShouldBe(expected.Sources.Count);
        for (int i = 0; i < expected.Sources.Count; i++)
        {
            AssertSourceEquivalent(expected.Sources[i], actual.Sources[i]);
        }

        // Compare targets
        actual.Targets.Count.ShouldBe(expected.Targets.Count);
        for (int i = 0; i < expected.Targets.Count; i++)
        {
            AssertTargetEquivalent(expected.Targets[i], actual.Targets[i]);
        }

        // Compare dependent (simplified - could be more thorough)
        if (expected.Dependent == null)
        {
            actual.Dependent.ShouldBeNull();
        }
        else
        {
            actual.Dependent.ShouldNotBeNull();
            actual.Dependent!.GetType().ShouldBe(expected.Dependent.GetType());
        }
    }

    private static void AssertSourceEquivalent(SourceExpression expected, SourceExpression actual)
    {
        actual.Variable.ShouldBe(expected.Variable);
        actual.Type.ShouldBe(expected.Type);

        // Compare cardinality
        if (expected.Cardinality == null)
        {
            actual.Cardinality.ShouldBeNull();
        }
        else
        {
            actual.Cardinality.ShouldNotBeNull();
            actual.Cardinality!.Min.ShouldBe(expected.Cardinality.Min);
            actual.Cardinality.Max.ShouldBe(expected.Cardinality.Max);
        }

        // Compare condition (simplified)
        if (expected.Condition == null)
        {
            actual.Condition.ShouldBeNull();
        }
        else
        {
            actual.Condition.ShouldNotBeNull();
            actual.Condition!.GetType().ShouldBe(expected.Condition.GetType());
        }
    }

    private static void AssertTargetEquivalent(TargetExpression expected, TargetExpression actual)
    {
        actual.Variable.ShouldBe(expected.Variable);
        actual.ListMode.ShouldBe(expected.ListMode);

        // Compare transform (simplified)
        if (expected.Transform == null)
        {
            actual.Transform.ShouldBeNull();
        }
        else
        {
            actual.Transform.ShouldNotBeNull();
            actual.Transform!.GetType().ShouldBe(expected.Transform.GetType());

            if (expected.Transform is TransformExpression expectedTransform &&
                actual.Transform is TransformExpression actualTransform)
            {
                actualTransform.FunctionName.ShouldBe(expectedTransform.FunctionName);
                actualTransform.Arguments.Count.ShouldBe(expectedTransform.Arguments.Count);
            }
        }
    }

    #endregion
}
