// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Serialization.Models;
using Ignixa.Serialization.SourceNodes;
using Shouldly;
using Xunit;

namespace Ignixa.Models.R4.Tests;

public sealed class ResourceAndContentReferenceFacadeTests
{
    [Fact]
    public void GivenBundleEntryWithResource_WhenSetAndRead_ThenReturnsTypedResourceJsonNode()
    {
        // BundleEntry has no R4/R5 subclass (fully base-only, no cross-version divergence) -- the base
        // Ignixa.Models.BundleEntry is the only type that exists.
        var entry = new Ignixa.Models.BundleEntry();
        var patient = new Ignixa.Models.Patient { Active = true };

        entry.Resource = patient;

        entry.Resource.ShouldNotBeNull();
        entry.Resource.ResourceType.ShouldBe("Patient");
    }

    [Fact]
    public void GivenOperationOutcomeWithContainedResources_WhenAdded_ThenListIsTypedResourceJsonNode()
    {
        var outcome = new Ignixa.Models.R4.OperationOutcome();
        var patient = new Ignixa.Models.Patient { Active = true };

        outcome.Contained.Add(patient);

        outcome.Contained.Count.ShouldBe(1);
        outcome.Contained[0].ResourceType.ShouldBe("Patient");
    }

    [Fact]
    public void GivenParametersParameterWithNestedParts_WhenAdded_ThenPartIsSelfTyped()
    {
        // ParametersParameter.Part resolves (via contentReference #Parameters.parameter) to
        // MutableJsonList<Ignixa.Models.ParametersParameter> -- the BASE type. .Name doesn't diverge
        // between R4/R5 and stays on the base; .ValueString (and the rest of value[x]) is real
        // per-version divergence, correctly excluded from the base, so it isn't asserted here -- reading
        // it back would require going through Ignixa.Models.R4.ParametersParameter specifically, which
        // this list's element type (the base) doesn't give you. That's expected, not a gap in this fix.
        var outer = new Ignixa.Models.ParametersParameter { Name = "outer" };

        outer.Part.Add(new Ignixa.Models.ParametersParameter { Name = "inner" });

        outer.Part.Count.ShouldBe(1);
        outer.Part[0].Name.ShouldBe("inner");
    }

    [Fact]
    public void GivenBundleEntryWithLink_WhenAdded_ThenLinkIsTypedBundleLink()
    {
        // BundleEntry.Link resolves (via contentReference #Bundle.link) to
        // MutableJsonList<Ignixa.Models.BundleLink> -- the BASE type. .Url doesn't diverge between R4/R5
        // and stays on the base; .Relation is real per-version divergence (R4 is a plain string, R5 is a
        // code bound to a value set), correctly excluded from the base, so it isn't asserted here -- same
        // reasoning as ParametersParameter.ValueString above.
        var entry = new Ignixa.Models.BundleEntry();

        entry.Link.Add(new Ignixa.Models.BundleLink { Url = "http://example.org/next" });

        entry.Link.Count.ShouldBe(1);
        entry.Link[0].Url.ShouldBe("http://example.org/next");
    }

    [Fact]
    public void GivenStructureMapContained_WhenAccessed_ThenNoLongerThrows()
    {
        // Locks in the latent MutableJsonList<ResourceJsonNode> constructor-binding bug this plan's
        // Task 1 fixed as a side effect: before the fix, this threw InvalidOperationException on the
        // FIRST use of MutableJsonList<ResourceJsonNode> anywhere in the process (a static factory
        // initializer that could never find a public (JsonObject, FhirVersion) constructor). Nothing in
        // the codebase exercised this property before now.
        var map = new StructureMapJsonNode();

        Should.NotThrow(() => map.Contained.Count);

        var patient = new Ignixa.Models.Patient { Active = true };
        map.Contained.Add(patient);

        map.Contained.Count.ShouldBe(1);
        map.Contained[0].ResourceType.ShouldBe("Patient");
    }
}
