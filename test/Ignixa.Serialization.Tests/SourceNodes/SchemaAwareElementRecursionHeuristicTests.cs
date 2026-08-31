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
/// Commit 1 narrowed the heuristic to also require the schema to declare the child as a content
/// reference (<c>ITypeExtended.ContentReference</c> non-null), which is the schema's own marker for a
/// recursive element, but kept the name-equality comparison as well.
/// <see cref="GivenQuestionnaireWithNestedItems_WhenNavigatingItemItem_ThenTypesAsTheParentQuestionnaireItem"/>
/// pins the case the heuristic exists for, so commit 2's further change does not regress it.
/// <para>
/// Commit 2 drops the name-equality half entirely and resolves the actual <c>ContentReference</c>
/// target via <c>schema.GetTypeDefinition</c>, rather than assuming the target is always the
/// immediate parent's own qualified type. <see cref="GivenExplanationOfBenefitDeeplyNestedAdjudication_WhenNavigated_ThenTypesAsTheAncestorAdjudicationBackbone"/>,
/// <see cref="GivenValueSetComposeExclude_WhenNavigated_ThenTypesAsTheSiblingIncludeBackboneNotTheParentOrItsOwnName"/>
/// and <see cref="GivenObservationComponentReferenceRange_WhenNavigated_ThenTypesAsTheAncestorReferenceRangeBackbone"/>
/// pin representative sites of the 76 unique qualified paths this unblocks - none of which the
/// name-equality heuristic ever touched, since in every one of them the child's own name differs from
/// its immediate parent's last name segment.
/// </para>
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

    [Fact]
    public void GivenExplanationOfBenefitDeeplyNestedAdjudication_WhenNavigated_ThenTypesAsTheAncestorAdjudicationBackbone()
    {
        // Arrange -- ExplanationOfBenefit.item.detail.subDetail.adjudication declares
        // contentReference "#ExplanationOfBenefit.item.adjudication": its target is neither the
        // immediate parent (ExplanationOfBenefit.item.detail.SubDetail) nor any name-matching
        // ancestor - the element's own name ("adjudication") never equals the last segment of any
        // enclosing backbone's qualified name ("SubDetail", "Detail", "item"), so the name-equality
        // heuristic never fired here even before commit 1's narrowing. Before this fix,
        // DeriveInstanceType fell through to the element's raw, unqualified name ("adjudication"),
        // which is not a valid FHIR type.
        var eobJson = """
        {
          "resourceType": "ExplanationOfBenefit",
          "id": "eob1",
          "status": "active",
          "item": [
            {
              "sequence": 1,
              "detail": [
                {
                  "sequence": 1,
                  "subDetail": [
                    {
                      "sequence": 1,
                      "adjudication": [
                        {
                          "category": { "text": "eligible" }
                        }
                      ]
                    }
                  ]
                }
              ]
            }
          ]
        }
        """;

        var resource = ResourceJsonNode.Parse(eobJson);
        var typedElement = resource.ToElement(_r4Provider);

        // Act
        var item = typedElement.Children("item").Single();
        var detail = item.Children("detail").Single();
        var subDetail = detail.Children("subDetail").Single();
        var adjudication = subDetail.Children("adjudication").Single();

        // Assert
        adjudication.InstanceType.ShouldBe("ExplanationOfBenefit.item.Adjudication");
        adjudication.InstanceType.ShouldNotBe("adjudication");

        // The resolved type's own children are reachable too - confirms the fix resolves the actual
        // target type, not merely a label, since navigation depends on the schema lookup this
        // InstanceType drives.
        var category = adjudication.Children("category").Single();
        category.InstanceType.ShouldBe("CodeableConcept");
    }

    [Fact]
    public void GivenValueSetComposeExclude_WhenNavigated_ThenTypesAsTheSiblingIncludeBackboneNotTheParentOrItsOwnName()
    {
        // Arrange -- ValueSet.compose.exclude declares contentReference "#ValueSet.compose.include":
        // its target is a SIBLING of "exclude" (both are children of "compose"), not an ancestor and
        // not the parent "compose" backbone itself. An implementation that assumes the
        // ContentReference target is always the parent's own qualified type ("ValueSet.Compose") is
        // wrong for this site - the correct target is "ValueSet.compose.Include".
        var valueSetJson = """
        {
          "resourceType": "ValueSet",
          "id": "vs1",
          "status": "active",
          "compose": {
            "include": [ { "system": "http://example.org/include-system" } ],
            "exclude": [ { "system": "http://example.org/exclude-system" } ]
          }
        }
        """;

        var resource = ResourceJsonNode.Parse(valueSetJson);
        var typedElement = resource.ToElement(_r4Provider);

        // Act
        var compose = typedElement.Children("compose").Single();
        var exclude = compose.Children("exclude").Single();

        // Assert
        exclude.InstanceType.ShouldBe("ValueSet.compose.Include");
        exclude.InstanceType.ShouldNotBe("exclude");
        exclude.InstanceType.ShouldNotBe(compose.InstanceType);

        var system = exclude.Children("system").Single();
        system.InstanceType.ShouldBe("uri");
    }

    [Fact]
    public void GivenObservationComponentReferenceRange_WhenNavigated_ThenTypesAsTheAncestorReferenceRangeBackbone()
    {
        // Arrange -- Observation.component.referenceRange declares contentReference
        // "#Observation.referenceRange": its target is the grandparent resource's own top-level
        // "referenceRange" backbone, not "component" (the immediate parent) and not
        // "Observation.Component.referenceRange" (there is no such qualified type).
        var observationJson = """
        {
          "resourceType": "Observation",
          "id": "obs1",
          "status": "final",
          "code": { "text": "vitals panel" },
          "component": [
            {
              "code": { "text": "systolic" },
              "referenceRange": [
                { "text": "normal range" }
              ]
            }
          ]
        }
        """;

        var resource = ResourceJsonNode.Parse(observationJson);
        var typedElement = resource.ToElement(_r4Provider);

        // Act
        var component = typedElement.Children("component").Single();
        var referenceRange = component.Children("referenceRange").Single();

        // Assert
        referenceRange.InstanceType.ShouldBe("Observation.ReferenceRange");
        referenceRange.InstanceType.ShouldNotBe("referenceRange");

        var text = referenceRange.Children("text").Single();
        text.InstanceType.ShouldBe("string");
        text.Value.ShouldBe("normal range");
    }
}
