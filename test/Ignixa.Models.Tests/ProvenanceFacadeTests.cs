// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
using Xunit;

namespace Ignixa.Models.Tests;

public sealed class ProvenanceFacadeTests
{
    [Fact]
    public void GivenProvenance_WhenReadBack_ThenSharedFieldsRoundTrip()
    {
        var provenance = new Provenance
        {
            Recorded = "2026-07-13T12:00:00Z",
        };
        provenance.Target.Add(Reference.FromResourceTypeAndId("Patient", "123"));
        provenance.Agent.Add(new ProvenanceAgent
        {
            Who = Reference.FromResourceTypeAndId("Practitioner", "1"),
        });

        provenance.Recorded.ShouldBe("2026-07-13T12:00:00Z");
        provenance.Target.Single().Reference2.ShouldBe("Patient/123");
        provenance.Agent.Single().Who!.Reference2.ShouldBe("Practitioner/1");
    }

    [Fact]
    public void GivenProvenance_WhenAddTargetCalled_ThenAppendsVersionedReference()
    {
        var provenance = new Provenance();

        provenance.AddTarget("Patient", "123", "2");

        provenance.Target.Single().Reference2.ShouldBe("Patient/123/_history/2");
    }

    [Theory]
    [InlineData(null, "123", "2")]
    [InlineData("Patient", null, "2")]
    [InlineData("Patient", "123", null)]
    [InlineData("", "123", "2")]
    public void GivenProvenance_WhenAddTargetCalledWithMissingArgument_ThenThrows(string? resourceType, string? resourceId, string? versionId)
    {
        var provenance = new Provenance();

        Should.Throw<ArgumentException>(() => provenance.AddTarget(resourceType!, resourceId!, versionId!));
    }
}
