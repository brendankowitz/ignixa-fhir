/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Tests for StructureMapParser version compatibility (R4 vs R5).
 * Verifies parser can handle both FHIR R4 and R5 StructureMap formats.
 */

using FluentAssertions;
using Ignixa.Abstractions;
using Ignixa.FhirMappingLanguage.Expressions;
using Ignixa.FhirMappingLanguage.Parser;
using Ignixa.Serialization;
using Ignixa.Serialization.Models;
using System.Text.Json.Nodes;
using Xunit;

namespace Ignixa.FhirMappingLanguage.Tests.Parser;

/// <summary>
/// Tests that verify StructureMapParser correctly handles version-specific properties.
/// </summary>
public class StructureMapParserVersionTests
{
    private readonly StructureMapParser _parser = new();

    #region R4 Format Tests

    [Fact]
    public void GivenR4StructureMapWithDependentVariables_WhenParsing_ThenParsesCorrectly()
    {
        // Arrange - R4 format uses dependent.variable (string array)
        var structureMapJson = new JsonObject
        {
            ["resourceType"] = "StructureMap",
            ["url"] = "http://example.org/test-r4",
            ["name"] = "TestMapR4",
            ["status"] = "draft",
            ["group"] = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = "Main",
                    ["typeMode"] = "none",
                    ["input"] = new JsonArray
                    {
                        new JsonObject { ["name"] = "src", ["mode"] = "source" }
                    },
                    ["rule"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["name"] = "rule1",
                            ["source"] = new JsonArray
                            {
                                new JsonObject { ["context"] = "src" }
                            },
                            ["dependent"] = new JsonArray
                            {
                                new JsonObject
                                {
                                    ["name"] = "HelperGroup",
                                    ["variable"] = new JsonArray { "var1", "var2" }
                                }
                            }
                        }
                    }
                }
            }
        };

        var structureMap = new StructureMapJsonNode(structureMapJson)
        {
            FhirVersion = FhirVersion.R4
        };

        // Act
        var ast = _parser.Parse(structureMap);

        // Assert
        ast.Should().NotBeNull();
        ast.Groups.Should().HaveCount(1);
        ast.Groups[0].Rules.Should().HaveCount(1);

        var dependent = ast.Groups[0].Rules[0].Dependent;
        dependent.Should().NotBeNull();
        dependent.Should().BeOfType<GroupInvocationExpression>();

        var invocation = (GroupInvocationExpression)dependent!;
        invocation.GroupName.Should().Be("HelperGroup");
        invocation.Arguments.Should().HaveCount(2);
        invocation.Arguments[0].Should().BeOfType<IdentifierExpression>();
        ((IdentifierExpression)invocation.Arguments[0]).Name.Should().Be("var1");
        invocation.Arguments[1].Should().BeOfType<IdentifierExpression>();
        ((IdentifierExpression)invocation.Arguments[1]).Name.Should().Be("var2");
    }

    [Fact]
    public void GivenR4StructureMapWithDefaultValueString_WhenParsing_ThenParsesCorrectly()
    {
        // Arrange - R4 format uses defaultValue[x] (e.g., defaultValueString)
        var structureMapJson = new JsonObject
        {
            ["resourceType"] = "StructureMap",
            ["url"] = "http://example.org/test-r4-default",
            ["name"] = "TestMapR4Default",
            ["status"] = "draft",
            ["group"] = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = "Main",
                    ["typeMode"] = "none",
                    ["input"] = new JsonArray
                    {
                        new JsonObject { ["name"] = "src", ["mode"] = "source" }
                    },
                    ["rule"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["name"] = "rule1",
                            ["source"] = new JsonArray
                            {
                                new JsonObject
                                {
                                    ["context"] = "src",
                                    ["element"] = "name",
                                    ["variable"] = "n",
                                    ["defaultValueString"] = "DefaultName"
                                }
                            }
                        }
                    }
                }
            }
        };

        var structureMap = new StructureMapJsonNode(structureMapJson)
        {
            FhirVersion = FhirVersion.R4
        };

        // Act
        var ast = _parser.Parse(structureMap);

        // Assert
        ast.Should().NotBeNull();
        ast.Groups[0].Rules[0].Sources.Should().HaveCount(1);

        var source = ast.Groups[0].Rules[0].Sources[0];
        source.Default.Should().NotBeNull();
        source.Default.Should().BeOfType<LiteralExpression>();
        ((LiteralExpression)source.Default!).Value.Should().Be("DefaultName");
    }

    [Fact]
    public void GivenR4StructureMapWithDefaultValueInteger_WhenParsing_ThenParsesCorrectly()
    {
        // Arrange - R4 format with defaultValueInteger
        var structureMapJson = new JsonObject
        {
            ["resourceType"] = "StructureMap",
            ["url"] = "http://example.org/test-r4-int",
            ["name"] = "TestMapR4Int",
            ["status"] = "draft",
            ["group"] = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = "Main",
                    ["typeMode"] = "none",
                    ["input"] = new JsonArray
                    {
                        new JsonObject { ["name"] = "src", ["mode"] = "source" }
                    },
                    ["rule"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["name"] = "rule1",
                            ["source"] = new JsonArray
                            {
                                new JsonObject
                                {
                                    ["context"] = "src",
                                    ["variable"] = "v",
                                    ["defaultValueInteger"] = 42
                                }
                            }
                        }
                    }
                }
            }
        };

        var structureMap = new StructureMapJsonNode(structureMapJson)
        {
            FhirVersion = FhirVersion.R4
        };

        // Act
        var ast = _parser.Parse(structureMap);

        // Assert
        ast.Groups[0].Rules[0].Sources[0].Default.Should().NotBeNull();
        ast.Groups[0].Rules[0].Sources[0].Default.Should().BeOfType<LiteralExpression>();
        ((LiteralExpression)ast.Groups[0].Rules[0].Sources[0].Default!).Value.Should().Be(42);
    }

    #endregion

    #region R5 Format Tests

    [Fact]
    public void GivenR5StructureMapWithDependentParameters_WhenParsing_ThenParsesCorrectly()
    {
        // Arrange - R5 format uses dependent.parameter (StructureMapParameter array)
        var structureMapJson = new JsonObject
        {
            ["resourceType"] = "StructureMap",
            ["url"] = "http://example.org/test-r5",
            ["name"] = "TestMapR5",
            ["status"] = "draft",
            ["group"] = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = "Main",
                    ["typeMode"] = "none",
                    ["input"] = new JsonArray
                    {
                        new JsonObject { ["name"] = "src", ["mode"] = "source" }
                    },
                    ["rule"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["name"] = "rule1",
                            ["source"] = new JsonArray
                            {
                                new JsonObject { ["context"] = "src" }
                            },
                            ["dependent"] = new JsonArray
                            {
                                new JsonObject
                                {
                                    ["name"] = "HelperGroup",
                                    ["parameter"] = new JsonArray
                                    {
                                        new JsonObject { ["valueId"] = "var1" },
                                        new JsonObject { ["valueId"] = "var2" }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };

        var structureMap = new StructureMapJsonNode(structureMapJson)
        {
            FhirVersion = FhirVersion.R5
        };

        // Act
        var ast = _parser.Parse(structureMap);

        // Assert
        ast.Should().NotBeNull();
        ast.Groups.Should().HaveCount(1);
        ast.Groups[0].Rules.Should().HaveCount(1);

        var dependent = ast.Groups[0].Rules[0].Dependent;
        dependent.Should().NotBeNull();
        dependent.Should().BeOfType<GroupInvocationExpression>();

        var invocation = (GroupInvocationExpression)dependent!;
        invocation.GroupName.Should().Be("HelperGroup");
        invocation.Arguments.Should().HaveCount(2);
        invocation.Arguments[0].Should().BeOfType<IdentifierExpression>();
        ((IdentifierExpression)invocation.Arguments[0]).Name.Should().Be("var1");
        invocation.Arguments[1].Should().BeOfType<IdentifierExpression>();
        ((IdentifierExpression)invocation.Arguments[1]).Name.Should().Be("var2");
    }

    [Fact]
    public void GivenR5StructureMapWithDefaultValue_WhenParsing_ThenParsesCorrectly()
    {
        // Arrange - R5 format uses defaultValue (string property, not defaultValue[x])
        var structureMapJson = new JsonObject
        {
            ["resourceType"] = "StructureMap",
            ["url"] = "http://example.org/test-r5-default",
            ["name"] = "TestMapR5Default",
            ["status"] = "draft",
            ["group"] = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = "Main",
                    ["typeMode"] = "none",
                    ["input"] = new JsonArray
                    {
                        new JsonObject { ["name"] = "src", ["mode"] = "source" }
                    },
                    ["rule"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["name"] = "rule1",
                            ["source"] = new JsonArray
                            {
                                new JsonObject
                                {
                                    ["context"] = "src",
                                    ["element"] = "name",
                                    ["variable"] = "n",
                                    ["defaultValue"] = "'DefaultName'"
                                }
                            }
                        }
                    }
                }
            }
        };

        var structureMap = new StructureMapJsonNode(structureMapJson)
        {
            FhirVersion = FhirVersion.R5
        };

        // Act
        var ast = _parser.Parse(structureMap);

        // Assert
        ast.Should().NotBeNull();
        ast.Groups[0].Rules[0].Sources.Should().HaveCount(1);

        var source = ast.Groups[0].Rules[0].Sources[0];
        source.Default.Should().NotBeNull();
        source.Default.Should().BeOfType<LiteralExpression>();
        ((LiteralExpression)source.Default!).Value.Should().Be("'DefaultName'");
    }

    #endregion

    #region Version Unspecified Tests

    [Fact]
    public void GivenStructureMapWithoutFhirVersion_WhenParsingR4Format_ThenParsesCorrectly()
    {
        // Arrange - No FhirVersion set, but uses R4 format (variable array)
        var structureMapJson = new JsonObject
        {
            ["resourceType"] = "StructureMap",
            ["url"] = "http://example.org/test-unspecified",
            ["name"] = "TestMapUnspecified",
            ["status"] = "draft",
            ["group"] = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = "Main",
                    ["typeMode"] = "none",
                    ["input"] = new JsonArray
                    {
                        new JsonObject { ["name"] = "src", ["mode"] = "source" }
                    },
                    ["rule"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["name"] = "rule1",
                            ["source"] = new JsonArray
                            {
                                new JsonObject { ["context"] = "src" }
                            },
                            ["dependent"] = new JsonArray
                            {
                                new JsonObject
                                {
                                    ["name"] = "HelperGroup",
                                    ["variable"] = new JsonArray { "var1" }
                                }
                            }
                        }
                    }
                }
            }
        };

        var structureMap = new StructureMapJsonNode(structureMapJson);
        // Note: FhirVersion is NOT set

        // Act
        var ast = _parser.Parse(structureMap);

        // Assert - Should parse successfully by detecting R4 format
        ast.Should().NotBeNull();
        var dependent = ast.Groups[0].Rules[0].Dependent;
        dependent.Should().NotBeNull();
        dependent.Should().BeOfType<GroupInvocationExpression>();
        ((GroupInvocationExpression)dependent!).Arguments.Should().HaveCount(1);
    }

    [Fact]
    public void GivenStructureMapWithoutFhirVersion_WhenParsingR5Format_ThenParsesCorrectly()
    {
        // Arrange - No FhirVersion set, but uses R5 format (parameter array)
        var structureMapJson = new JsonObject
        {
            ["resourceType"] = "StructureMap",
            ["url"] = "http://example.org/test-unspecified-r5",
            ["name"] = "TestMapUnspecifiedR5",
            ["status"] = "draft",
            ["group"] = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = "Main",
                    ["typeMode"] = "none",
                    ["input"] = new JsonArray
                    {
                        new JsonObject { ["name"] = "src", ["mode"] = "source" }
                    },
                    ["rule"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["name"] = "rule1",
                            ["source"] = new JsonArray
                            {
                                new JsonObject { ["context"] = "src" }
                            },
                            ["dependent"] = new JsonArray
                            {
                                new JsonObject
                                {
                                    ["name"] = "HelperGroup",
                                    ["parameter"] = new JsonArray
                                    {
                                        new JsonObject { ["valueId"] = "var1" }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };

        var structureMap = new StructureMapJsonNode(structureMapJson);
        // Note: FhirVersion is NOT set

        // Act
        var ast = _parser.Parse(structureMap);

        // Assert - Should parse successfully by detecting R5 format
        ast.Should().NotBeNull();
        var dependent = ast.Groups[0].Rules[0].Dependent;
        dependent.Should().NotBeNull();
        dependent.Should().BeOfType<GroupInvocationExpression>();
        ((GroupInvocationExpression)dependent!).Arguments.Should().HaveCount(1);
    }

    #endregion

    #region Parser Constructor Version Tests

    [Fact]
    public void GivenParserWithExpectedVersion_WhenParsingUnversionedMap_ThenAppliesVersion()
    {
        // Arrange
        var json = new JsonObject
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
                    ["input"] = new JsonArray()
                }
            }
        };

        var structureMap = new StructureMapJsonNode(json);
        // Don't set FhirVersion on the node

        var parser = new StructureMapParser(FhirVersion.R4);

        // Act
        var ast = parser.Parse(structureMap);

        // Assert
        ast.Url.Should().Be("http://example.org/test");
        structureMap.FhirVersion.Should().Be(FhirVersion.R4); // Version was applied
    }

    [Fact]
    public void GivenParserWithExpectedVersion_WhenParsingVersionedMap_ThenKeepsOriginalVersion()
    {
        // Arrange
        var json = new JsonObject
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
                    ["input"] = new JsonArray()
                }
            }
        };

        var structureMap = new StructureMapJsonNode(json);
        structureMap.FhirVersion = FhirVersion.R5; // Already set

        var parser = new StructureMapParser(FhirVersion.R4); // Different expected version

        // Act
        var ast = parser.Parse(structureMap);

        // Assert
        structureMap.FhirVersion.Should().Be(FhirVersion.R5); // Original version preserved
    }

    [Fact]
    public void GivenParserWithoutExpectedVersion_WhenParsingUnversionedMap_ThenLeavesVersionUnset()
    {
        // Arrange
        var json = new JsonObject
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
                    ["input"] = new JsonArray()
                }
            }
        };

        var structureMap = new StructureMapJsonNode(json);
        // Don't set FhirVersion on the node

        var parser = new StructureMapParser(); // No expected version

        // Act
        var ast = parser.Parse(structureMap);

        // Assert
        ast.Url.Should().Be("http://example.org/test");
        structureMap.FhirVersion.Should().BeNull(); // Version remains unset
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void GivenStructureMapWithEmptyDependentVariables_WhenParsing_ThenReturnsEmptyParameters()
    {
        // Arrange
        var structureMapJson = new JsonObject
        {
            ["resourceType"] = "StructureMap",
            ["url"] = "http://example.org/test-empty",
            ["name"] = "TestMapEmpty",
            ["status"] = "draft",
            ["group"] = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = "Main",
                    ["typeMode"] = "none",
                    ["input"] = new JsonArray
                    {
                        new JsonObject { ["name"] = "src", ["mode"] = "source" }
                    },
                    ["rule"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["name"] = "rule1",
                            ["source"] = new JsonArray
                            {
                                new JsonObject { ["context"] = "src" }
                            },
                            ["dependent"] = new JsonArray
                            {
                                new JsonObject
                                {
                                    ["name"] = "HelperGroup",
                                    ["variable"] = new JsonArray()
                                }
                            }
                        }
                    }
                }
            }
        };

        var structureMap = new StructureMapJsonNode(structureMapJson)
        {
            FhirVersion = FhirVersion.R4
        };

        // Act
        var ast = _parser.Parse(structureMap);

        // Assert
        var dependent = ast.Groups[0].Rules[0].Dependent;
        dependent.Should().NotBeNull();
        ((GroupInvocationExpression)dependent!).Arguments.Should().BeEmpty();
    }

    [Fact]
    public void GivenStructureMapWithNestedRules_WhenParsing_ThenParsesAsRuleSet()
    {
        // Arrange - Nested rules should take precedence over dependent calls
        var structureMapJson = new JsonObject
        {
            ["resourceType"] = "StructureMap",
            ["url"] = "http://example.org/test-nested",
            ["name"] = "TestMapNested",
            ["status"] = "draft",
            ["group"] = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = "Main",
                    ["typeMode"] = "none",
                    ["input"] = new JsonArray
                    {
                        new JsonObject { ["name"] = "src", ["mode"] = "source" }
                    },
                    ["rule"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["name"] = "rule1",
                            ["source"] = new JsonArray
                            {
                                new JsonObject { ["context"] = "src" }
                            },
                            ["rule"] = new JsonArray
                            {
                                new JsonObject
                                {
                                    ["name"] = "nestedRule1",
                                    ["source"] = new JsonArray
                                    {
                                        new JsonObject { ["context"] = "src" }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };

        var structureMap = new StructureMapJsonNode(structureMapJson)
        {
            FhirVersion = FhirVersion.R4
        };

        // Act
        var ast = _parser.Parse(structureMap);

        // Assert
        var dependent = ast.Groups[0].Rules[0].Dependent;
        dependent.Should().NotBeNull();
        dependent.Should().BeOfType<RuleSetExpression>();

        var ruleSet = (RuleSetExpression)dependent!;
        ruleSet.Rules.Should().HaveCount(1);
        ruleSet.Rules[0].Name.Should().Be("nestedRule1");
    }

    #endregion
}
