/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Unit tests for SqlOnFhirSchemaEvaluator.
 * Tests schema extraction from ViewDefinitions using official SQL-on-FHIR v2 test fixtures.
 */

using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;
using Ignixa.SqlOnFhir.Evaluation;
using Ignixa.SqlOnFhir.Expressions;
using Ignixa.SqlOnFhir.Models;
using Ignixa.SqlOnFhir.Parsing;

namespace Ignixa.SqlOnFhir.Tests;

/// <summary>
/// Unit tests for SqlOnFhirSchemaEvaluator.
/// Tests extraction of output column schema (names, types, collection flags) from ViewDefinitions.
/// </summary>
public class SqlOnFhirSchemaEvaluatorTests
{
    private readonly SqlOnFhirSchemaEvaluator _evaluator = new();
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    #region Simple Column Tests

    [Fact]
    public void GivenSingleColumn_WhenExtractingSchema_ThenReturnsOneColumn()
    {
        // Arrange
        var viewDef = new ViewDefinition
        {
            Resource = "Patient",
            Select = new List<SelectGroup>
            {
                new SelectGroup
                {
                    Column = new List<ViewColumnDefinition>
                    {
                        new ViewColumnDefinition { Name = "id", Path = "id", Type = "id" }
                    }
                }
            }
        };

        // Act
        var schema = _evaluator.GetSchema(ParseViewDefinition(viewDef));

        // Assert
        Assert.Single(schema);
        Assert.Equal("id", schema[0].Name);
        Assert.Equal("id", schema[0].Type);
        Assert.False(schema[0].Collection);
    }

    [Fact]
    public void GivenMultipleColumns_WhenExtractingSchema_ThenReturnsAllColumns()
    {
        // Arrange
        var viewDef = new ViewDefinition
        {
            Resource = "Patient",
            Select = new List<SelectGroup>
            {
                new SelectGroup
                {
                    Column = new List<ViewColumnDefinition>
                    {
                        new ViewColumnDefinition { Name = "id", Path = "id", Type = "id" },
                        new ViewColumnDefinition { Name = "active", Path = "active", Type = "boolean" },
                        new ViewColumnDefinition { Name = "birthDate", Path = "birthDate", Type = "date" }
                    }
                }
            }
        };

        // Act
        var schema = _evaluator.GetSchema(ParseViewDefinition(viewDef));

        // Assert
        Assert.Equal(3, schema.Count);
        Assert.Equal("id", schema[0].Name);
        Assert.Equal("id", schema[0].Type);
        Assert.Equal("active", schema[1].Name);
        Assert.Equal("boolean", schema[1].Type);
        Assert.Equal("birthDate", schema[2].Name);
        Assert.Equal("date", schema[2].Type);
    }

    [Fact]
    public void GivenColumnWithCollectionFlag_WhenExtractingSchema_ThenReturnsCollectionTrue()
    {
        // Arrange
        var viewDef = new ViewDefinition
        {
            Resource = "Patient",
            Select = new List<SelectGroup>
            {
                new SelectGroup
                {
                    Column = new List<ViewColumnDefinition>
                    {
                        new ViewColumnDefinition
                        {
                            Name = "identifier_value",
                            Path = "identifier.value",
                            Type = "string",
                            Collection = true
                        }
                    }
                }
            }
        };

        // Act
        var schema = _evaluator.GetSchema(ParseViewDefinition(viewDef));

        // Assert
        Assert.Single(schema);
        Assert.Equal("identifier_value", schema[0].Name);
        Assert.Equal("string", schema[0].Type);
        Assert.True(schema[0].Collection);
    }

    [Fact]
    public void GivenColumnWithoutType_WhenExtractingSchema_ThenReturnsNullType()
    {
        // Arrange
        var viewDef = new ViewDefinition
        {
            Resource = "Patient",
            Select = new List<SelectGroup>
            {
                new SelectGroup
                {
                    Column = new List<ViewColumnDefinition>
                    {
                        new ViewColumnDefinition { Name = "custom", Path = "extension.value" }
                    }
                }
            }
        };

        // Act
        var schema = _evaluator.GetSchema(ParseViewDefinition(viewDef));

        // Assert
        Assert.Single(schema);
        Assert.Equal("custom", schema[0].Name);
        Assert.Null(schema[0].Type);
        Assert.False(schema[0].Collection);
    }

