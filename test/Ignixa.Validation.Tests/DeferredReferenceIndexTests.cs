// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Validation.Tests.TestHelpers;
using Shouldly;
using Xunit;

namespace Ignixa.Validation.Tests;

/// <summary>
/// Pins the timing of the <see cref="ReferenceIndex"/> build behind <c>EnterRootResource</c> /
/// <c>EnterContainedResource</c>: the index has a single consumer (<c>resolve()</c>), so entering a
/// scope must not walk the resource for an index nobody reads.
/// </summary>
/// <remarks>
/// Observed through the public <see cref="IElement"/> seam - the index cannot be built without asking
/// the resource for its children - rather than by exposing a build counter on the production type.
/// </remarks>
public class DeferredReferenceIndexTests
{
    private static IElement ToElement(string json)
        => JsonNodeSourceNode.Create(JsonNode.Parse(json)!).ToElement(TestSchemaProvider.GetR4Schema());

    [Fact]
    public void GivenResource_WhenEnteringRootScope_ThenResourceIsNotWalked()
    {
        // Arrange
        var recording = new ChildAccessRecordingElement(
            ToElement("""{ "resourceType": "Patient", "id": "p1" }"""));

        // Act
        var state = new ValidationState().EnterRootResource(recording);

        // Assert — a seeded scope still advertises a resolver, but nothing has been indexed yet.
        state.Scope.Resolver.ShouldNotBeNull();
        recording.ChildAccessCount.ShouldBe(0);
    }

    [Fact]
    public void GivenRootScope_WhenResolverIsFirstCalled_ThenResourceIsWalked()
    {
        // Arrange
        var recording = new ChildAccessRecordingElement(
            ToElement("""{ "resourceType": "Patient", "id": "p1" }"""));
        var state = new ValidationState().EnterRootResource(recording);

        // Act
        state.Scope.Resolver!("#anything");

        // Assert
        recording.ChildAccessCount.ShouldBeGreaterThan(0);
        recording.RequestedChildNames.ShouldContain("contained");
    }

    [Fact]
    public void GivenRootScope_WhenResolverIsCalledRepeatedly_ThenResourceIsWalkedOnce()
    {
        // Arrange
        var recording = new ChildAccessRecordingElement(ToElement("""
        {
            "resourceType": "Observation",
            "id": "obs1",
            "status": "final",
            "contained": [ { "resourceType": "Patient", "id": "p1" } ]
        }
        """));
        var state = new ValidationState().EnterRootResource(recording);

        // Act
        state.Scope.Resolver!("#p1");
        var walksAfterFirstResolve = recording.ChildAccessCount;
        state.Scope.Resolver!("#p1");
        state.Scope.Resolver!("#missing");

        // Assert — memoized: deferring the build must not turn it into a per-call build.
        walksAfterFirstResolve.ShouldBeGreaterThan(0);
        recording.ChildAccessCount.ShouldBe(walksAfterFirstResolve);
    }

    [Fact]
    public void GivenContainedScope_WhenResolvingWithinContained_ThenParentIsNotWalked()
    {
        // Arrange — the chained resolver must only reach the parent on a local miss, so a hit inside
        // the contained resource leaves the parent's index unbuilt.
        var parent = ToElement("""
        {
            "resourceType": "Observation",
            "id": "obs1",
            "status": "final",
            "contained": [ {
                "resourceType": "Patient",
                "id": "p1",
                "contained": [ { "resourceType": "Organization", "id": "org1" } ]
            } ]
        }
        """);
        var recordingParent = new ChildAccessRecordingElement(parent);
        var contained = new ChildAccessRecordingElement(parent.Children("contained")[0]);

        var state = new ValidationState()
            .EnterRootResource(recordingParent)
            .EnterContainedResource(contained);

        // Act
        var resolved = state.Scope.Resolver!("#org1");

        // Assert
        resolved.ShouldNotBeNull();
        resolved.InstanceType.ShouldBe("Organization");
        contained.ChildAccessCount.ShouldBeGreaterThan(0);
        recordingParent.ChildAccessCount.ShouldBe(0);
    }

    [Fact]
    public void GivenContainedScope_WhenResolvingOutsideContained_ThenParentIsWalkedOnMiss()
    {
        // Arrange
        var parent = ToElement("""
        {
            "resourceType": "Observation",
            "id": "obs1",
            "status": "final",
            "contained": [
                { "resourceType": "Patient", "id": "p1" },
                { "resourceType": "Organization", "id": "org1" }
            ]
        }
        """);
        var recordingParent = new ChildAccessRecordingElement(parent);
        var contained = new ChildAccessRecordingElement(parent.Children("contained")[0]);

        var state = new ValidationState()
            .EnterRootResource(recordingParent)
            .EnterContainedResource(contained);

        // Act — #org1 is a contained peer, so it misses the child's own (empty) index and falls
        // through to the parent, which must be indexed at that point.
        var resolved = state.Scope.Resolver!("#org1");

        // Assert
        resolved.ShouldNotBeNull();
        resolved.InstanceType.ShouldBe("Organization");
        recordingParent.ChildAccessCount.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void GivenBundleScope_WhenResolvingEntryReference_ThenEntriesAreIndexedOnDemand()
    {
        // Arrange
        var recording = new ChildAccessRecordingElement(ToElement("""
        {
            "resourceType": "Bundle",
            "id": "b1",
            "type": "collection",
            "entry": [
                { "fullUrl": "urn:uuid:1", "resource": { "resourceType": "Patient", "id": "p1" } }
            ]
        }
        """));
        var state = new ValidationState().EnterRootResource(recording);

        // Act
        recording.ChildAccessCount.ShouldBe(0);
        var byRelative = state.Scope.Resolver!("Patient/p1");
        var byFullUrl = state.Scope.Resolver!("urn:uuid:1");

        // Assert
        byRelative.ShouldNotBeNull();
        byRelative.InstanceType.ShouldBe("Patient");
        byFullUrl.ShouldBeSameAs(byRelative);
        recording.RequestedChildNames.ShouldContain("entry");
    }
}
