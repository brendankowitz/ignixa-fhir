// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Parser;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Ignixa.Specification.Generated;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Checks;
using Ignixa.Validation.Tests.TestHelpers;
using Shouldly;
using Xunit;

namespace Ignixa.Validation.Tests;

/// <summary>
/// Pins WHO builds the <see cref="ReferenceIndex"/> during validation, and how many times.
/// </summary>
/// <remarks>
/// <para>
/// There is one implementation of in-instance reference resolution (<see cref="ReferenceIndex"/>) and
/// two consumers that each build it from the current scope's <c>RootResource ?? Resource</c>:
/// <c>EvaluationContext.ReferenceIndexCache</c> for anything reached through FHIRPath
/// (<c>resolve()</c> in an invariant or a slicing discriminator), and
/// <see cref="ReferenceResolutionCheck"/> for the one consumer that is not a FHIRPath evaluation.
/// <see cref="ValidationState"/> builds no index of its own.
/// </para>
/// <para>
/// It used to. <c>ResourceScope.Resolver</c> was a memoised in-instance resolver handed to
/// <c>FhirEvaluationContext.ElementResolver</c>, which sits BEHIND the index as a fallback - so it was
/// consulted only when the index had already missed, and being the weaker of the two implementations
/// (no bare <c>#</c>, no focus-scoped fragment isolation) it never resolved anything the index had not.
/// All it did was rebuild an identical index on every miss. These tests are the measurement that
/// established that, kept executable: the build counts below were 2 and 3 before the two mechanisms
/// were collapsed into one.
/// </para>
/// <para>
/// Counted through the public <see cref="IElement"/> seam rather than a build counter on the
/// production type: an index cannot be built without asking its root for <c>contained</c> exactly once.
/// </para>
/// </remarks>
public class ReferenceIndexOwnershipTests
{
    private readonly ISchema _schema = new R4CoreSchemaProvider();
    private readonly FhirPathParser _parser = new();

    private static IElement ToElement(string json)
        => JsonNodeSourceNode.Create(JsonNode.Parse(json)!).ToElement(TestSchemaProvider.GetR4Schema());

    /// <summary>
    /// How many times an index was built over this element: <see cref="ReferenceIndex.Build"/> asks its
    /// root for <c>contained</c> exactly once, and nothing else in these scenarios asks for it.
    /// </summary>
    private static int IndexBuilds(ChildAccessRecordingElement element)
        => element.RequestedChildNames.Count(name => name == "contained");

    private static Ignixa.Specification.ConstraintDefinition Constraint(string expression, string appliesTo)
        => new()
        {
            Key = "probe",
            Severity = ConstraintSeverity.Error,
            Human = "probe",
            Expression = expression,
            Xpath = null,
            AppliesTo = new[] { appliesTo }
        };

    private ValidationResult Evaluate(IElement element, ValidationState state, string expression, string appliesTo)
        => new FhirPathInvariantCheck(Constraint(expression, appliesTo), _schema, _parser)
            .Validate(element, new ValidationSettings { Depth = ValidationDepth.Spec }, state);

    [Fact]
    public void GivenResource_WhenEnteringRootScope_ThenResourceIsNotWalked()
    {
        // Arrange
        var recording = new ChildAccessRecordingElement(
            ToElement("""{ "resourceType": "Patient", "id": "p1" }"""));

        // Act
        var state = new ValidationState().EnterRootResource(recording);

        // Assert — seeding a scope records two element references and walks nothing.
        state.Scope.Resource.ShouldBeSameAs(recording);
        recording.ChildAccessCount.ShouldBe(0);
    }

    [Fact]
    public void GivenResource_WhenEnteringContainedScope_ThenNeitherResourceIsWalked()
    {
        // Arrange
        var parent = ToElement("""
        {
            "resourceType": "Observation",
            "id": "obs1",
            "status": "final",
            "contained": [ { "resourceType": "Patient", "id": "p1" } ]
        }
        """);
        var recordingParent = new ChildAccessRecordingElement(parent);
        var recordingContained = new ChildAccessRecordingElement(parent.Children("contained")[0]);

        // Act
        var state = new ValidationState()
            .EnterRootResource(recordingParent)
            .EnterContainedResource(recordingContained);

        // Assert
        state.Scope.Resource.ShouldBeSameAs(recordingContained);
        state.Scope.RootResource.ShouldBeSameAs(recordingParent);
        recordingParent.ChildAccessCount.ShouldBe(0);
        recordingContained.ChildAccessCount.ShouldBe(0);
    }

