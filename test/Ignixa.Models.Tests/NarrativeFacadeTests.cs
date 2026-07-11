// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Serialization.TestSupport;
using Shouldly;
using Xunit;

namespace Ignixa.Models.Tests;

public sealed class NarrativeFacadeTests
{
    [Fact]
    public void GivenNarrativeWithStatusAndDiv_WhenReadBack_ThenValuesRoundTrip()
    {
        var narrative = new Narrative
        {
            Status = NarrativeStatus.Generated,
            Div = "<div xmlns=\"http://www.w3.org/1999/xhtml\"><p>hello</p></div>",
        };

        narrative.Status.ShouldBe(NarrativeStatus.Generated);
        narrative.Div.ShouldBe("<div xmlns=\"http://www.w3.org/1999/xhtml\"><p>hello</p></div>");
        narrative.MutableNode()["status"]!.GetValue<string>().ShouldBe("generated");
        narrative.MutableNode()["div"]!.GetValue<string>().ShouldBe("<div xmlns=\"http://www.w3.org/1999/xhtml\"><p>hello</p></div>");
    }

    [Theory]
    [InlineData(NarrativeStatus.Generated, "generated")]
    [InlineData(NarrativeStatus.Extensions, "extensions")]
    [InlineData(NarrativeStatus.Additional, "additional")]
    [InlineData(NarrativeStatus.Empty, "empty")]
    public void GivenEachStatusValue_WhenSerialized_ThenMatchesFhirLiteral(NarrativeStatus status, string expectedLiteral)
    {
        var narrative = new Narrative { Status = status };

        narrative.MutableNode()["status"]!.GetValue<string>().ShouldBe(expectedLiteral);
    }
}
