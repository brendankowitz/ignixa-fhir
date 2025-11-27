/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Unit tests for SqlOnFhirEvaluator.
 * Tests ViewDefinition evaluation with WHERE clauses, SELECT groups, and forEach semantics.
 */

using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Serialization;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Ignixa.Specification.Extensions;
using Ignixa.SqlOnFhir.Evaluation;
using Ignixa.SqlOnFhir.Models;

#pragma warning disable CS0618 // Type or member is obsolete - ISourceNavigator used for legacy tests

namespace Ignixa.SqlOnFhir.Tests;

/// <summary>
/// Unit tests for SqlOnFhirEvaluator.
/// Tests SQL on FHIR v2 ViewDefinition evaluation.
/// </summary>
public class SqlOnFhirEvaluatorTests
{
    private readonly SqlOnFhirEvaluator _evaluator = new();
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    private static readonly IFhirSchemaProvider _schemaProvider =
        FhirSpecificationExtensions.FromVersionString("4.0.1").GetSchemaProvider();

    #region Basic Column Evaluation Tests

    [Fact]
    public void GivenSimpleColumnPath_WhenEvaluated_ThenReturnsValue()
    {
        // Arrange
        var patientJson = new Dictionary<string, object?>
        {
            { "resourceType", "Patient" },
            { "id", "P001" }
        };
        var resource = CreateTypedElement(patientJson);

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
        var rows = _evaluator.Evaluate(ConvertToSourceNode(viewDef), resource).ToList();

        // Assert
        Assert.Single(rows);
        Assert.Equal("P001", rows[0]["id"]);
    }