    #endregion

    [Fact]
    public void GivenColumnWithTags_WhenExtractingSchema_ThenTagsIncludedInSchema()
    {
        // Arrange
        var viewDef = new ViewDefinition
        {
            Resource = "Patient",
            Select = new List<SelectGroup>
            {
                new SelectGroup
                {
                    Column = new List<ViewColumnDefinition>
                    {
                        new ViewColumnDefinition
                        {
                            Name = "id",
                            Path = "id",
                            Type = "id",
                            Tag = new List<ColumnTag>
                            {
                                new ColumnTag { Name = "ansi/type", Value = "VARCHAR(64)" },
                                new ColumnTag { Name = "custom/indexed", Value = "true" }
                            }
                        }
                    }
                }
            }
        };

        // Act
        var schema = _evaluator.GetSchema(ParseViewDefinition(viewDef));

        // Assert
        Assert.Single(schema);
        Assert.NotNull(schema[0].Tags);
        var tags = schema[0].Tags!;
        Assert.Equal(2, tags.Count);
        Assert.Equal("ansi/type", tags[0].Name);
        Assert.Equal("VARCHAR(64)", tags[0].Value);
        Assert.Equal("custom/indexed", tags[1].Name);
        Assert.Equal("true", tags[1].Value);
    }

    [Fact]
    public void GivenColumnWithNoTags_WhenExtractingSchema_ThenTagsIsNull()
    {
        // Arrange
        var viewDef = new ViewDefinition
        {
            Resource = "Patient",
            Select = new List<SelectGroup>
            {
                new SelectGroup
                {
                    Column = new List<ViewColumnDefinition>
                    {
                        new ViewColumnDefinition { Name = "id", Path = "id", Type = "id" }
                    }
                }
            }
        };

        // Act
        var schema = _evaluator.GetSchema(ParseViewDefinition(viewDef));

        // Assert
        Assert.Single(schema);
        Assert.Null(schema[0].Tags);
    }

    [Fact]
    public void GivenColumnTagWithMissingValue_WhenParsing_ThenThrows()
    {
        var json = """
            {
              "resource": "Patient",
              "select": [{
                "column": [{
                  "name": "id",
                  "path": "id",
                  "tag": [{ "name": "ansi/type" }]
                }]
              }]
            }
            """;

        Assert.Throws<InvalidOperationException>(() => ParseViewDefinitionJson(json));
    }

    [Fact]
    public void GivenColumnTagWithEmptyName_WhenParsing_ThenThrows()
    {
        var json = """
            {
              "resource": "Patient",
              "select": [{
                "column": [{
                  "name": "id",
                  "path": "id",
                  "tag": [{ "name": "", "value": "VARCHAR(64)" }]
                }]
              }]
            }
            """;

        Assert.Throws<InvalidOperationException>(() => ParseViewDefinitionJson(json));
    }

    #region Multiple SELECT Groups Tests

    [Fact]
    public void GivenMultipleSelectGroups_WhenExtractingSchema_ThenUnionsAllColumns()
    {
        // Arrange
        var viewDef = new ViewDefinition
        {
            Resource = "Patient",
            Select = new List<SelectGroup>
            {
                new SelectGroup
                {
                    Column = new List<ViewColumnDefinition>
                    {
                        new ViewColumnDefinition { Name = "id", Path = "id", Type = "id" }
                    }
                },
                new SelectGroup
                {
                    ForEach = "name",
                    Column = new List<ViewColumnDefinition>
                    {
                        new ViewColumnDefinition { Name = "family", Path = "family", Type = "string" }
                    }
                }
            }
        };

        // Act
        var schema = _evaluator.GetSchema(ParseViewDefinition(viewDef));

        // Assert
        Assert.Equal(2, schema.Count);
        Assert.Equal("id", schema[0].Name);
        Assert.Equal("family", schema[1].Name);
    }

