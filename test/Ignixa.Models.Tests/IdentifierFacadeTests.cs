// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Serialization.TestSupport;
using Shouldly;
using Xunit;

namespace Ignixa.Models.Tests;

public sealed class IdentifierFacadeTests
{
    [Fact]
    public void GivenIdentifierWithSystemAndValue_WhenReadBack_ThenValuesRoundTrip()
    {
        var identifier = new Identifier
        {
            System = "http://example.org/mrn",
            Value = "12345",
        };

        identifier.System.ShouldBe("http://example.org/mrn");
        identifier.Value.ShouldBe("12345");
        identifier.MutableNode()["system"]!.GetValue<string>().ShouldBe("http://example.org/mrn");
        identifier.MutableNode()["value"]!.GetValue<string>().ShouldBe("12345");
    }

    [Fact]
    public void GivenIdentifierWithUse_WhenSerialized_ThenMatchesFhirLiteral()
    {
        var identifier = new Identifier { Use = IdentifierUse.Official };

        identifier.Use.ShouldBe(IdentifierUse.Official);
        identifier.MutableNode()["use"]!.GetValue<string>().ShouldBe("official");
    }

    [Fact]
    public void GivenIdentifierWithAssigner_WhenReadBack_ThenAssignerIsReadableAsReference()
    {
        var identifier = new Identifier
        {
            Assigner = new Reference { Reference2 = "Organization/1" },
        };

        identifier.Assigner!.Reference2.ShouldBe("Organization/1");
    }

    [Fact]
    public void GivenIdentifierWithType_WhenReadBack_ThenTypeIsReadableAsCodeableConcept()
    {
        var identifier = new Identifier
        {
            Type = new CodeableConcept { Text = "Medical Record Number" },
        };

        identifier.Type!.Text.ShouldBe("Medical Record Number");
    }
}
