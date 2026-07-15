// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Serialization.TestSupport;
using Shouldly;
using Xunit;

namespace Ignixa.Models.Tests;

public sealed class ReferenceFacadeTests
{
    [Fact]
    public void GivenReferenceWithValue_WhenReadBack_ThenValueRoundTrips()
    {
        var reference = new Reference { Reference2 = "Patient/123" };

        reference.Reference2.ShouldBe("Patient/123");
        reference.MutableNode()["reference"]!.GetValue<string>().ShouldBe("Patient/123");
    }

    [Fact]
    public void GivenResourceTypeAndId_WhenCreatedViaFactory_ThenReferenceIsResourceTypeSlashId()
    {
        var reference = Reference.FromResourceTypeAndId("Patient", "123");

        reference.Reference2.ShouldBe("Patient/123");
    }

    [Fact]
    public void GivenNullResourceType_WhenCreatedViaFactory_ThenThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => Reference.FromResourceTypeAndId(null!, "123"));
    }

    [Fact]
    public void GivenNullId_WhenCreatedViaFactory_ThenThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => Reference.FromResourceTypeAndId("Patient", null!));
    }
}
