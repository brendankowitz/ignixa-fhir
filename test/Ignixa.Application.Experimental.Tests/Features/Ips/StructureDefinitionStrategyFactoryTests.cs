// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Application.Features.Experimental.Ips.Strategy;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Ignixa.Application.Tests.Features.Ips;

/// <summary>
/// Unit tests for <see cref="StructureDefinitionStrategyFactory"/>.
/// </summary>
public class StructureDefinitionStrategyFactoryTests
{
    // Regression for #536: the slice name came from a package and was interpolated into FHIRPath text, so a
    // quote in it was parsed as syntax (a parse error, or a predicate matching other slices' elements).
    [Fact]
    public void GivenSliceNameContainingAQuoteAndExpressionSyntax_WhenCreatingAStrategy_ThenEachSliceKeepsItsOwnMetadata()
    {
        // Arrange
        const string hostile = "a' or true or path = 'b";
        var json = $$"""
        {
          "resourceType": "StructureDefinition",
          "url": "http://example.org/test",
          "type": "Composition",
          "snapshot": {
            "element": [
              { "path": "Composition.section", "slicing": { "rules": "open" } },
              { "path": "Composition.section:plain", "sliceName": "plain", "min": 0, "max": "1" },
              {
                "path": "Composition.section:plain.code",
                "patternCodeableConcept": { "coding": [{ "system": "http://loinc.org", "code": "1-1" }] }
              },
              { "path": "Composition.section:plain.title", "fixedString": "Plain" },
              { "path": "Composition.section:{{hostile}}", "sliceName": "{{hostile}}", "min": 0, "max": "1" },
              {
                "path": "Composition.section:{{hostile}}.code",
                "patternCodeableConcept": { "coding": [{ "system": "http://loinc.org", "code": "2-2" }] }
              },
              { "path": "Composition.section:{{hostile}}.title", "fixedString": "Hostile" }
            ]
          }
        }
        """;
        var factory = new StructureDefinitionStrategyFactory(
            new R4CoreSchemaProvider(), NullLogger<StructureDefinitionStrategyFactory>.Instance);

        // Act
        var strategy = factory.CreateFromStructureDefinition(JsonSourceNodeFactory.Parse<ResourceJsonNode>(json));

        // Assert
        strategy.ShouldNotBeNull().GetSections()
            .Select(section => (section.Title, section.Code))
            .ShouldBe([("Plain", "1-1"), ("Hostile", "2-2")], ignoreOrder: true);
    }
}
