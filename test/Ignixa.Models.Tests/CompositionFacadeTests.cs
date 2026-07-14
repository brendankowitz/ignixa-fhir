// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Serialization.TestSupport;
using Shouldly;
using Xunit;

namespace Ignixa.Models.Tests;

public sealed class CompositionFacadeTests
{
    [Fact]
    public void GivenComposition_WhenReadBack_ThenSharedFieldsRoundTrip()
    {
        var composition = new Composition
        {
            Title = "Patient Summary",
            Date = "2026-07-13T12:00:00Z",
            Type = new CodeableConcept { Text = "Summary" },
        };
        composition.Author.Add(Reference.FromResourceTypeAndId("Organization", "1"));

        composition.Title.ShouldBe("Patient Summary");
        composition.Date.ShouldBe("2026-07-13T12:00:00Z");
        composition.Type!.Text.ShouldBe("Summary");
        composition.Author.Single().Reference2.ShouldBe("Organization/1");
    }

    [Fact]
    public void GivenCompositionSection_WhenReadBack_ThenValuesRoundTrip()
    {
        var section = new CompositionSection
        {
            Title = "Allergies",
            Code = new CodeableConcept { Text = "Allergies and Intolerances" },
        };
        section.Entry.Add(Reference.FromResourceTypeAndId("AllergyIntolerance", "1"));

        section.Title.ShouldBe("Allergies");
        section.Code!.Text.ShouldBe("Allergies and Intolerances");
        section.Entry.Single().Reference2.ShouldBe("AllergyIntolerance/1");
    }

    [Fact]
    public void GivenR4Composition_WhenSubjectStatusAndIdentifierSet_ThenTheyRoundTripAsR4Shape()
    {
        var composition = new Ignixa.Models.R4.Composition
        {
            Status = Ignixa.Models.R4.CompositionStatus.Final,
            Subject = Reference.FromResourceTypeAndId("Patient", "123"),
            Identifier = new Identifier { System = "urn:ietf:rfc:3986", Value = "urn:uuid:abc" },
        };

        composition.Status.ShouldBe(Ignixa.Models.R4.CompositionStatus.Final);
        composition.Subject!.Reference2.ShouldBe("Patient/123");
        composition.Identifier!.Value.ShouldBe("urn:uuid:abc");
        composition.MutableNode()["status"]!.GetValue<string>().ShouldBe("final");
    }

    [Fact]
    public void GivenBaseComposition_WhenCastToR5_ThenSubjectIsAList()
    {
        var composition = new Ignixa.Models.R5.Composition();
        composition.Subject.Add(Reference.FromResourceTypeAndId("Patient", "123"));
        composition.Identifier.Add(new Identifier { Value = "urn:uuid:abc" });

        composition.Subject.Single().Reference2.ShouldBe("Patient/123");
        composition.Identifier.Single().Value.ShouldBe("urn:uuid:abc");
    }
}
