/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Edge case tests for StructureMapParser with JSON StructureMap resources.
 * Tests critical parsing scenarios including targets without variables, various transforms, and complex nesting.
 */

using Shouldly;
using Ignixa.Abstractions;
using Ignixa.FhirMappingLanguage.Expressions;
using Ignixa.FhirMappingLanguage.Parser;
using Ignixa.Serialization.Models;
using System.Text.Json.Nodes;
using Xunit;

namespace Ignixa.FhirMappingLanguage.Tests.Parser;

/// <summary>
/// Tests edge cases for StructureMapParser JSON parsing.
/// </summary>
public class StructureMapJsonEdgeCasesTests
{
    private readonly StructureMapParser _parser = new();

    #region CRITICAL Edge Cases

    [Fact]
    public void GivenTargetWithTransformButNoVariable_WhenParsing_ThenParsesTransformCorrectly()
    {
        // Arrange - This is the exact bug pattern that was fixed
        var structureMapJson = new JsonObject
        {
            ["resourceType"] = "StructureMap",
            ["url"] = "http://example.org/test-target-no-var",
            ["name"] = "TestTargetNoVariable",
            ["status"] = "draft",
            ["group"] = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = "Transform",
                    ["typeMode"] = "none",
                    ["input"] = new JsonArray
                    {
                        new JsonObject { ["name"] = "src", ["mode"] = "source" },
                        new JsonObject { ["name"] = "tgt", ["mode"] = "target" }
                    },
                    ["rule"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["name"] = "copyStatus",
                            ["source"] = new JsonArray
                            {
                                new JsonObject
                                {
                                    ["context"] = "src",
                                    ["element"] = "status",
                                    ["variable"] = "vStatus"
                                }
                            },
                            ["target"] = new JsonArray
                            {
                                new JsonObject
                                {
                                    ["context"] = "tgt",
                                    ["element"] = "status",
                                    // No variable, only transform
                                    ["transform"] = "copy",
                                    ["parameter"] = new JsonArray
                                    {
                                        new JsonObject { ["valueId"] = "vStatus" }
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
        ast.ShouldNotBeNull();
        var target = ast.Groups[0].Rules[0].Targets[0];
        target.Variable.ShouldBeNull();
        target.Transform.ShouldNotBeNull();
        target.Transform.ShouldBeOfType<TransformExpression>();
        ((TransformExpression)target.Transform!).FunctionName.ShouldBe("copy");
        ((TransformExpression)target.Transform!).Arguments.Count.ShouldBe(1);
    }

    [Fact]
    public void GivenSourceWithElementButNoVariable_WhenParsing_ThenParsesContextCorrectly()
    {
        // Arrange - Source can have element without variable (used for filtering)
        var structureMapJson = new JsonObject
        {
            ["resourceType"] = "StructureMap",
            ["url"] = "http://example.org/test-source-no-var",
            ["name"] = "TestSourceNoVariable",
            ["status"] = "draft",
            ["group"] = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = "Transform",
                    ["typeMode"] = "none",
                    ["input"] = new JsonArray
                    {
                        new JsonObject { ["name"] = "src", ["mode"] = "source" }
                    },
                    ["rule"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["name"] = "filterRule",
                            ["source"] = new JsonArray
                            {
                                new JsonObject
                                {
                                    ["context"] = "src",
                                    ["element"] = "name",
                                    // No variable - just filtering
                                    ["condition"] = "family.exists()"
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
        ast.ShouldNotBeNull();
        var source = ast.Groups[0].Rules[0].Sources[0];
        source.Variable.ShouldBeNull();
        source.Context.ShouldBeOfType<QualifiedIdentifierExpression>();
        var qualified = (QualifiedIdentifierExpression)source.Context;
        qualified.ToString().ShouldContain("name");
        source.Condition.ShouldNotBeNull();
        source.Condition.ShouldBeOfType<FhirPathExpression>();
    }

    [Fact]
    public void GivenTransformWithoutParameterArray_WhenParsing_ThenCreatesTransformWithEmptyArgs()
    {
        // Arrange - Transform without explicit parameter array (uuid can work without parameters)
        var structureMapJson = new JsonObject
        {
            ["resourceType"] = "StructureMap",
            ["url"] = "http://example.org/test-no-param-array",
            ["name"] = "TestNoParamArray",
            ["status"] = "draft",
            ["group"] = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = "Transform",
                    ["typeMode"] = "none",
                    ["input"] = new JsonArray
                    {
                        new JsonObject { ["name"] = "src", ["mode"] = "source" },
                        new JsonObject { ["name"] = "tgt", ["mode"] = "target" }
                    },
                    ["rule"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["name"] = "generateId",
                            ["source"] = new JsonArray
                            {
                                new JsonObject { ["context"] = "src" }
                            },
                            ["target"] = new JsonArray
                            {
                                new JsonObject
                                {
                                    ["context"] = "tgt",
                                    ["element"] = "id",
                                    ["variable"] = "vid",
                                    ["transform"] = "uuid"
                                    // No parameter array at all
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
        ast.Groups[0].Rules[0].Targets[0].Transform.ShouldNotBeNull();
        var uuidTransform = (TransformExpression)ast.Groups[0].Rules[0].Targets[0].Transform!;
        uuidTransform.FunctionName.ShouldBe("uuid");
        uuidTransform.Arguments.ShouldBeEmpty();
    }

    [Fact]
    public void GivenAllTransformTypes_WhenParsing_ThenParsesAllCorrectly()
    {
        // Arrange - Test various transform types: copy, create, evaluate, translate, uuid, cc, qty, id, cp, reference
        var structureMapJson = new JsonObject
        {
            ["resourceType"] = "StructureMap",
            ["url"] = "http://example.org/test-all-transforms",
            ["name"] = "TestAllTransforms",
            ["status"] = "draft",
            ["group"] = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = "Transform",
                    ["typeMode"] = "none",
                    ["input"] = new JsonArray
                    {
                        new JsonObject { ["name"] = "src", ["mode"] = "source" },
                        new JsonObject { ["name"] = "tgt", ["mode"] = "target" }
                    },
                    ["rule"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["name"] = "copyTransform",
                            ["source"] = new JsonArray { new JsonObject { ["context"] = "src", ["variable"] = "v" } },
                            ["target"] = new JsonArray
                            {
                                new JsonObject
                                {
                                    ["context"] = "tgt",
                                    ["transform"] = "copy",
                                    ["parameter"] = new JsonArray { new JsonObject { ["valueId"] = "v" } }
                                }
                            }
                        },
                        new JsonObject
                        {
                            ["name"] = "createTransform",
                            ["source"] = new JsonArray { new JsonObject { ["context"] = "src" } },
                            ["target"] = new JsonArray
                            {
                                new JsonObject
                                {
                                    ["context"] = "tgt",
                                    ["transform"] = "create",
                                    ["parameter"] = new JsonArray { new JsonObject { ["valueString"] = "Patient" } }
                                }
                            }
                        },
                        new JsonObject
                        {
                            ["name"] = "evaluateTransform",
                            ["source"] = new JsonArray { new JsonObject { ["context"] = "src", ["variable"] = "v" } },
                            ["target"] = new JsonArray
                            {
                                new JsonObject
                                {
                                    ["context"] = "tgt",
                                    ["transform"] = "evaluate",
                                    ["parameter"] = new JsonArray
                                    {
                                        new JsonObject { ["valueId"] = "src" },
                                        new JsonObject { ["valueString"] = "name.given.first()" }
                                    }
                                }
                            }
                        },
                        new JsonObject
                        {
                            ["name"] = "translateTransform",
                            ["source"] = new JsonArray { new JsonObject { ["context"] = "src", ["variable"] = "v" } },
                            ["target"] = new JsonArray
                            {
                                new JsonObject
                                {
                                    ["context"] = "tgt",
                                    ["transform"] = "translate",
                                    ["parameter"] = new JsonArray
                                    {
                                        new JsonObject { ["valueId"] = "v" },
                                        new JsonObject { ["valueString"] = "#conceptMap" },
                                        new JsonObject { ["valueString"] = "code" }
                                    }
                                }
                            }
                        },
                        new JsonObject
                        {
                            ["name"] = "ccTransform",
                            ["source"] = new JsonArray { new JsonObject { ["context"] = "src", ["variable"] = "v" } },
                            ["target"] = new JsonArray
                            {
                                new JsonObject
                                {
                                    ["context"] = "tgt",
                                    ["transform"] = "cc",
                                    ["parameter"] = new JsonArray
                                    {
                                        new JsonObject { ["valueString"] = "http://loinc.org" },
                                        new JsonObject { ["valueString"] = "8867-4" }
                                    }
                                }
                            }
                        },
                        new JsonObject
                        {
                            ["name"] = "qtyTransform",
                            ["source"] = new JsonArray { new JsonObject { ["context"] = "src" } },
                            ["target"] = new JsonArray
                            {
                                new JsonObject
                                {
                                    ["context"] = "tgt",
                                    ["transform"] = "qty",
                                    ["parameter"] = new JsonArray
                                    {
                                        new JsonObject { ["valueInteger"] = 100 },
                                        new JsonObject { ["valueString"] = "mg" }
                                    }
                                }
                            }
                        },
                        new JsonObject
                        {
                            ["name"] = "idTransform",
                            ["source"] = new JsonArray { new JsonObject { ["context"] = "src" } },
                            ["target"] = new JsonArray
                            {
                                new JsonObject
                                {
                                    ["context"] = "tgt",
                                    ["transform"] = "id",
                                    ["parameter"] = new JsonArray
                                    {
                                        new JsonObject { ["valueString"] = "Patient/123" }
                                    }
                                }
                            }
                        },
                        new JsonObject
                        {
                            ["name"] = "cpTransform",
                            ["source"] = new JsonArray { new JsonObject { ["context"] = "src", ["variable"] = "v" } },
                            ["target"] = new JsonArray
                            {
                                new JsonObject
                                {
                                    ["context"] = "tgt",
                                    ["transform"] = "cp",
                                    ["parameter"] = new JsonArray { new JsonObject { ["valueId"] = "v" } }
                                }
                            }
                        },
                        new JsonObject
                        {
                            ["name"] = "referenceTransform",
                            ["source"] = new JsonArray { new JsonObject { ["context"] = "src", ["variable"] = "v" } },
                            ["target"] = new JsonArray
                            {
                                new JsonObject
                                {
                                    ["context"] = "tgt",
                                    ["transform"] = "reference",
                                    ["parameter"] = new JsonArray { new JsonObject { ["valueId"] = "v" } }
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
        var rules = ast.Groups[0].Rules;
        rules.Count.ShouldBe(9);

        // Verify each transform type
        ((TransformExpression)rules[0].Targets[0].Transform!).FunctionName.ShouldBe("copy");
        ((TransformExpression)rules[1].Targets[0].Transform!).FunctionName.ShouldBe("create");
        ((TransformExpression)rules[2].Targets[0].Transform!).FunctionName.ShouldBe("evaluate");
        ((TransformExpression)rules[3].Targets[0].Transform!).FunctionName.ShouldBe("translate");
        ((TransformExpression)rules[4].Targets[0].Transform!).FunctionName.ShouldBe("cc");
        ((TransformExpression)rules[5].Targets[0].Transform!).FunctionName.ShouldBe("qty");
        ((TransformExpression)rules[6].Targets[0].Transform!).FunctionName.ShouldBe("id");
        ((TransformExpression)rules[7].Targets[0].Transform!).FunctionName.ShouldBe("cp");
        ((TransformExpression)rules[8].Targets[0].Transform!).FunctionName.ShouldBe("reference");

        // Verify parameter counts
        ((TransformExpression)rules[0].Targets[0].Transform!).Arguments.Count.ShouldBe(1);
        ((TransformExpression)rules[1].Targets[0].Transform!).Arguments.Count.ShouldBe(1);
        ((TransformExpression)rules[2].Targets[0].Transform!).Arguments.Count.ShouldBe(2);
        ((TransformExpression)rules[3].Targets[0].Transform!).Arguments.Count.ShouldBe(3);
        ((TransformExpression)rules[4].Targets[0].Transform!).Arguments.Count.ShouldBe(2);
        ((TransformExpression)rules[5].Targets[0].Transform!).Arguments.Count.ShouldBe(2);

        // Verify parameter types for qty transform
        ((TransformExpression)rules[5].Targets[0].Transform!).Arguments[0].ShouldBeOfType<LiteralExpression>();
        ((LiteralExpression)((TransformExpression)rules[5].Targets[0].Transform!).Arguments[0]).Value.ShouldBe(100);
    }

    [Fact]
    public void GivenMultipleTransformsInSameRule_WhenParsing_ThenParsesAllTargets()
    {
        // Arrange - Single rule with multiple target transforms
        var structureMapJson = new JsonObject
        {
            ["resourceType"] = "StructureMap",
            ["url"] = "http://example.org/test-multi-transform",
            ["name"] = "TestMultiTransform",
            ["status"] = "draft",
            ["group"] = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = "Transform",
                    ["typeMode"] = "none",
                    ["input"] = new JsonArray
                    {
                        new JsonObject { ["name"] = "src", ["mode"] = "source" },
                        new JsonObject { ["name"] = "tgt", ["mode"] = "target" }
                    },
                    ["rule"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["name"] = "multiTarget",
                            ["source"] = new JsonArray
                            {
                                new JsonObject
                                {
                                    ["context"] = "src",
                                    ["element"] = "name",
                                    ["variable"] = "vName"
                                }
                            },
                            ["target"] = new JsonArray
                            {
                                new JsonObject
                                {
                                    ["context"] = "tgt",
                                    ["element"] = "id",
                                    ["transform"] = "uuid"
                                },
                                new JsonObject
                                {
                                    ["context"] = "tgt",
                                    ["element"] = "name",
                                    ["transform"] = "copy",
                                    ["parameter"] = new JsonArray
                                    {
                                        new JsonObject { ["valueId"] = "vName" }
                                    }
                                },
                                new JsonObject
                                {
                                    ["context"] = "tgt",
                                    ["element"] = "meta",
                                    ["variable"] = "vMeta",
                                    ["transform"] = "create",
                                    ["parameter"] = new JsonArray
                                    {
                                        new JsonObject { ["valueString"] = "Meta" }
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
        var targets = ast.Groups[0].Rules[0].Targets;
        targets.Count.ShouldBe(3);
        ((TransformExpression)targets[0].Transform!).FunctionName.ShouldBe("uuid");
        ((TransformExpression)targets[1].Transform!).FunctionName.ShouldBe("copy");
        ((TransformExpression)targets[2].Transform!).FunctionName.ShouldBe("create");
        targets[2].Variable.ShouldBe("vMeta");
    }

    #endregion

    #region MEDIUM Priority Edge Cases

    [Fact]
    public void GivenSourceWithAllOptionalFieldsCombined_WhenParsing_ThenParsesAllFields()
    {
        // Arrange - Source with condition + check + log + defaultValue
        var structureMapJson = new JsonObject
        {
            ["resourceType"] = "StructureMap",
            ["url"] = "http://example.org/test-source-full",
            ["name"] = "TestSourceFull",
            ["status"] = "draft",
            ["group"] = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = "Transform",
                    ["typeMode"] = "none",
                    ["input"] = new JsonArray
                    {
                        new JsonObject { ["name"] = "src", ["mode"] = "source" }
                    },
                    ["rule"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["name"] = "complexSource",
                            ["source"] = new JsonArray
                            {
                                new JsonObject
                                {
                                    ["context"] = "src",
                                    ["element"] = "name",
                                    ["variable"] = "vName",
                                    ["type"] = "HumanName",
                                    ["min"] = 1,
                                    ["max"] = "1",
                                    ["condition"] = "family.exists()",
                                    ["check"] = "given.count() > 0",
                                    ["logMessage"] = "Processing name",
                                    ["defaultValueString"] = "Unknown"
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
        var source = ast.Groups[0].Rules[0].Sources[0];
        source.Variable.ShouldBe("vName");
        source.Type.ShouldBe("HumanName");
        source.Cardinality.ShouldNotBeNull();
        source.Cardinality!.Min.ShouldBe(1);
        source.Cardinality.Max.ShouldBe(1);
        source.Condition.ShouldNotBeNull();
        source.Condition.ShouldBeOfType<FhirPathExpression>();
        ((FhirPathExpression)source.Condition!).ToString().ShouldContain("family.exists()");
        source.Check.ShouldNotBeNull();
        source.Check.ShouldBeOfType<FhirPathExpression>();
        source.Log.ShouldNotBeNull();
        source.Log.ShouldBeOfType<LiteralExpression>();
        ((LiteralExpression)source.Log!).Value.ShouldBe("Processing name");
        source.Default.ShouldNotBeNull();
        source.Default.ShouldBeOfType<LiteralExpression>();
        ((LiteralExpression)source.Default!).Value.ShouldBe("Unknown");
    }

    [Fact]
    public void GivenDeeplyNestedRules_WhenParsing_ThenParsesAllLevels()
    {
        // Arrange - Rules with dependent rules that have their own dependent rules (3 levels)
        var structureMapJson = new JsonObject
        {
            ["resourceType"] = "StructureMap",
            ["url"] = "http://example.org/test-deep-nesting",
            ["name"] = "TestDeepNesting",
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
                            ["name"] = "level1",
                            ["source"] = new JsonArray
                            {
                                new JsonObject { ["context"] = "src" }
                            },
                            ["rule"] = new JsonArray
                            {
                                new JsonObject
                                {
                                    ["name"] = "level2",
                                    ["source"] = new JsonArray
                                    {
                                        new JsonObject { ["context"] = "src" }
                                    },
                                    ["rule"] = new JsonArray
                                    {
                                        new JsonObject
                                        {
                                            ["name"] = "level3",
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
        ast.Groups[0].Rules[0].Name.ShouldBe("level1");
        ast.Groups[0].Rules[0].Dependent.ShouldNotBeNull();
        ast.Groups[0].Rules[0].Dependent.ShouldBeOfType<RuleSetExpression>();

        var level2 = (RuleSetExpression)ast.Groups[0].Rules[0].Dependent!;
        level2.Rules.Count.ShouldBe(1);
        level2.Rules[0].Name.ShouldBe("level2");
        level2.Rules[0].Dependent.ShouldNotBeNull();
        level2.Rules[0].Dependent.ShouldBeOfType<RuleSetExpression>();

        var level3 = (RuleSetExpression)level2.Rules[0].Dependent!;
        level3.Rules.Count.ShouldBe(1);
        level3.Rules[0].Name.ShouldBe("level3");
    }

    [Fact]
    public void GivenMultipleListModesOnSameTarget_WhenParsing_ThenParsesFirstMode()
    {
        // Arrange - Target with multiple list modes (parser uses first)
        var structureMapJson = new JsonObject
        {
            ["resourceType"] = "StructureMap",
            ["url"] = "http://example.org/test-multi-listmode",
            ["name"] = "TestMultiListMode",
            ["status"] = "draft",
            ["group"] = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = "Transform",
                    ["typeMode"] = "none",
                    ["input"] = new JsonArray
                    {
                        new JsonObject { ["name"] = "src", ["mode"] = "source" },
                        new JsonObject { ["name"] = "tgt", ["mode"] = "target" }
                    },
                    ["rule"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["name"] = "listModeRule",
                            ["source"] = new JsonArray
                            {
                                new JsonObject { ["context"] = "src" }
                            },
                            ["target"] = new JsonArray
                            {
                                new JsonObject
                                {
                                    ["context"] = "tgt",
                                    ["element"] = "name",
                                    ["listMode"] = new JsonArray { "first", "share" }
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
        var target = ast.Groups[0].Rules[0].Targets[0];
        target.ListMode.ShouldNotBeNull();
        target.ListMode.ShouldBe(ListMode.First);
    }

    [Fact]
    public void GivenAllListModeVariants_WhenParsing_ThenParsesCorrectly()
    {
        // Arrange - Test all list mode values
        var structureMapJson = new JsonObject
        {
            ["resourceType"] = "StructureMap",
            ["url"] = "http://example.org/test-all-listmodes",
            ["name"] = "TestAllListModes",
            ["status"] = "draft",
            ["group"] = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = "Transform",
                    ["typeMode"] = "none",
                    ["input"] = new JsonArray
                    {
                        new JsonObject { ["name"] = "src", ["mode"] = "source" },
                        new JsonObject { ["name"] = "tgt", ["mode"] = "target" }
                    },
                    ["rule"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["name"] = "firstMode",
                            ["source"] = new JsonArray { new JsonObject { ["context"] = "src" } },
                            ["target"] = new JsonArray
                            {
                                new JsonObject
                                {
                                    ["context"] = "tgt",
                                    ["listMode"] = new JsonArray { "first" }
                                }
                            }
                        },
                        new JsonObject
                        {
                            ["name"] = "lastMode",
                            ["source"] = new JsonArray { new JsonObject { ["context"] = "src" } },
                            ["target"] = new JsonArray
                            {
                                new JsonObject
                                {
                                    ["context"] = "tgt",
                                    ["listMode"] = new JsonArray { "last" }
                                }
                            }
                        },
                        new JsonObject
                        {
                            ["name"] = "shareMode",
                            ["source"] = new JsonArray { new JsonObject { ["context"] = "src" } },
                            ["target"] = new JsonArray
                            {
                                new JsonObject
                                {
                                    ["context"] = "tgt",
                                    ["listMode"] = new JsonArray { "share" }
                                }
                            }
                        },
                        new JsonObject
                        {
                            ["name"] = "singleMode",
                            ["source"] = new JsonArray { new JsonObject { ["context"] = "src" } },
                            ["target"] = new JsonArray
                            {
                                new JsonObject
                                {
                                    ["context"] = "tgt",
                                    ["listMode"] = new JsonArray { "single" }
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
        var rules = ast.Groups[0].Rules;
        rules[0].Targets[0].ListMode.ShouldBe(ListMode.First);
        rules[1].Targets[0].ListMode.ShouldBe(ListMode.Last);
        rules[2].Targets[0].ListMode.ShouldBe(ListMode.Share);
        rules[3].Targets[0].ListMode.ShouldBe(ListMode.Single);
    }

    [Fact]
    public void GivenEmptyParameterArray_WhenParsing_ThenParsesEmptyArguments()
    {
        // Arrange - Transform with empty parameter array
        var structureMapJson = new JsonObject
        {
            ["resourceType"] = "StructureMap",
            ["url"] = "http://example.org/test-empty-params",
            ["name"] = "TestEmptyParams",
            ["status"] = "draft",
            ["group"] = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = "Transform",
                    ["typeMode"] = "none",
                    ["input"] = new JsonArray
                    {
                        new JsonObject { ["name"] = "src", ["mode"] = "source" },
                        new JsonObject { ["name"] = "tgt", ["mode"] = "target" }
                    },
                    ["rule"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["name"] = "emptyParamRule",
                            ["source"] = new JsonArray { new JsonObject { ["context"] = "src" } },
                            ["target"] = new JsonArray
                            {
                                new JsonObject
                                {
                                    ["context"] = "tgt",
                                    ["transform"] = "uuid",
                                    ["parameter"] = new JsonArray()
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
        var transform = (TransformExpression)ast.Groups[0].Rules[0].Targets[0].Transform!;
        transform.FunctionName.ShouldBe("uuid");
        transform.Arguments.ShouldBeEmpty();
    }

    [Fact]
    public void GivenTargetWithOnlyContext_WhenParsing_ThenCreatesMinimalTarget()
    {
        // Arrange - Target with only context, no element/variable/transform
        var structureMapJson = new JsonObject
        {
            ["resourceType"] = "StructureMap",
            ["url"] = "http://example.org/test-minimal-target",
            ["name"] = "TestMinimalTarget",
            ["status"] = "draft",
            ["group"] = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = "Transform",
                    ["typeMode"] = "none",
                    ["input"] = new JsonArray
                    {
                        new JsonObject { ["name"] = "src", ["mode"] = "source" },
                        new JsonObject { ["name"] = "tgt", ["mode"] = "target" }
                    },
                    ["rule"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["name"] = "minimalTarget",
                            ["source"] = new JsonArray
                            {
                                new JsonObject { ["context"] = "src" }
                            },
                            ["target"] = new JsonArray
                            {
                                new JsonObject
                                {
                                    ["context"] = "tgt"
                                    // No element, variable, or transform
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
        var target = ast.Groups[0].Rules[0].Targets[0];
        target.Context.ShouldNotBeNull();
        target.Context.ShouldBeOfType<IdentifierExpression>();
        ((IdentifierExpression)target.Context!).Name.ShouldBe("tgt");
        target.Variable.ShouldBeNull();
        target.Transform.ShouldBeNull();
        target.ListMode.ShouldBeNull();
    }

    [Fact]
    public void GivenMixedParameterTypes_WhenParsing_ThenParsesAllTypes()
    {
        // Arrange - Transform with mixed parameter types (string, integer, boolean, id)
        var structureMapJson = new JsonObject
        {
            ["resourceType"] = "StructureMap",
            ["url"] = "http://example.org/test-mixed-params",
            ["name"] = "TestMixedParams",
            ["status"] = "draft",
            ["group"] = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = "Transform",
                    ["typeMode"] = "none",
                    ["input"] = new JsonArray
                    {
                        new JsonObject { ["name"] = "src", ["mode"] = "source" },
                        new JsonObject { ["name"] = "tgt", ["mode"] = "target" }
                    },
                    ["rule"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["name"] = "mixedParams",
                            ["source"] = new JsonArray { new JsonObject { ["context"] = "src" } },
                            ["target"] = new JsonArray
                            {
                                new JsonObject
                                {
                                    ["context"] = "tgt",
                                    ["transform"] = "evaluate",
                                    ["parameter"] = new JsonArray
                                    {
                                        new JsonObject { ["valueString"] = "textValue" },
                                        new JsonObject { ["valueInteger"] = 42 },
                                        new JsonObject { ["valueBoolean"] = true },
                                        new JsonObject { ["valueId"] = "varName" }
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
        var transform = (TransformExpression)ast.Groups[0].Rules[0].Targets[0].Transform!;
        transform.Arguments.Count.ShouldBe(4);
        transform.Arguments[0].ShouldBeOfType<LiteralExpression>();
        ((LiteralExpression)transform.Arguments[0]).Value.ShouldBe("textValue");
        transform.Arguments[1].ShouldBeOfType<LiteralExpression>();
        ((LiteralExpression)transform.Arguments[1]).Value.ShouldBe(42);
        transform.Arguments[2].ShouldBeOfType<LiteralExpression>();
        ((LiteralExpression)transform.Arguments[2]).Value.ShouldBe(true);
        transform.Arguments[3].ShouldBeOfType<IdentifierExpression>();
        ((IdentifierExpression)transform.Arguments[3]).Name.ShouldBe("varName");
    }

    [Fact]
    public void GivenCardinalityWithUnboundedMax_WhenParsing_ThenParsesMaxAsNull()
    {
        // Arrange - Source with max = "*" (unbounded)
        var structureMapJson = new JsonObject
        {
            ["resourceType"] = "StructureMap",
            ["url"] = "http://example.org/test-unbounded",
            ["name"] = "TestUnbounded",
            ["status"] = "draft",
            ["group"] = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = "Transform",
                    ["typeMode"] = "none",
                    ["input"] = new JsonArray
                    {
                        new JsonObject { ["name"] = "src", ["mode"] = "source" }
                    },
                    ["rule"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["name"] = "unboundedRule",
                            ["source"] = new JsonArray
                            {
                                new JsonObject
                                {
                                    ["context"] = "src",
                                    ["element"] = "name",
                                    ["variable"] = "v",
                                    ["min"] = 0,
                                    ["max"] = "*"
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
        var source = ast.Groups[0].Rules[0].Sources[0];
        source.Cardinality.ShouldNotBeNull();
        source.Cardinality!.Min.ShouldBe(0);
        source.Cardinality.Max.ShouldBeNull(); // "*" means unbounded (null)
    }

    #endregion
}