    [Fact]
    public void GivenMultipleColumns_WhenEvaluated_ThenReturnsRowWithAllColumns()
    {
        // Arrange
        var patientJson = new Dictionary<string, object?>
        {
            { "resourceType", "Patient" },
            { "id", "P001" },
            { "active", true }
        };
        var resource = CreateTypedElement(patientJson);

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
                        new ViewColumnDefinition { Name = "active", Path = "active", Type = "boolean" }
                    }
                }
            }
        };

        // Act
        var rows = _evaluator.Evaluate(ConvertToSourceNode(viewDef), resource).ToList();

        // Assert
        Assert.Single(rows);
        Assert.Equal("P001", rows[0]["id"]);
        Assert.Equal(true, rows[0]["active"]);
    }

    [Fact]
    public void GivenMissingColumn_WhenEvaluated_ThenReturnsNull()
    {
        // Arrange
        var patientJson = new Dictionary<string, object?>
        {
            { "resourceType", "Patient" },
            { "id", "P001" }
        };
        var resource = CreateTypedElement(patientJson);

        var viewDef = new ViewDefinition
        {
            Resource = "Patient",
            Select = new List<SelectGroup>
            {
                new SelectGroup
                {
                    Column = new List<ViewColumnDefinition>
                    {
                        new ViewColumnDefinition { Name = "birthDate", Path = "birthDate", Type = "date" }
                    }
                }
            }
        };

        // Act
        var rows = _evaluator.Evaluate(ConvertToSourceNode(viewDef), resource).ToList();

        // Assert
        Assert.Single(rows);
        Assert.Null(rows[0]["birthDate"]);
    }

    #endregion

    #region Type Conversion Tests

    [Fact]
    public void GivenBooleanColumn_WhenEvaluated_ThenConvertsCorrectly()
    {
        // Arrange
        var patientJson = new Dictionary<string, object?>
        {
            { "resourceType", "Patient" },
            { "active", true }
        };
        var resource = CreateTypedElement(patientJson);

        var viewDef = new ViewDefinition
        {
            Resource = "Patient",
            Select = new List<SelectGroup>
            {
                new SelectGroup
                {
                    Column = new List<ViewColumnDefinition>
                    {
                        new ViewColumnDefinition { Name = "is_active", Path = "active", Type = "boolean" }
                    }
                }
            }
        };

        // Act
        var rows = _evaluator.Evaluate(ConvertToSourceNode(viewDef), resource).ToList();

        // Assert
        Assert.Single(rows);
        Assert.IsType<bool>(rows[0]["is_active"]);
        Assert.Equal(true, rows[0]["is_active"]);
    }

    [Fact]
    public void GivenIntegerType_WhenEvaluated_ThenConvertsCorrectly()
    {
        // Arrange
        var observationJson = new Dictionary<string, object?>
        {
            { "resourceType", "Observation" },
            { "value", 42 }
        };
        var resource = CreateTypedElement(observationJson);

        var viewDef = new ViewDefinition
        {
            Resource = "Observation",
            Select = new List<SelectGroup>
            {
                new SelectGroup
                {
                    Column = new List<ViewColumnDefinition>
                    {
                        new ViewColumnDefinition { Name = "value", Path = "value", Type = "integer" }
                    }
                }
            }
        };

        // Act
        var rows = _evaluator.Evaluate(ConvertToSourceNode(viewDef), resource).ToList();

        // Assert
        Assert.Single(rows);
        Assert.IsType<int>(rows[0]["value"]);
        Assert.Equal(42, rows[0]["value"]);
    }

    #endregion

    #region WHERE Clause Tests

    [Fact]
    public void GivenWhereClause_WhenEvaluated_ThenIncludesMatchingResource()
    {
        // Arrange
        var patientJson = new Dictionary<string, object?>
        {
            { "resourceType", "Patient" },
            { "id", "P001" },
            { "active", true }
        };
        var resource = CreateTypedElement(patientJson);

        var viewDef = new ViewDefinition
        {
            Resource = "Patient",
            Where = new List<WhereClause>
            {
                new WhereClause { Path = "active = true" }
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

        // Act
        var rows = _evaluator.Evaluate(ConvertToSourceNode(viewDef), resource).ToList();

        // Assert
        Assert.Single(rows);
        Assert.Equal("P001", rows[0]["id"]);
    }

    [Fact]
    public void GivenWhereClause_WhenEvaluated_ThenExcludesNonMatchingResource()
    {
        // Arrange
        var patientJson = new Dictionary<string, object?>
        {
            { "resourceType", "Patient" },
            { "id", "P001" },
            { "active", false }
        };
        var resource = CreateTypedElement(patientJson);

        var viewDef = new ViewDefinition
        {
            Resource = "Patient",
            Where = new List<WhereClause>
            {
                new WhereClause { Path = "active = true" }
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

        // Act
        var rows = _evaluator.Evaluate(ConvertToSourceNode(viewDef), resource).ToList();

        // Assert
        Assert.Empty(rows);
    }

    #endregion

    #region ForEach Array Unnesting Tests

    [Fact]
    public void GivenForEach_WhenEvaluated_ThenCreatesRowPerArrayElement()
    {
        // Arrange
        var patientJson = new Dictionary<string, object?>
        {
            { "resourceType", "Patient" },
            { "id", "P001" },
            { "name", new object[]
                {
                    new Dictionary<string, object?> { { "family", "Smith" } },
                    new Dictionary<string, object?> { { "family", "Doe" } }
                }
            }
        };
        var resource = CreateTypedElement(patientJson);

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
        var rows = _evaluator.Evaluate(ConvertToSourceNode(viewDef), resource).ToList();

        // Assert
        Assert.Equal(2, rows.Count);
        Assert.Equal("Smith", rows[0]["family"]);
        Assert.Equal("Doe", rows[1]["family"]);
    }

    [Fact]
    public void GivenEmptyForEach_WhenEvaluated_ThenSkipsResource()
    {
        // Arrange
        var patientJson = new Dictionary<string, object?>
        {
            { "resourceType", "Patient" },
            { "id", "P001" },
            { "name", Array.Empty<object>() }
        };
        var resource = CreateTypedElement(patientJson);

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
                        new ViewColumnDefinition { Name = "family", Path = "family", Type = "string" }
                    }
                }
            }
        };

        // Act
        var rows = _evaluator.Evaluate(ConvertToSourceNode(viewDef), resource).ToList();

        // Assert
        Assert.Empty(rows);
    }

    [Fact]
    public void GivenEmptyForEachOrNull_WhenEvaluated_ThenCreatesRowWithNull()
    {
        // Arrange
        var patientJson = new Dictionary<string, object?>
        {
            { "resourceType", "Patient" },
            { "id", "P001" },
            { "name", Array.Empty<object>() }
        };
        var resource = CreateTypedElement(patientJson);

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
                    ForEachOrNull = "name",
                    Column = new List<ViewColumnDefinition>
                    {
                        new ViewColumnDefinition { Name = "family", Path = "family", Type = "string" }
                    }
                }
            }
        };

        // Act
        var rows = _evaluator.Evaluate(ConvertToSourceNode(viewDef), resource).ToList();

        // Assert
        Assert.Single(rows);
        Assert.Null(rows[0]["family"]);
    }

    #endregion

    #region Helper Methods

    private static IElement CreateTypedElement(Dictionary<string, object?> data)
    {
        // Use real ResourceJsonNode instead of mocks for proper FHIR semantics
        var json = JsonSerializer.Serialize(data, _jsonOptions);
        var resourceNode = ResourceJsonNode.Parse(json);
        return (IElement)resourceNode.ToElement(_schemaProvider);
    }

    private static ISourceNavigator ConvertToSourceNode(ViewDefinition viewDef)
    {
        // Convert ViewDefinition model to JSON and then to ISourceNavigator
        // Use camelCase naming policy to match FHIR JSON conventions
        var json = JsonSerializer.Serialize(viewDef, _jsonOptions);
        var jsonNode = JsonNode.Parse(json)!;
        return JsonNodeSourceNode.Create(jsonNode, "ViewDefinition");
    }

    #endregion
}