    [Fact]
    public void GivenRootScopedInvariant_WhenResolveMisses_ThenIndexIsBuiltOnce()
    {
        // Arrange — the miss path is the one that used to build twice: the index missed, then the
        // ElementResolver fallback rebuilt the same index over the same root and missed identically.
        var recording = new ChildAccessRecordingElement(ToElement("""
        {
            "resourceType": "Patient",
            "id": "example",
            "contained": [ { "resourceType": "Practitioner", "id": "p1" } ],
            "generalPractitioner": [ { "reference": "#missing" } ]
        }
        """));
        var state = new ValidationState().EnterRootResource(recording);

        // Act
        var result = Evaluate(recording, state, "generalPractitioner.reference.resolve().empty()", "Patient");

        // Assert
        result.IsValid.ShouldBeTrue();
        IndexBuilds(recording).ShouldBe(1);
    }

    [Fact]
    public void GivenContainedScopedInvariant_WhenResolveMisses_ThenOnlyTheParentIsIndexedAndOnlyOnce()
    {
        // Arrange — worst case for the old design: parent indexed by the cache, contained indexed by the
        // chained resolver, then the parent indexed a second time by that chain's fall-through. Three
        // builds to conclude a reference does not resolve.
        var parent = ToElement("""
        {
            "resourceType": "Observation",
            "id": "obs1",
            "status": "final",
            "code": { "text": "x" },
            "contained": [
                { "resourceType": "Patient", "id": "p1", "generalPractitioner": [ { "reference": "#nope" } ] },
                { "resourceType": "Practitioner", "id": "pr1" }
            ]
        }
        """);
        var recordingParent = new ChildAccessRecordingElement(parent);
        var recordingContained = new ChildAccessRecordingElement(parent.Children("contained")[0]);
        var state = new ValidationState()
            .EnterRootResource(recordingParent)
            .EnterContainedResource(recordingContained);

        // Act
        var result = Evaluate(
            recordingContained, state, "generalPractitioner.reference.resolve().empty()", "Patient");

        // Assert — the contained resource is never indexed: FHIR forbids nested contained, so its own
        // pool is always empty and indexing it could only ever resolve nothing.
        result.IsValid.ShouldBeTrue();
        IndexBuilds(recordingParent).ShouldBe(1);
        IndexBuilds(recordingContained).ShouldBe(0);
    }