    [Fact]
    public void GivenMultipleSelectGroupsWithDuplicateColumns_WhenExtractingSchema_ThenReturnsUniqueColumns()
    {
        // Arrange
        var viewDef = new ViewDefinition
        {
            Resource = "Patient",
            Select = new List<SelectGroup>
            {
                new SelectGroup
                {
                    Column = new List<ViewColumnDefinition>
                    {
                        new ViewColumnDefinition { Name = "id", Path = "id", Type = "id" }
                    }
                },
                new SelectGroup
                {
                    Column = new List<ViewColumnDefinition>
                    {
                        new ViewColumnDefinition { Name = "id", Path = "id", Type = "id" },
                        new ViewColumnDefinition { Name = "active", Path = "active", Type = "boolean" }
                    }
                }
            }
        };

        // Act
        var schema = _evaluator.GetSchema(ParseViewDefinition(viewDef));

        // Assert
        Assert.Equal(2, schema.Count);
        Assert.Equal("id", schema[0].Name);
        Assert.Equal("active", schema[1].Name);
    }

    #endregion

    #region ForEach/ForEachOrNull Tests

    [Fact]
    public void GivenForEach_WhenExtractingSchema_ThenReturnsColumnsFromForEachSelect()
    {
        // Arrange
        var viewDef = new ViewDefinition
        {
            Resource = "Patient",
            Select = new List<SelectGroup>
            {
                new SelectGroup
                {
                    ForEach = "name",
                    Column = new List<ViewColumnDefinition>
                    {
                        new ViewColumnDefinition { Name = "family", Path = "family", Type = "string" },
                        new ViewColumnDefinition { Name = "given", Path = "given.first()", Type = "string" }
                    }
                }
            }
        };

        // Act
        var schema = _evaluator.GetSchema(ParseViewDefinition(viewDef));

        // Assert
        Assert.Equal(2, schema.Count);
        Assert.Equal("family", schema[0].Name);
        Assert.Equal("given", schema[1].Name);
    }

    [Fact]
    public void GivenForEachOrNull_WhenExtractingSchema_ThenReturnsSameColumnsAsForEach()
    {
        // Arrange
        var viewDefForEach = new ViewDefinition
        {
            Resource = "Patient",
            Select = new List<SelectGroup>
            {
                new SelectGroup
                {
                    ForEach = "name",
                    Column = new List<ViewColumnDefinition>
                    {
                        new ViewColumnDefinition { Name = "family", Path = "family", Type = "string" }
                    }
                }
            }
        };

        var viewDefForEachOrNull = new ViewDefinition
        {
            Resource = "Patient",
            Select = new List<SelectGroup>
            {
                new SelectGroup
                {
                    ForEachOrNull = "name",
                    Column = new List<ViewColumnDefinition>
                    {
                        new ViewColumnDefinition { Name = "family", Path = "family", Type = "string" }
                    }
                }
            }
        };

        // Act
        var schemaForEach = _evaluator.GetSchema(ParseViewDefinition(viewDefForEach));
        var schemaForEachOrNull = _evaluator.GetSchema(ParseViewDefinition(viewDefForEachOrNull));

        // Assert
        Assert.Equal(schemaForEach.Count, schemaForEachOrNull.Count);
        Assert.Equal(schemaForEach[0].Name, schemaForEachOrNull[0].Name);
        Assert.Equal(schemaForEach[0].Type, schemaForEachOrNull[0].Type);
    }

    #endregion

    #region Nested Select Tests

    [Fact]
    public void GivenNestedSelect_WhenExtractingSchema_ThenIncludesColumnsFromBothLevels()
    {
        // Arrange - nested select uses JSON since model doesn't support it
        var viewJson = @"{
            ""resource"": ""Patient"",
            ""select"": [
                {
                    ""column"": [
                        {
                            ""name"": ""id"",
                            ""path"": ""id"",
                            ""type"": ""id""
                        }
                    ],
                    ""select"": [
                        {
                            ""forEach"": ""name"",
                            ""column"": [
                                {
                                    ""name"": ""family"",
                                    ""path"": ""family"",
                                    ""type"": ""string""
                                }
                            ]
                        }
                    ]
                }
            ]
        }";

        // Act
        var schema = _evaluator.GetSchema(ParseViewDefinitionJson(viewJson));

