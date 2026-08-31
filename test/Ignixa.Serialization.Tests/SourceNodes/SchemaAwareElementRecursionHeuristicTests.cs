// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Ignixa.Specification.Extensions;
using Shouldly;
using Xunit;

namespace Ignixa.Serialization.Tests.SourceNodes;

/// <summary>
/// Falsification tests for issue #454: <c>ComputeChildResolution</c>'s recursive-BackboneElement
/// heuristic used to fire whenever a child element's name matched the last segment of its parent's
/// qualified type name, regardless of what the schema actually declared. That name-equality shape also
/// matches non-recursive children that merely happen to share their backbone's name -
/// <c>Encounter.location.location</c> is a plain <c>Reference</c>, not a recursive backbone - so the
/// heuristic overwrote a correct answer with the parent's own qualified type name.
/// </summary>
/// <remarks>
/// The fix narrows the heuristic to also require the schema to declare the child as a content
/// reference (<c>ITypeExtended.ContentReference</c> non-null), which is the schema's own marker for a
/// recursive element. <see cref="GivenQuestionnaireWithNestedItems_WhenNavigatingItemItem_ThenTypesAsTheParentQuestionnaireItem"/>
/// pins the case the heuristic exists for, so the narrowing does not regress it.
/// </remarks>
public class SchemaAwareElementRecursionHeuristicTests
{
    private readonly IFhirSchemaProvider _r4Provider = FhirVersion.R4.GetSchemaProvider();

    [Fact]
    public void GivenEncounterWithPopulatedLocationLocation_WhenNavigated_ThenTheChildTypesAsReferenceNotEncounterLocation()
    {
        // Arrange
        var encounterJson = """
        {
          "resourceType": "Encounter",
          "id": "enc1",
          "status": "in-progress",
          "class": { "code": "AMB" },
          "location": [
            {
              "location": { "reference": "Location/loc1" },
              "status": "active"
            }
          ]
        }
        """;

        var resource = ResourceJsonNode.Parse(encounterJson);
        var typedElement = resource.ToElement(_r4Provider);

        // Act
        var locationBackbone = typedElement.Children("location").Single();
        var nestedLocation = locationBackbone.Children("location").Single();

        // Assert
        nestedLocation.InstanceType.ShouldBe("Reference");
        nestedLocation.InstanceType.ShouldNotBe("Encounter.Location");
    }

    [Fact]
    public void GivenQuestionnaireWithNestedItems_WhenNavigatingItemItem_ThenTypesAsTheParentQuestionnaireItem()
    {
        // Arrange -- the recursive shape (QuestionnaireResponse.item.item / Questionnaire.item.item) the
        // heuristic exists for. The heuristic must still fire here: Questionnaire.Item.item declares no
        // type of its own and carries a ContentReference back to "#Questionnaire.item".
        var questionnaireJson = """
        {
          "resourceType": "Questionnaire",
          "id": "q1",
          "status": "active",
          "item": [
            {
              "linkId": "group1",
              "type": "group",
              "item": [
                {
                  "linkId": "nested1",
                  "type": "string"
                }
              ]
            }
          ]
        }
        """;

        var resource = ResourceJsonNode.Parse(questionnaireJson);
        var typedElement = resource.ToElement(_r4Provider);

        // Act
        var outerItem = typedElement.Children("item").Single();
        var innerItem = outerItem.Children("item").Single();

        // Assert
        outerItem.InstanceType.ShouldBe("Questionnaire.Item");
        innerItem.InstanceType.ShouldBe("Questionnaire.Item");
    }
}
