// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Serialization.TestSupport;
using Shouldly;
using Xunit;

namespace Ignixa.Models.Tests;

public sealed class ConceptMapFacadeTests
{
    [Fact]
    public void GivenConceptMap_WhenReadBack_ThenSharedFieldsRoundTrip()
    {
        var conceptMap = new ConceptMap
        {
            Url = "http://example.org/fhir/ConceptMap/test",
            Name = "TestMap",
            Title = "Test Concept Map",
            Status = PublicationStatus.Active,
        };

        var element = new ConceptMapGroupElement { Code = "foo" };
        var group = new ConceptMapGroup();
        group.Element.Add(element);
        conceptMap.Group.Add(group);

        conceptMap.Url.ShouldBe("http://example.org/fhir/ConceptMap/test");
        conceptMap.Name.ShouldBe("TestMap");
        conceptMap.Status.ShouldBe(PublicationStatus.Active);
        conceptMap.Group.Single().Element.Single().Code.ShouldBe("foo");
    }

    [Fact]
    public void GivenR4ConceptMapGroupAndTarget_WhenSourceAndEquivalenceSet_ThenRoundTripAsR4Shape()
    {
        var group = new Ignixa.Models.R4.ConceptMapGroup { Source = "http://example.org/CodeSystem/a" };
        var target = new Ignixa.Models.R4.ConceptMapGroupElementTarget
        {
            Code = "bar",
            Equivalence = Ignixa.Models.R4.ConceptMapEquivalence.Equivalent,
        };

        group.Source.ShouldBe("http://example.org/CodeSystem/a");
        target.Equivalence.ShouldBe(Ignixa.Models.R4.ConceptMapEquivalence.Equivalent);
        target.MutableNode()["equivalence"]!.GetValue<string>().ShouldBe("equivalent");
    }

    [Fact]
    public void GivenR5ConceptMapTarget_WhenRelationshipSet_ThenRoundTripsAsRelationshipLiteral()
    {
        var target = new Ignixa.Models.R5.ConceptMapGroupElementTarget
        {
            Code = "bar",
            Relationship = Ignixa.Models.R5.ConceptMapRelationship.Equivalent,
        };

        target.Relationship.ShouldBe(Ignixa.Models.R5.ConceptMapRelationship.Equivalent);
        target.MutableNode()["relationship"]!.GetValue<string>().ShouldBe("equivalent");
    }
}