        // Assert
        Assert.Equal(2, schema.Count);
        Assert.Equal("id", schema[0].Name);
        Assert.Equal("family", schema[1].Name);
    }

    [Fact]
    public void GivenDeeplyNestedSelect_WhenExtractingSchema_ThenIncludesAllLevels()
    {
        // Arrange - deeply nested select uses JSON since model doesn't support it
        var viewJson = @"{
            ""resource"": ""Patient"",
            ""select"": [
                {
                    ""column"": [
                        {
                            ""name"": ""id"",
                            ""path"": ""id"",
                            ""type"": ""id""
                        }
                    ],
                    ""select"": [
                        {
                            ""forEach"": ""name"",
                            ""column"": [
                                {
                                    ""name"": ""family"",
                                    ""path"": ""family"",
                                    ""type"": ""string""
                                }
                            ],
                            ""select"": [
                                {
                                    ""forEach"": ""given"",
                                    ""column"": [
                                        {
                                            ""name"": ""given"",
                                            ""path"": ""$this"",
                                            ""type"": ""string""
                                        }
                                    ]
                                }
                            ]
                        }
                    ]
                }
            ]
        }";

        // Act
        var schema = _evaluator.GetSchema(ParseViewDefinitionJson(viewJson));

        // Assert
        Assert.Equal(3, schema.Count);
        Assert.Equal("id", schema[0].Name);
        Assert.Equal("family", schema[1].Name);
        Assert.Equal("given", schema[2].Name);
    }

    #endregion

    #region UnionAll Tests

    [Fact]
    public void GivenUnionAll_WhenExtractingSchema_ThenMergesColumnsFromAllBranches()
    {
        // Arrange - unionAll uses JSON since model doesn't support it
        var viewJson = @"{
            ""resource"": ""Patient"",
            ""select"": [
                {
                    ""unionAll"": [
                        {
                            ""column"": [
                                {
                                    ""name"": ""system"",
                                    ""path"": ""system"",
                                    ""type"": ""uri""
                                },
                                {
                                    ""name"": ""value"",
                                    ""path"": ""value"",
                                    ""type"": ""string""
                                }
                            ]
                        },
                        {
                            ""column"": [
                                {
                                    ""name"": ""system"",
                                    ""path"": ""'http://custom'"",
                                    ""type"": ""uri""
                                },
                                {
                                    ""name"": ""value"",
                                    ""path"": ""id"",
                                    ""type"": ""string""
                                }
                            ]
                        }
                    ]
                }
            ]
        }";

        // Act
        var schema = _evaluator.GetSchema(ParseViewDefinitionJson(viewJson));

        // Assert
        Assert.Equal(2, schema.Count);
        Assert.Equal("system", schema[0].Name);
        Assert.Equal("value", schema[1].Name);
    }

    [Fact]
    public void GivenUnionAllWithAdditionalColumns_WhenExtractingSchema_ThenIncludesAllUniqueColumns()
    {
        // Arrange - unionAll uses JSON since model doesn't support it
        var viewJson = @"{
            ""resource"": ""Patient"",
            ""select"": [
                {
                    ""unionAll"": [
                        {
                            ""column"": [
                                {
                                    ""name"": ""id"",
                                    ""path"": ""id"",
                                    ""type"": ""id""
                                },
                                {
                                    ""name"": ""type"",
                                    ""path"": ""'patient'"",
                                    ""type"": ""string""
                                }
                            ]
                        },
                        {
                            ""column"": [
                                {
                                    ""name"": ""id"",
                                    ""path"": ""identifier.value.first()"",
                                    ""type"": ""id""
                                },
                                {
                                    ""name"": ""type"",
                                    ""path"": ""'identifier'"",
                                    ""type"": ""string""
                                }
                            ]
                        }
                    ]
                }
            ]
        }";

        // Act
        var schema = _evaluator.GetSchema(ParseViewDefinitionJson(viewJson));

        // Assert
        Assert.Equal(2, schema.Count);
        Assert.Equal("id", schema[0].Name);
        Assert.Equal("type", schema[1].Name);
    }

    #endregion

    #region Official Test Fixtures

    [Fact]
    public void GivenBasicViewFromOfficialTests_WhenExtractingSchema_ThenReturnsCorrectColumns()
    {
        // Arrange - from basic.json test
        var viewJson = @"{
            ""resource"": ""Patient"",
            ""status"": ""active"",
            ""select"": [
                {
                    ""column"": [
                        {
                            ""name"": ""id"",
                            ""path"": ""id"",
                            ""type"": ""id""
                        }
                    ]
                }
            ]
        }";

        // Act
        var schema = _evaluator.GetSchema(ParseViewDefinitionJson(viewJson));

        // Assert
        Assert.Single(schema);
        Assert.Equal("id", schema[0].Name);
        Assert.Equal("id", schema[0].Type);
    }

    [Fact]
    public void GivenForEachViewFromOfficialTests_WhenExtractingSchema_ThenReturnsCorrectColumns()
    {
        // Arrange - from foreach.json test (simplified)
        var viewJson = @"{
            ""resource"": ""Patient"",
            ""select"": [
                {
                    ""column"": [
                        {
                            ""name"": ""id"",
                            ""path"": ""id""
                        }
                    ]
                },
                {
                    ""forEach"": ""name"",
                    ""column"": [
                        {
                            ""name"": ""family"",
                            ""path"": ""family""
                        }
                    ]
                }
            ]
        }";

        // Act
        var schema = _evaluator.GetSchema(ParseViewDefinitionJson(viewJson));

        // Assert
        Assert.Equal(2, schema.Count);
        Assert.Equal("id", schema[0].Name);
        Assert.Equal("family", schema[1].Name);
    }

    [Fact]
    public void GivenCollectionViewFromOfficialTests_WhenExtractingSchema_ThenReturnsCollectionColumns()
    {
        // Arrange - from collection.json test
        var viewJson = @"{
            ""resource"": ""Patient"",
            ""select"": [
                {
                    ""column"": [
                        {
                            ""name"": ""id"",
                            ""path"": ""id"",
                            ""type"": ""id""
                        },
                        {
                            ""name"": ""names"",
                            ""path"": ""name.family"",
                            ""collection"": true
                        }
                    ]
                }
            ]
        }";

        // Act
        var schema = _evaluator.GetSchema(ParseViewDefinitionJson(viewJson));

        // Assert
        Assert.Equal(2, schema.Count);
        Assert.Equal("id", schema[0].Name);
        Assert.False(schema[0].Collection);
        Assert.Equal("names", schema[1].Name);
        Assert.True(schema[1].Collection);
    }

    [Fact]
    public void GivenUnionViewFromOfficialTests_WhenExtractingSchema_ThenMergesUnionColumns()
    {
        // Arrange - from union.json test (simplified)
        var viewJson = @"{
            ""resource"": ""Patient"",
            ""select"": [
                {
                    ""unionAll"": [
                        {
                            ""forEach"": ""identifier"",
                            ""column"": [
                                {
                                    ""name"": ""system"",
                                    ""path"": ""system""
                                },
                                {
                                    ""name"": ""value"",
                                    ""path"": ""value""
                                }
                            ]
                        },
                        {
                            ""column"": [
                                {
                                    ""name"": ""system"",
                                    ""path"": ""'http://example.org/fhir/Patient'""
                                },
                                {
                                    ""name"": ""value"",
                                    ""path"": ""id""
                                }
                            ]
                        }
                    ]
                }
            ]
        }";

        // Act
        var schema = _evaluator.GetSchema(ParseViewDefinitionJson(viewJson));

        // Assert
        Assert.Equal(2, schema.Count);
        Assert.Equal("system", schema[0].Name);
        Assert.Equal("value", schema[1].Name);
    }

    [Fact]
    public void GivenComplexNestedViewFromOfficialTests_WhenExtractingSchema_ThenReturnsAllColumns()
    {
        // Arrange - complex nested structure
        var viewJson = @"{
            ""resource"": ""Patient"",
            ""select"": [
                {
                    ""column"": [
                        {
                            ""name"": ""patient_id"",
                            ""path"": ""id"",
                            ""type"": ""id""
                        }
                    ],
                    ""select"": [
                        {
                            ""forEach"": ""name"",
                            ""column"": [
                                {
                                    ""name"": ""family"",
                                    ""path"": ""family"",
                                    ""type"": ""string""
                                }
                            ],
                            ""select"": [
                                {
                                    ""forEach"": ""given"",
                                    ""column"": [
                                        {
                                            ""name"": ""given_name"",
                                            ""path"": ""$this"",
                                            ""type"": ""string""
                                        }
                                    ]
                                }
                            ]
                        }
                    ]
                }
            ]
        }";

        // Act
        var schema = _evaluator.GetSchema(ParseViewDefinitionJson(viewJson));

        // Assert
        Assert.Equal(3, schema.Count);
        Assert.Equal("patient_id", schema[0].Name);
        Assert.Equal("family", schema[1].Name);
        Assert.Equal("given_name", schema[2].Name);
    }

    #endregion

    #region Schema Caching Tests

    [Fact]
    public void GivenSameViewDefinition_WhenCalledMultipleTimes_ThenReturnsCachedSchema()
    {
        // Arrange
        var viewDef = new ViewDefinition
        {
            Resource = "Patient",
            Select = new List<SelectGroup>
            {
                new SelectGroup
                {
                    Column = new List<ViewColumnDefinition>
                    {
                        new ViewColumnDefinition { Name = "id", Path = "id", Type = "id" }
                    }
                }
            }
        };
        var viewExpression = ParseViewDefinition(viewDef);

        // Act
        var schema1 = _evaluator.GetSchema(viewExpression);
        var schema2 = _evaluator.GetSchema(viewExpression);

        // Assert - should return same instance (cached)
        Assert.Same(schema1, schema2);
    }

    [Fact]
    public void GivenClearedCache_WhenExtractingSchema_ThenRecomputesSchema()
    {
        // Arrange
        var viewDef = new ViewDefinition
        {
            Resource = "Patient",
            Select = new List<SelectGroup>
            {
                new SelectGroup
                {
                    Column = new List<ViewColumnDefinition>
                    {
                        new ViewColumnDefinition { Name = "id", Path = "id", Type = "id" }
                    }
                }
            }
        };
        var viewExpression = ParseViewDefinition(viewDef);

        // Act
        var schema1 = _evaluator.GetSchema(viewExpression);
        _evaluator.ClearCache();
        var schema2 = _evaluator.GetSchema(viewExpression);

        // Assert - should be equal but not same instance (recomputed)
        Assert.NotSame(schema1, schema2);
        Assert.Equal(schema1.Count, schema2.Count);
        Assert.Equal(schema1[0].Name, schema2[0].Name);
    }

    #endregion

    [Fact]
    public void GivenViewDefinitionWithFhirVersionAndProfile_WhenParsed_ThenModelAcceptsFields()
    {
        var viewDef = new ViewDefinition
        {
            Resource = "Patient",
            FhirVersion = new List<string> { "4.0.1", "5.0.0" },
            Profile = new List<string> { "http://hl7.org/fhir/StructureDefinition/Patient" },
            Where = new List<WhereClause>
            {
                new WhereClause { Path = "active = true", Description = "Only active patients" }
            },
            Select = new List<SelectGroup>
            {
                new SelectGroup
                {
                    Column = new List<ViewColumnDefinition>
                    {
                        new ViewColumnDefinition { Name = "id", Path = "id", Type = "id" }
                    }
                }
            }
        };

        var schema = _evaluator.GetSchema(ParseViewDefinition(viewDef));

        Assert.Single(schema);
        Assert.Equal("id", schema[0].Name);
    }

    #region Helper Methods

    /// <summary>
    /// Converts ViewDefinition model to ISourceNode and parses to ViewDefinitionExpression.
    /// </summary>
    private static ViewDefinitionExpression ParseViewDefinition(ViewDefinition viewDef)
    {
        var json = JsonSerializer.Serialize(viewDef, _jsonOptions);
        var jsonNode = JsonNode.Parse(json)!;
        var sourceNode = JsonNodeSourceNode.Create(jsonNode, "ViewDefinition");
        return ViewDefinitionExpressionParser.Parse(sourceNode);
    }

    /// <summary>
    /// Parses ViewDefinition from JSON string to ViewDefinitionExpression.
    /// </summary>
    private static ViewDefinitionExpression ParseViewDefinitionJson(string json)
    {
        var jsonNode = JsonNode.Parse(json)!;
        var sourceNode = JsonNodeSourceNode.Create(jsonNode, "ViewDefinition");
        return ViewDefinitionExpressionParser.Parse(sourceNode);
    }

    #endregion
}
