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
    }

    /// <summary>
    /// The old heuristic compared the child's name against the last segment of the parent's type name,
    /// and when that name had no dot the "last segment" was the whole name - so it fired on datatypes
    /// whose child shares their name, not only on backbones. Four sites are in that class, and this is
    /// the one that matters: <c>Reference.reference</c> is on every reference in every resource, and it
    /// used to report <c>Reference</c> rather than the <c>string</c> the schema declares.
    /// </summary>
    /// <remarks>
    /// Pinned here because the parity corpus cannot see it - no search parameter resolves a reference's
    /// own <c>reference</c> child - while several consumers can: <c>resolve()</c>'s extraction path,
    /// <c>ofType(Reference)</c> in de-identification rules, and <c>FreeTextEdgeCaseStrategy</c>, which
    /// was relying on the wrong type to keep emoji out of reference values.
    /// </remarks>
    [Fact]
    public void GivenAReferencesOwnReferenceChild_WhenNavigated_ThenTypesAsStringNotReference()
    {
        var observationJson = """
        {
          "resourceType": "Observation",
          "id": "obs1",
          "status": "final",
          "code": { "coding": [ { "code": "1234-5" } ] },
          "subject": { "reference": "Patient/p1", "display": "A patient" }
        }
        """;

        var resource = ResourceJsonNode.Parse(observationJson);
        var subject = resource.ToElement(_r4Provider).Children("subject").Single();

        subject.InstanceType.ShouldBe("Reference");
        subject.Children("reference").Single().InstanceType.ShouldBe("string");
        subject.Children("display").Single().InstanceType.ShouldBe("string");
    }

    /// <summary>
    /// The one member of that datatype class the old heuristic got right, and it got it right for the
    /// wrong reason - <c>Extension.extension</c> is genuine recursion that declares no
    /// <c>ContentReference</c>, so the narrowed branch no longer fires on it at all. It still types
    /// correctly because <c>DeriveInstanceType</c> reads its declared type, which is the mechanism this
    /// test exists to pin: nested extensions are every profile and every IG.
    /// </summary>
    [Fact]
    public void GivenANestedExtension_WhenNavigated_ThenStillTypesAsExtension()
    {
        var patientJson = """
        {
          "resourceType": "Patient",
          "id": "p1",
          "extension": [
            {
              "url": "http://example.org/outer",
              "extension": [ { "url": "http://example.org/inner", "valueString": "x" } ]
            }
          ]
        }
        """;

        var resource = ResourceJsonNode.Parse(patientJson);
        var outer = resource.ToElement(_r4Provider).Children("extension").Single();
        var inner = outer.Children("extension").Single();

        outer.InstanceType.ShouldBe("Extension");
        inner.InstanceType.ShouldBe("Extension");

        // Navigable, not merely labelled: the nested extension's own choice variant resolves.
        inner.Children("valueString").Single().InstanceType.ShouldBe("string");
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

        var text = referenceRange.Children("text").Single();
        text.InstanceType.ShouldBe("string");
        text.Value.ShouldBe("normal range");
    }
}
