/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Unit tests for the 'and' operator inside where() clauses.
 * Reproduces bug where 'and' operator was broken by visitor pattern refactoring.
 */

using Ignixa.FhirPath;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Parser;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Ignixa.Specification.Extensions;

namespace Ignixa.FhirPath.Tests.Evaluation;

public class AndOperatorInWhereTests
{
    private readonly FhirPathParser _parser = new();
    private readonly FhirPathEvaluator _evaluator = new();
    private readonly IFhirSchemaProvider _r4Provider;

    public AndOperatorInWhereTests()
    {
        _r4Provider = FhirVersion.R4.GetSchemaProvider();
    }

    private IEnumerable<IElement> EvaluatePath(IElement element, string pathExpression)
    {
        var expression = _parser.Parse(pathExpression);
        return _evaluator.Evaluate(element, expression, new EvaluationContext());
    }

    [Fact]
    public void GivenCollectionWithMultipleConditions_WhenUsingAndInWhere_ThenFiltersBothConditions()
    {
        // Arrange - StructureDefinition with section slices
        var json = """
        {
          "resourceType": "StructureDefinition",
          "url": "http://example.org/test",
          "type": "Composition",
          "snapshot": {
            "element": [
              {
                "path": "Composition.section",
                "sliceName": null
              },
              {
                "path": "Composition.section:sectionAllergies",
                "sliceName": "sectionAllergies"
              },
              {
                "path": "Composition.section:sectionAllergies.title"
              },
              {
                "path": "Composition.section:sectionMedications",
                "sliceName": "sectionMedications"
              }
            ]
          }
        }
        """;

        var resource = ResourceJsonNode.Parse(json);
        var element = resource.ToElement(_r4Provider);

        // Act - Use AND to filter elements matching BOTH conditions
        var resultWithAnd = EvaluatePath(element,
            "snapshot.element.where(path.startsWith('Composition.section:') and sliceName.exists())");

        // Act - For comparison, use chained where() (the workaround that works)
        var resultChained = EvaluatePath(element,
            "snapshot.element.where(path.startsWith('Composition.section:')).where(sliceName.exists())");

        // Assert - Both should return the same 2 elements (the slice definitions, not the base or child elements)
        var andList = resultWithAnd.ToList();
        var chainedList = resultChained.ToList();

        Assert.Equal(2, chainedList.Count); // Workaround works
        Assert.Equal(2, andList.Count); // This is the bug - was returning 0
        Assert.Equal(chainedList.Count, andList.Count);
    }

    [Fact]
    public void GivenSimpleCollection_WhenUsingAndInWhere_ThenFiltersBothConditions()
    {
        // Arrange - Simple test with identifiers
        var json = """
        {
          "resourceType": "Patient",
          "id": "test",
          "identifier": [
            {
              "system": "http://example.org/mrn",
              "value": "12345",
              "use": "official"
            },
            {
              "system": "http://example.org/mrn",
              "value": "67890"
            },
            {
              "system": "http://example.org/other",
              "value": "99999",
              "use": "official"
            }
          ]
        }
        """;

        var resource = ResourceJsonNode.Parse(json);
        var element = resource.ToElement(_r4Provider);

        // Act - Filter identifiers where BOTH conditions match:
        // 1. system = 'http://example.org/mrn'
        // 2. use.exists()
        var resultWithAnd = EvaluatePath(element,
            "identifier.where(system = 'http://example.org/mrn' and use.exists())");

        var resultChained = EvaluatePath(element,
            "identifier.where(system = 'http://example.org/mrn').where(use.exists())");

        // Assert - Only the first identifier matches both conditions
        var andList = resultWithAnd.ToList();
        var chainedList = resultChained.ToList();

        Assert.Single(chainedList); // Workaround works
        Assert.Single(andList); // Bug - was returning 0
        Assert.Equal(chainedList.Count, andList.Count);
    }

    [Fact]
    public void GivenTrueAndTrue_WhenEvaluating_ThenReturnsTrue()
    {
        // Arrange - Simple boolean and (sanity check)
        var json = """
        {
          "resourceType": "Patient",
          "id": "test"
        }
        """;

        var resource = ResourceJsonNode.Parse(json);
        var element = resource.ToElement(_r4Provider);

        // Act
        var result = EvaluatePath(element, "true and true");

        // Assert
        var list = result.ToList();
        Assert.Single(list);
        Assert.True((bool)list[0].Value!);
    }

    [Fact]
    public void GivenMultipleConditionsInWhereWithAnd_WhenEvaluating_ThenAllConditionsApplied()
    {
        // Arrange - More complex test with 3 conditions
        var json = """
        {
          "resourceType": "Patient",
          "id": "test",
          "name": [
            {
              "use": "official",
              "family": "Smith",
              "given": ["John"]
            },
            {
              "use": "official",
              "family": "Doe"
            },
            {
              "use": "nickname",
              "family": "Smith",
              "given": ["Johnny"]
            },
            {
              "family": "Smith",
              "given": ["Jane"]
            }
          ]
        }
        """;

        var resource = ResourceJsonNode.Parse(json);
        var element = resource.ToElement(_r4Provider);

        // Act - Filter names where use='official' AND family='Smith' AND given.exists()
        var result = EvaluatePath(element,
            "name.where(use = 'official' and family = 'Smith' and given.exists())");

        // Assert - Only the first name matches all 3 conditions
        var list = result.ToList();
        Assert.Single(list);

        // Verify it's the right one
        var givenResult = EvaluatePath(list[0], "given.first()");
        Assert.Equal("John", givenResult.First().Value);
    }

    [Fact]
    public void GivenNestedAndOperators_WhenEvaluating_ThenAllConditionsApplied()
    {
        // Arrange
        var json = """
        {
          "resourceType": "Patient",
          "id": "test",
          "identifier": [
            { "system": "A", "value": "1", "use": "official" },
            { "system": "A", "value": "2", "use": "temp" },
            { "system": "B", "value": "1", "use": "official" }
          ]
        }
        """;

        var resource = ResourceJsonNode.Parse(json);
        var element = resource.ToElement(_r4Provider);

        // Act - Nested and: (system = 'A' and value = '1') and use = 'official'
        var result = EvaluatePath(element,
            "identifier.where((system = 'A' and value = '1') and use = 'official')");

        // Assert - Only the first identifier matches all conditions
        var list = result.ToList();
        Assert.Single(list);
    }
}