    [Fact]
    public void GivenContainedScopedInvariant_WhenResolvingAPeerContainedResource_ThenItResolvesViaTheParentPool()
    {
        // Arrange — the case most likely to break silently when the chained resolver went away. A
        // contained resource referencing a contained PEER (#pr1) has nothing in its own pool to resolve
        // against; it works only because %rootResource points at the containing parent, whose pool holds
        // every peer.
        var parent = ToElement("""
        {
            "resourceType": "Observation",
            "id": "obs1",
            "status": "final",
            "code": { "text": "x" },
            "contained": [
                { "resourceType": "Patient", "id": "p1", "generalPractitioner": [ { "reference": "#pr1" } ] },
                { "resourceType": "Practitioner", "id": "pr1" }
            ]
        }
        """);
        var contained = parent.Children("contained")[0];
        var state = new ValidationState().EnterRootResource(parent).EnterContainedResource(contained);

        // Act
        var result = Evaluate(
            contained, state, "generalPractitioner.reference.resolve().is(Practitioner)", "Patient");

        // Assert
        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void GivenContainedScopedInvariant_WhenResolvingBareHash_ThenItResolvesToTheContainer()
    {
        // Arrange — bare '#' resolves to the container from inside a contained resource's scope. The
        // resolver that used to back this scope could not do it at all (ReferenceIndex.Resolve(string)
        // returns null for "#"), so this capability arrives entirely from the surviving mechanism.
        var parent = ToElement("""
        {
            "resourceType": "Observation",
            "id": "obs1",
            "status": "final",
            "code": { "text": "x" },
            "contained": [ { "resourceType": "Patient", "id": "p1" } ]
        }
        """);
        var contained = parent.Children("contained")[0];
        var state = new ValidationState().EnterRootResource(parent).EnterContainedResource(contained);

        // Act
        var result = Evaluate(contained, state, "'#'.resolve().is(Observation)", "Patient");

        // Assert
        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void GivenOneConstraintResolvingRepeatedly_WhenEvaluated_ThenIndexIsBuiltOnce()
    {
        // Arrange — within a single evaluation the cache survives every `with`-copy of the context, so
        // three resolve() calls in one expression share one index.
        var recording = new ChildAccessRecordingElement(ToElement("""
        {
            "resourceType": "Patient",
            "id": "example",
            "contained": [ { "resourceType": "Practitioner", "id": "p1" } ],
            "generalPractitioner": [
                { "reference": "#p1" }, { "reference": "#p1" }, { "reference": "#p1" }
            ]
        }
        """));
        var state = new ValidationState().EnterRootResource(recording);

        // Act
        var result = Evaluate(
            recording, state, "generalPractitioner.reference.resolve().count() = 3", "Patient");

        // Assert
        result.IsValid.ShouldBeTrue();
        IndexBuilds(recording).ShouldBe(1);
    }

    [Fact]
    public void GivenContainedScope_WhenReferenceResolutionCheckRuns_ThenPeersResolveAndDanglingRefsAreFlagged()
    {
        // Arrange — ReferenceResolutionCheck is the one consumer that is not a FHIRPath evaluation, so
        // the per-EvaluationContext cache cannot serve it and it builds its own index. ContainedResourceCheck
        // validates each contained resource against its own schema, which runs this check at contained
        // scope - so it has to reach the parent's pool for peers exactly as resolve() does.
        var parent = ToElement("""
        {
            "resourceType": "Observation",
            "id": "obs1",
            "status": "final",
            "code": { "text": "x" },
            "contained": [
                {
                    "resourceType": "Patient",
                    "id": "p1",
                    "generalPractitioner": [ { "reference": "#pr1" }, { "reference": "#nope" } ]
                },
                { "resourceType": "Practitioner", "id": "pr1" }
            ]
        }
        """);
        var contained = parent.Children("contained")[0];
        var state = new ValidationState().EnterRootResource(parent).EnterContainedResource(contained);

        // Act
        var result = new ReferenceResolutionCheck().Validate(
            contained, new ValidationSettings { Depth = ValidationDepth.Full }, state);

        // Assert — the peer resolves, the dangling reference is the only issue.
        result.Issues.Count(i => i.Code == "ref-resolve").ShouldBe(1);
        result.Issues.ShouldContain(i => i.Message.Contains("#nope", StringComparison.Ordinal));
        result.Issues.ShouldNotContain(i => i.Message.Contains("#pr1", StringComparison.Ordinal));
    }

    [Fact]
    public void GivenBundleEntriesWithTheSameFragmentId_WhenReferenceResolutionCheckRuns_ThenFragmentsStayEntryScoped()
    {
        // Arrange — entry[0] contains #prA; entry[1] references #prA but contains nothing. Fragment
        // lookups are scoped to the entry that encloses the reference, so entry[1]'s must NOT see
        // entry[0]'s contained pool. This is the containment isolation that ReferenceIndex enforces via
        // the focus location, replacing the resolver-rechaining the check used to do itself.
        var bundle = ToElement("""
        {
            "resourceType": "Bundle",
            "type": "collection",
            "entry": [
                { "resource": {
                    "resourceType": "Patient", "id": "p1",
                    "contained": [ { "resourceType": "Practitioner", "id": "prA" } ],
                    "generalPractitioner": [ { "reference": "#prA" } ] } },
                { "resource": {
                    "resourceType": "Patient", "id": "p2",
                    "generalPractitioner": [ { "reference": "#prA" } ] } }
            ]
        }
        """);
        var state = new ValidationState().EnterRootResource(bundle);

        // Act
        var result = new ReferenceResolutionCheck().Validate(
            bundle, new ValidationSettings { Depth = ValidationDepth.Full }, state);

        // Assert
        result.Issues.Count(i => i.Code == "ref-resolve").ShouldBe(1);
        result.Issues.ShouldContain(i => i.Path == "Bundle.entry[1].resource.generalPractitioner[0].reference");
    }
}
