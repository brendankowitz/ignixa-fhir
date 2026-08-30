// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Evaluation.Functions;
using Ignixa.FhirPath.Parser;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Ignixa.Specification.Extensions;
using Shouldly;
using Xunit;

namespace Ignixa.FhirPath.Tests.Evaluation;

/// <summary>
/// Unit tests for the <c>resolve()</c> function's in-instance resolution (GitHub issue #400):
/// contained resources and sibling Bundle entries must resolve without an external
/// <see cref="FhirEvaluationContext.ElementResolver"/>, and the in-instance result must take
/// precedence when a reference could also be resolved externally.
/// </summary>
public class ResolveFunctionTests
{
    private readonly IFhirSchemaProvider _r4Provider = FhirVersion.R4.GetSchemaProvider();
    private readonly FhirPathParser _parser = new();
    private readonly FhirPathEvaluator _evaluator = new();

    private IElement ToElement(string json) =>
        ResourceJsonNode.Parse(json).ToElement(_r4Provider);

    private const string ObservationWithContainedPatientJson = @"{
        ""resourceType"": ""Observation"",
        ""id"": ""obs1"",
        ""status"": ""final"",
        ""code"": { ""coding"": [ { ""system"": ""http://loinc.org"", ""code"": ""1234-5"" } ] },
        ""subject"": { ""reference"": ""#p1"" },
        ""contained"": [
            { ""resourceType"": ""Patient"", ""id"": ""p1"" }
        ]
    }";

    [Fact]
    public void GivenContainedPatientReferencedByFragment_WhenNoElementResolverIsSupplied_ThenResolveFindsIt()
    {
        // Arrange
        var expr = _parser.Parse("Observation.subject.where(resolve() is Patient).exists()");
        var observation = ToElement(ObservationWithContainedPatientJson);
        var context = new EvaluationContext { Resource = observation };

        // Act
        var result = _evaluator.Evaluate(observation, expr, context).Single();

        // Assert
        result.Value.ShouldBe(true);
    }

    [Fact]
    public void GivenContainedPatientReferencedByFragment_WhenElementResolverOnlyDoesExternalLookups_ThenResolveStillFindsIt()
    {
        // Arrange
        var expr = _parser.Parse("Observation.subject.where(resolve() is Patient).exists()");
        var observation = ToElement(ObservationWithContainedPatientJson);
        var context = new FhirEvaluationContext
        {
            Resource = observation,
            ElementResolver = _ => null,
        };

        // Act
        var result = _evaluator.Evaluate(observation, expr, context).Single();

        // Assert
        result.Value.ShouldBe(true);
    }

    [Fact]
    public void GivenBundleWithSiblingEntries_WhenResolvingByTypeAndIdWithNoExternalResolver_ThenFindsSibling()
    {
        // Arrange
        var bundle = ToElement(@"{
            ""resourceType"": ""Bundle"",
            ""type"": ""collection"",
            ""entry"": [
                {
                    ""fullUrl"": ""http://example.org/fhir/Patient/1"",
                    ""resource"": { ""resourceType"": ""Patient"", ""id"": ""1"" }
                },
                {
                    ""fullUrl"": ""http://example.org/fhir/Observation/2"",
                    ""resource"": {
                        ""resourceType"": ""Observation"",
                        ""id"": ""2"",
                        ""status"": ""final"",
                        ""code"": { ""coding"": [ { ""system"": ""http://loinc.org"", ""code"": ""1234-5"" } ] },
                        ""subject"": { ""reference"": ""Patient/1"" }
                    }
                }
            ]
        }");
        var expr = _parser.Parse("Bundle.entry.resource.ofType(Observation).subject.resolve().id");
        var context = new EvaluationContext { Resource = bundle };

        // Act
        var result = _evaluator.Evaluate(bundle, expr, context).Single();

        // Assert
        result.Value.ShouldBe("1");
    }

    [Fact]
    public void GivenBundleWithSiblingEntries_WhenResolvingByFullUrlWithNoExternalResolver_ThenFindsSibling()
    {
        // Arrange
        var bundle = ToElement(@"{
            ""resourceType"": ""Bundle"",
            ""type"": ""collection"",
            ""entry"": [
                {
                    ""fullUrl"": ""http://example.org/fhir/Patient/1"",
                    ""resource"": { ""resourceType"": ""Patient"", ""id"": ""1"" }
                },
                {
                    ""fullUrl"": ""http://example.org/fhir/Observation/2"",
                    ""resource"": {
                        ""resourceType"": ""Observation"",
                        ""id"": ""2"",
                        ""status"": ""final"",
                        ""code"": { ""coding"": [ { ""system"": ""http://loinc.org"", ""code"": ""1234-5"" } ] },
                        ""subject"": { ""reference"": ""http://example.org/fhir/Patient/1"" }
                    }
                }
            ]
        }");
        var expr = _parser.Parse("Bundle.entry.resource.ofType(Observation).subject.resolve().id");
        var context = new EvaluationContext { Resource = bundle };

        // Act
        var result = _evaluator.Evaluate(bundle, expr, context).Single();

        // Assert
        result.Value.ShouldBe("1");
    }

    [Fact]
    public void GivenBareHashAtRootScope_WhenElementResolverWouldReturnNonNull_ThenReturnsEmptyWithoutConsultingResolver()
    {
        // Arrange - at root scope, bare '#' happens to yield the same outcome (empty, resolver never
        // consulted) in both reference engines, even though they get there differently and diverge
        // elsewhere (see FhirSpecificFunctions.ResolveReferenceValue for the full divergence). Firely's
        // ScopedNodeExtensions.Resolve<T> returns null for bare '#' at root scope - asserted by its own
        // ScopedNodeOnBaseTests, verified against Firely 5.13.1 and 6.0.1, 2026-08. HAPI's
        // FHIRPathEngine.funcResolve short-circuits every '#'-prefixed reference unconditionally, so it
        // never reaches the host resolver for '#' at any scope. This test proves Ignixa matches that
        // root-scope outcome: resolving '#' at root (where in-instance lookup returns null) with an
        // ElementResolver that would return a non-null element must still return empty, proving the
        // resolver was never consulted.
        var observation = ToElement(ObservationWithContainedPatientJson);
        var resolverElement = ToElement(@"{ ""resourceType"": ""Patient"", ""id"": ""resolver-decoy"" }");
        var expr = _parser.Parse("'#'.resolve()");
        var context = new FhirEvaluationContext
        {
            Resource = observation,
            ElementResolver = reference => reference == "#" ? resolverElement : null,
        };

        // Act
        var result = _evaluator.Evaluate(observation, expr, context).ToList();

        // Assert
        result.ShouldBeEmpty();
    }

    [Fact]
    public void GivenBareHashWithNoResourceOrRootResource_WhenElementResolverIsConfigured_ThenReturnsResolverResult()
    {
        // Arrange - with neither %resource nor %rootResource set there is no in-instance index at
        // all (referenceIndex is null), so there is no containment scope to decide bare '#' against.
        // Unlike the root-scope case above (which HAS an index and legitimately short-circuits to
        // empty), this must fall through to the host resolver like any other reference - matching
        // pre-existing behaviour and Firely's ScopedNodeExtensions.Resolve<T>, which only
        // short-circuits '#' when it actually has a ScopedNode scope to decide it from, and otherwise
        // defers to the external resolver.
        var input = ToElement(@"{ ""resourceType"": ""Patient"", ""id"": ""example"" }");
        var resolverElement = ToElement(@"{ ""resourceType"": ""Patient"", ""id"": ""resolved-via-host"" }");
        var expr = _parser.Parse("'#'.resolve()");
        var context = new FhirEvaluationContext
        {
            ElementResolver = reference => reference == "#" ? resolverElement : null,
        };

        // Act
        var result = _evaluator.Evaluate(input, expr, context).Single();

        // Assert
        result.ShouldBeSameAs(resolverElement);
    }

    [Fact]
    public void GivenRootResourceItself_WhenResolvingBareHash_ThenReturnsEmpty()
    {
        // Arrange - Firely's ScopedNodeOnBaseTests asserts Resolve("#") is null for a
        // non-contained root (verified against Firely 5.13.1 and 6.0.1, 2026-08); bare '#' only
        // resolves to the container from inside a contained resource's own scope (see the sibling
        // test below), not at root/self scope.
        var observation = ToElement(ObservationWithContainedPatientJson);
        var expr = _parser.Parse("'#'.resolve()");
        var context = new EvaluationContext { Resource = observation };

        // Act
        var result = _evaluator.Evaluate(observation, expr, context).ToList();

        // Assert
        result.ShouldBeEmpty();
    }

    [Fact]
    public void GivenReferenceResolvableBothInInstanceAndExternally_WhenResolving_ThenInInstanceResultWins()
    {
        // Arrange
        var observation = ToElement(ObservationWithContainedPatientJson);
        var externalPatient = ToElement(@"{ ""resourceType"": ""Patient"", ""id"": ""external-decoy"" }");
        var expr = _parser.Parse("Observation.subject.resolve().id");
        var context = new FhirEvaluationContext
        {
            Resource = observation,
            ElementResolver = _ => externalPatient,
        };

        // Act
        var result = _evaluator.Evaluate(observation, expr, context).Single();

        // Assert
        result.Value.ShouldBe("p1");
    }

    [Fact]
    public void GivenReferenceNotPresentInTheInstance_WhenResolving_ThenFallsBackToElementResolver()
    {
        // Arrange
        var observation = ToElement(ObservationWithContainedPatientJson);
        var externalPatient = ToElement(@"{ ""resourceType"": ""Patient"", ""id"": ""99"" }");
        var expr = _parser.Parse("'Patient/99'.resolve().id");
        var context = new FhirEvaluationContext
        {
            Resource = observation,
            ElementResolver = reference => reference == "Patient/99" ? externalPatient : null,
        };

        // Act
        var result = _evaluator.Evaluate(observation, expr, context).Single();

        // Assert
        result.Value.ShouldBe("99");
    }

    [Fact]
    public void GivenNoRootAndNoElementResolver_WhenResolving_ThenReturnsEmptyWithoutThrowing()
    {
        // Arrange
        var input = ToElement(@"{ ""resourceType"": ""Patient"", ""id"": ""example"" }");
        var expr = _parser.Parse("'Patient/1'.resolve()");
        var context = new EvaluationContext();

        // Act
        var result = _evaluator.Evaluate(input, expr, context).ToList();

        // Assert
        result.ShouldBeEmpty();
    }

    [Fact]
    public void GivenElementResolverThatThrows_WhenResolving_ThenReturnsEmptyWithoutThrowing()
    {
        // Arrange
        var observation = ToElement(ObservationWithContainedPatientJson);
        var expr = _parser.Parse("'Patient/unresolvable'.resolve()");
        var context = new FhirEvaluationContext
        {
            Resource = observation,
            ElementResolver = _ => throw new InvalidOperationException("host resolver failure"),
        };

        // Act
        var result = _evaluator.Evaluate(observation, expr, context).ToList();

        // Assert
        result.ShouldBeEmpty();
    }

    [Fact]
    public void GivenElementResolverThatThrowsOperationCanceledException_WhenResolving_ThenPropagates()
    {
        // Arrange - the host resolver is the one genuine trust boundary in resolve(), but
        // cancellation is not "reference not found": swallowing it would let a caller mistake
        // request abort for a missing reference.
        var observation = ToElement(ObservationWithContainedPatientJson);
        var expr = _parser.Parse("'Patient/unresolvable'.resolve()");
        var context = new FhirEvaluationContext
        {
            Resource = observation,
            ElementResolver = _ => throw new OperationCanceledException(),
        };

        // Act
        var act = () => _evaluator.Evaluate(observation, expr, context).ToList();

        // Assert
        Should.Throw<OperationCanceledException>(act);
    }

    [Fact]
    public void GivenElementResolverThatThrowsTaskCanceledException_WhenResolving_ThenPropagates()
    {
        // Arrange - TaskCanceledException derives from OperationCanceledException, so it already
        // propagates through the plain `is not OperationCanceledException` filter; pinned here so
        // the whole cancellation contract in the table below is covered by one test file.
        var observation = ToElement(ObservationWithContainedPatientJson);
        var expr = _parser.Parse("'Patient/unresolvable'.resolve()");
        var context = new FhirEvaluationContext
        {
            Resource = observation,
            ElementResolver = _ => throw new TaskCanceledException(),
        };

        // Act
        var act = () => _evaluator.Evaluate(observation, expr, context).ToList();

        // Assert
        Should.Throw<TaskCanceledException>(act);
    }

    [Fact]
    public void GivenElementResolverThatThrowsAggregateExceptionWrappingOperationCanceledException_WhenResolving_ThenPropagates()
    {
        // Arrange - a sync-over-async host resolver (.Result / .Wait()) wraps a cancelled task's
        // exception in an AggregateException, which does NOT derive from OperationCanceledException,
        // so a plain `is not OperationCanceledException` filter would swallow it. Cancellation must
        // still propagate rather than being reported as "reference not found".
        var observation = ToElement(ObservationWithContainedPatientJson);
        var expr = _parser.Parse("'Patient/unresolvable'.resolve()");
        var context = new FhirEvaluationContext
        {
            Resource = observation,
            ElementResolver = _ => throw new AggregateException(new OperationCanceledException()),
        };

        // Act
        var act = () => _evaluator.Evaluate(observation, expr, context).ToList();

        // Assert
        Should.Throw<AggregateException>(act);
    }

    [Fact]
    public void GivenElementResolverThatThrowsAggregateExceptionWrappingTaskCanceledException_WhenResolving_ThenPropagates()
    {
        // Arrange - same sync-over-async shape as above, but the wrapped exception is the
        // TaskCanceledException subtype specifically.
        var observation = ToElement(ObservationWithContainedPatientJson);
        var expr = _parser.Parse("'Patient/unresolvable'.resolve()");
        var context = new FhirEvaluationContext
        {
            Resource = observation,
            ElementResolver = _ => throw new AggregateException(new TaskCanceledException()),
        };

        // Act
        var act = () => _evaluator.Evaluate(observation, expr, context).ToList();

        // Assert
        Should.Throw<AggregateException>(act);
    }

    [Fact]
    public void GivenElementResolverThatThrowsAggregateExceptionWrappingOutOfMemoryException_WhenResolving_ThenPropagates()
    {
        // Arrange - OutOfMemoryException gets the same sync-over-async wrapping treatment as
        // cancellation and must propagate for the same reason: it is not "reference not found".
        var observation = ToElement(ObservationWithContainedPatientJson);
        var expr = _parser.Parse("'Patient/unresolvable'.resolve()");
        var context = new FhirEvaluationContext
        {
            Resource = observation,
            ElementResolver = _ => throw new AggregateException(new OutOfMemoryException()),
        };

        // Act
        var act = () => _evaluator.Evaluate(observation, expr, context).ToList();

        // Assert
        Should.Throw<AggregateException>(act);
    }

    [Fact]
    public void GivenRootWhoseChildrenThrows_WhenResolving_ThenPropagatesTheException()
    {
        // Arrange - a ThrowingElement is a broken IElement, not a reference that failed to
        // resolve: resolve()'s spec-mandated empty-on-failure contract covers the latter, not a
        // defect in our own in-instance resolution, which must propagate instead of being masked.
        var root = new ThrowingElement();
        var expr = _parser.Parse("'#'.resolve()");
        var context = new EvaluationContext { Resource = root };

        // Act
        var act = () => _evaluator.Evaluate(root, expr, context).ToList();

        // Assert
        Should.Throw<InvalidOperationException>(act);
    }

    [Fact]
    public void GivenReferenceElementWhoseValueIsTheReferenceString_WhenResolving_ThenInInstanceResolutionFindsIt()
    {
        // Arrange - pins the premise this test relies on: SchemaAwareElement reports
        // `Observation.subject.reference` as InstanceType "Reference" (a case-insensitive
        // name/type-match quirk), which is exactly what routes ExtractReferenceValue into its
        // "Value is the reference string" fallback rather than the bare-primitive branch. If that
        // heuristic is ever tightened this assertion fails loudly instead of the test silently
        // starting to cover a different branch.
        var observation = ToElement(ObservationWithContainedPatientJson);
        var referenceElement = _evaluator.Evaluate(
            observation,
            _parser.Parse("Observation.subject.reference"),
            new EvaluationContext { Resource = observation }).Single();
        referenceElement.InstanceType.ShouldBe("Reference");
        var expr = _parser.Parse("Observation.subject.reference.resolve().id");
        var context = new EvaluationContext { Resource = observation };

        // Act
        var result = _evaluator.Evaluate(observation, expr, context).Single();

        // Assert
        result.Value.ShouldBe("p1");
    }

    [Theory]
    [InlineData("canonical")]
    [InlineData("uri")]
    [InlineData("url")]
    public void GivenBarePrimitiveReferenceValue_WhenResolving_ThenInInstanceResolutionFindsIt(string primitiveType)
    {
        // Arrange
        var observation = ToElement(ObservationWithContainedPatientJson);
        var reference = new PrimitiveElement("#p1", primitiveType);
        var context = new EvaluationContext { Resource = observation };

        // Act
        var result = FhirSpecificFunctions.Resolve(new[] { reference }, context).Single();

        // Assert
        result.Children("id").Single().Value.ShouldBe("p1");
    }

    [Fact]
    public void GivenBareHashFromInsideAContainedResourceScope_WhenResolving_ThenReturnsTheParentNotTheContainedResource()
    {
        // Arrange
        // Verified against Firely 5.13.1 and 6.0.1, 2026-08 (ScopedNodeExtensions.Resolve<T>):
        // resolving '#' from inside a contained resource's own scope yields the parent
        // (RootResource), never the contained resource being evaluated - consistent with R4
        // references.html §2.3.0.8 ("there is only one container resource"). This mirrors
        // ValidationState.EnterContainedResource, which sets RootResource
        // to the parent while Resource becomes the contained resource being validated.
        var observation = ToElement(ObservationWithContainedPatientJson);
        var containedPatient = observation.Children("contained").Single();
        var expr = _parser.Parse("'#'.resolve()");
        var context = new EvaluationContext { Resource = containedPatient, RootResource = observation };

        // Act
        var result = _evaluator.Evaluate(containedPatient, expr, context).Single();

        // Assert
        result.ShouldBeSameAs(observation);
    }

    [Fact]
    public void GivenBareHashFromInsideABundleEntryResourceScope_WhenResolving_ThenReturnsEmpty()
    {
        // Arrange - Firely's ScopedNodeOnBaseTests asserts Resolve("#") is null for a Bundle entry
        // resource too, not just the Bundle root itself. This is exactly the case the naive
        // "RootResource != Resource" proxy would get wrong (it would misread this as a contained
        // scope and return the Bundle); the correct check is containment membership, not identity
        // inequality.
        var bundle = ToElement(@"{
            ""resourceType"": ""Bundle"",
            ""type"": ""collection"",
            ""entry"": [
                { ""resource"": { ""resourceType"": ""Patient"", ""id"": ""1"" } }
            ]
        }");
        var entryResource = bundle.Children("entry").Single().Children("resource").Single();
        var expr = _parser.Parse("'#'.resolve()");
        var context = new EvaluationContext { Resource = entryResource, RootResource = bundle };

        // Act
        var result = _evaluator.Evaluate(entryResource, expr, context).ToList();

        // Assert
        result.ShouldBeEmpty();
    }

    [Fact]
    public void GivenBundleEntryWithContainedFragment_WhenNoElementResolverIsSupplied_ThenResolveFindsEntryContained()
    {
        // Arrange - reviewer case A: a #frag reference inside a Bundle entry resource. Firely resolves
        // this; before this fix Ignixa returned empty because ReferenceIndex only indexed the Bundle
        // root's (non-existent) contained pool. No ElementResolver is supplied, so success proves
        // in-instance, entry-scoped resolution.
        var bundle = ToElement(@"{
            ""resourceType"": ""Bundle"",
            ""type"": ""collection"",
            ""entry"": [
                {
                    ""resource"": {
                        ""resourceType"": ""Patient"",
                        ""id"": ""patA"",
                        ""managingOrganization"": { ""reference"": ""#org1"" },
                        ""contained"": [ { ""resourceType"": ""Organization"", ""id"": ""org1"", ""name"": ""OrgA"" } ]
                    }
                }
            ]
        }");
        var expr = _parser.Parse("Bundle.entry.resource.ofType(Patient).managingOrganization.resolve().name");
        var context = new EvaluationContext { Resource = bundle };

        // Act
        var result = _evaluator.Evaluate(bundle, expr, context).Single();

        // Assert
        result.Value.ShouldBe("OrgA");
    }

    [Fact]
    public void GivenTwoBundleEntriesWithSameContainedId_WhenResolving_ThenEachResolvesToItsOwnContained()
    {
        // Arrange - both entries contain an Organization with id "org1" but different names. Each
        // entry is its own container boundary, so patA's #org1 must resolve to OrgA and patB's to
        // OrgB. A single merged pool would collapse both to whichever was indexed first.
        var bundle = ToElement(ContainerScopeTestFixtures.BundleWithTwoEntriesSharingContainedIdJson);
        var context = new EvaluationContext { Resource = bundle };

        // Act
        var resolvedA = _evaluator.Evaluate(
            bundle,
            _parser.Parse("Bundle.entry.resource.ofType(Patient).where(id = 'patA').managingOrganization.resolve().name"),
            context).Single();
        var resolvedB = _evaluator.Evaluate(
            bundle,
            _parser.Parse("Bundle.entry.resource.ofType(Patient).where(id = 'patB').managingOrganization.resolve().name"),
            context).Single();

        // Assert
        resolvedA.Value.ShouldBe("OrgA");
        resolvedB.Value.ShouldBe("OrgB");
    }

    [Fact]
    public void GivenParametersWithContainedFragmentsAtTopAndUnderPart_WhenResolving_ThenEachResolvesWithinItsOwnContainer()
    {
        // Arrange - the Parameters equivalent of the Bundle isolation case, including a resource
        // nested under parameter.part.resource. Each parameter.resource is a container boundary.
        var parameters = ToElement(ContainerScopeTestFixtures.ParametersWithContainedFragmentsJson);
        var context = new EvaluationContext { Resource = parameters };

        // Act
        var resolvedTop = _evaluator.Evaluate(
            parameters,
            _parser.Parse("Parameters.parameter.resource.ofType(Patient).where(id = 'ptop').managingOrganization.resolve().name"),
            context).Single();
        var resolvedNested = _evaluator.Evaluate(
            parameters,
            _parser.Parse("Parameters.parameter.part.resource.ofType(Patient).where(id = 'pnested').managingOrganization.resolve().name"),
            context).Single();

        // Assert
        resolvedTop.Value.ShouldBe("TopOrg");
        resolvedNested.Value.ShouldBe("NestedOrg");
    }

    [Fact]
    public void GivenBundleEntryBWithNoContainedOfItsOwn_WhenResolvingItsFragmentReference_ThenReturnsEmptyNotEntryAsContained()
    {
        // Arrange - end-to-end counterpart of the ReferenceIndex-level negative isolation test.
        // Every prior isolation test only asserted that entry A finds its OWN #org1, which still
        // holds even if resolution leaked to sibling pools; this pins the negative direction
        // (R4 references.html §2.3.0.8): entry B declares no contained of its own, so
        // managingOrganization.resolve() must be empty, never entry A's Organization.
        var bundle = ToElement(ContainerScopeTestFixtures.BundleWhereOnlyOneEntryHasContainedIdJson);
        var context = new EvaluationContext { Resource = bundle };

        // Act
        var resolved = _evaluator.Evaluate(
            bundle,
            _parser.Parse("Bundle.entry.resource.ofType(Patient).where(id = 'patB').managingOrganization.resolve()"),
            context).ToList();

        // Assert
        resolved.ShouldBeEmpty();
    }

    [Fact]
    public void GivenParametersPartWithNoContainedOfItsOwn_WhenResolvingItsFragmentReference_ThenReturnsEmptyNotTopLevelContained()
    {
        // Arrange - the Parameters equivalent of the Bundle negative-isolation case above: the
        // resource nested under parameter.part.resource references #org1 but declares no contained
        // of its own, so it must never see the top-level parameter.resource's contained Organization.
        var parameters = ToElement(ContainerScopeTestFixtures.ParametersWhereOnlyOneEntryHasContainedIdJson);
        var context = new EvaluationContext { Resource = parameters };

        // Act
        var resolved = _evaluator.Evaluate(
            parameters,
            _parser.Parse("Parameters.parameter.part.resource.ofType(Patient).where(id = 'pnested').managingOrganization.resolve()"),
            context).ToList();

        // Assert
        resolved.ShouldBeEmpty();
    }

    [Fact]
    public void GivenElevenBundleEntriesWithEntryOneAndEntryTenSharingContainedId_WhenResolvingEachEntrysFragment_ThenEachResolvesToItsOwnContained()
    {
        // Arrange - regression coverage for SelectContainedPool's longest-prefix loop across many
        // candidate scopes end-to-end through the evaluator, including a two-digit bracket index.
        // This does NOT exercise the IsInScope trailing-boundary check itself: through the real
        // evaluator, focusLocation is always a genuine IElement.Location, and
        // "Bundle.entry[10].resource" is not a plain string-prefix of "Bundle.entry[1].resource" at
        // all (the closing ']' diverges from the next index digit immediately), so no real parsed
        // Location can construct the collision the guard defends against. A unit-level test in
        // ReferenceIndexTests (GivenFocusLocationSharingContainerPrefixWithoutDotBoundary_...) hand
        // -crafts a focusLocation string via the public Resolve(reference, focusLocation) API to
        // pin that guard instead, since Resolve's focusLocation parameter does not require a real
        // Location.
        var bundle = ToElement(ContainerScopeTestFixtures.BundleWithElevenEntriesSharingContainedIdAtEntryOneAndTenJson);
        var context = new EvaluationContext { Resource = bundle };

        // Act
        var resolvedEntryOne = _evaluator.Evaluate(
            bundle,
            _parser.Parse("Bundle.entry.resource.ofType(Patient).where(id = 'pat1').managingOrganization.resolve().name"),
            context).Single();
        var resolvedEntryTen = _evaluator.Evaluate(
            bundle,
            _parser.Parse("Bundle.entry.resource.ofType(Patient).where(id = 'pat10').managingOrganization.resolve().name"),
            context).Single();

        // Assert
        resolvedEntryOne.Value.ShouldBe("OrgAtEntryOne");
        resolvedEntryTen.Value.ShouldBe("OrgAtEntryTen");
    }

    [Fact]
    public void GivenBareHashFromContainedInsideBundleEntry_WhenResolving_ThenReturnsEntryResourceNotBundle()
    {
        // Arrange - bare '#' from inside a contained resource resolves to that contained resource's
        // container, which inside a Bundle entry is the ENTRY resource (Patient patA), never the
        // Bundle root (R4 references.html §2.3.0.8: "there is only one container resource"). Children()
        // returns a fresh wrapper each call, so the entry resource cannot be compared by identity;
        // assert on its content instead.
        var bundle = ToElement(ContainerScopeTestFixtures.BundleWithTwoEntriesSharingContainedIdJson);
        var containedOrg = bundle.Children("entry")[0].Children("resource").Single().Children("contained").Single();
        var expr = _parser.Parse("'#'.resolve()");
        var context = new EvaluationContext { Resource = containedOrg, RootResource = bundle };

        // Act
        var result = _evaluator.Evaluate(containedOrg, expr, context).Single();

        // Assert
        result.InstanceType.ShouldBe("Patient");
        result.Children("id").Single().Value.ShouldBe("patA");
    }

    [Fact]
    public void GivenMultiElementFocusWithTwoResolvableReferences_WhenResolving_ThenReturnsBothInOrder()
    {
        // Arrange - GitHub issue #401 review gap: every pre-existing test narrows focus to a single
        // element (e.g. ofType(Observation) on a Bundle). A real 0..* expression such as
        // Observation.performer.resolve() must accumulate every resolvable element, in focus order,
        // not just the first one.
        var observation = ToElement(@"{
            ""resourceType"": ""Observation"",
            ""id"": ""obs-multi"",
            ""status"": ""final"",
            ""code"": { ""coding"": [ { ""system"": ""http://loinc.org"", ""code"": ""1234-5"" } ] },
            ""performer"": [
                { ""reference"": ""#pr1"" },
                { ""reference"": ""#pr2"" }
            ],
            ""contained"": [
                { ""resourceType"": ""Practitioner"", ""id"": ""pr1"" },
                { ""resourceType"": ""Practitioner"", ""id"": ""pr2"" }
            ]
        }");
        var expr = _parser.Parse("Observation.performer.resolve().id");
        var context = new EvaluationContext { Resource = observation };

        // Act
        var result = _evaluator.Evaluate(observation, expr, context).ToList();

        // Assert
        result.Count.ShouldBe(2);
        result[0].Value.ShouldBe("pr1");
        result[1].Value.ShouldBe("pr2");
    }

    [Fact]
    public void GivenMultiElementFocusWithOneResolvableAndOneUnresolvableReference_WhenResolving_ThenReturnsOnlyTheResolvedOneWithoutThrowing()
    {
        // Arrange - a mixed focus where one reference resolves in-instance and the other resolves
        // nowhere (no ElementResolver is supplied) must yield exactly the one resolved element,
        // not throw, and not silently drop the rest of the loop.
        var observation = ToElement(@"{
            ""resourceType"": ""Observation"",
            ""id"": ""obs-mixed"",
            ""status"": ""final"",
            ""code"": { ""coding"": [ { ""system"": ""http://loinc.org"", ""code"": ""1234-5"" } ] },
            ""performer"": [
                { ""reference"": ""#pr1"" },
                { ""reference"": ""#unknown"" }
            ],
            ""contained"": [
                { ""resourceType"": ""Practitioner"", ""id"": ""pr1"" }
            ]
        }");
        var expr = _parser.Parse("Observation.performer.resolve().id");
        var context = new EvaluationContext { Resource = observation };

        // Act
        var result = _evaluator.Evaluate(observation, expr, context).ToList();

        // Assert
        result.Single().Value.ShouldBe("pr1");
    }

    [Fact]
    public void GivenMultiElementFocusWithAnEmptyReferenceFollowedByAResolvableOne_WhenResolving_ThenSkipsTheEmptyOneAndStillResolvesTheLater()
    {
        // Arrange - this is the case that specifically catches "continue" degrading to "break" in
        // the resolve() accumulation loop: the first performer has no reference value at all (an
        // empty Reference object), which must be skipped, not treated as a reason to stop looking
        // at the rest of the focus.
        var observation = ToElement(@"{
            ""resourceType"": ""Observation"",
            ""id"": ""obs-skip"",
            ""status"": ""final"",
            ""code"": { ""coding"": [ { ""system"": ""http://loinc.org"", ""code"": ""1234-5"" } ] },
            ""performer"": [
                { },
                { ""reference"": ""#pr2"" }
            ],
            ""contained"": [
                { ""resourceType"": ""Practitioner"", ""id"": ""pr2"" }
            ]
        }");
        var expr = _parser.Parse("Observation.performer.resolve().id");
        var context = new EvaluationContext { Resource = observation };

        // Act
        var result = _evaluator.Evaluate(observation, expr, context).ToList();

        // Assert
        result.Single().Value.ShouldBe("pr2");
    }

    [Fact]
    public void GivenUnresolvedFragmentReference_WhenElementResolverCanResolveIt_ThenFallsBackToResolver()
    {
        // Arrange - deliberate Firely-over-HAPI choice. Firely short-circuits only the exact string
        // "#": for an unresolved "#unknownId" its ScopedNodeExtensions.Resolve<T> still consults the
        // external resolver (its own ScopedNodeOnBaseTests asserts Assert.IsNull(inner7.Resolve("#d",
        // externalResolve)); Assert.AreEqual("#d", lastUrlResolved);) - the host WAS called. HAPI
        // instead short-circuits every "#"-prefixed reference and never consults the host resolver
        // for any fragment. Ignixa follows Firely: a "#id" that misses the in-instance index falls
        // through to the host ElementResolver, while a bare "#" never does (see the sibling
        // bare-hash tests above).
        var observation = ToElement(ObservationWithContainedPatientJson);
        var resolverElement = ToElement(@"{ ""resourceType"": ""Patient"", ""id"": ""from-resolver"" }");
        var expr = _parser.Parse("'#unknownId'.resolve()");
        var context = new FhirEvaluationContext
        {
            Resource = observation,
            ElementResolver = reference => reference == "#unknownId" ? resolverElement : null,
        };

        // Act
        var result = _evaluator.Evaluate(observation, expr, context).Single();

        // Assert
        result.ShouldBeSameAs(resolverElement);
    }

    [Fact]
    public void GivenBundleEntryWithVersionedReference_WhenResolvingThroughTheEvaluator_ThenFindsTheVersionedEntry()
    {
        // Arrange - versioned Type/id/_history/vid resolution is covered at the ReferenceIndex unit
        // level (ReferenceIndexTests), but not end-to-end through resolve().
        var bundle = ToElement(@"{
            ""resourceType"": ""Bundle"",
            ""type"": ""collection"",
            ""entry"": [
                {
                    ""resource"": {
                        ""resourceType"": ""Patient"",
                        ""id"": ""1"",
                        ""meta"": { ""versionId"": ""3"" }
                    }
                },
                {
                    ""resource"": {
                        ""resourceType"": ""Observation"",
                        ""id"": ""2"",
                        ""status"": ""final"",
                        ""code"": { ""coding"": [ { ""system"": ""http://loinc.org"", ""code"": ""1234-5"" } ] },
                        ""subject"": { ""reference"": ""Patient/1/_history/3"" }
                    }
                }
            ]
        }");
        var expr = _parser.Parse("Bundle.entry.resource.ofType(Observation).subject.resolve().id");
        var context = new EvaluationContext { Resource = bundle };

        // Act
        var result = _evaluator.Evaluate(bundle, expr, context).Single();

        // Assert
        result.Value.ShouldBe("1");
    }

    [Fact]
    public void GivenFocusElementWithResourceReferenceInstanceType_WhenResolving_ThenExtractsReferenceValueAndResolves()
    {
        // Arrange - ExtractReferenceValue treats InstanceType "ResourceReference" the same as
        // "Reference" (see ElementSearchIndexer, which recognizes both names for the same FHIR
        // Reference concept), but no existing test ever constructs an element with that InstanceType.
        var observation = ToElement(ObservationWithContainedPatientJson);
        var reference = new ResourceReferenceElement("#p1");
        var context = new EvaluationContext { Resource = observation };

        // Act
        var result = FhirSpecificFunctions.Resolve(new[] { reference }, context).Single();

        // Assert
        result.Children("id").Single().Value.ShouldBe("p1");
    }

    /// <summary>
    /// Minimal <see cref="IElement"/> whose <see cref="Children"/> always throws, used to prove that
    /// a pathological instance DOES make <c>resolve()</c> throw while building its in-instance index -
    /// a defect in our own engine propagates instead of being masked as an empty resolve() result.
    /// </summary>
    private sealed class ThrowingElement : IElement
    {
        public string Name => "root";
        public object? Value => null;
        public string InstanceType => "Observation";
        public string Location => "Observation";
        public IType? Type => null;
        public bool HasPrimitiveValue => false;

        public IReadOnlyList<IElement> Children(string? name = null) =>
            throw new InvalidOperationException("simulated corrupt instance");

        public T? Meta<T>() where T : class => null;
    }

    /// <summary>
    /// Minimal <see cref="IElement"/> for a bare primitive reference value
    /// (<c>string</c>/<c>uri</c>/<c>canonical</c>/<c>url</c>), with no <c>reference</c> child.
    /// </summary>
    private sealed class PrimitiveElement : IElement
    {
        public PrimitiveElement(object value, string instanceType)
        {
            Value = value;
            InstanceType = instanceType;
        }

        public string Name => string.Empty;
        public object? Value { get; }
        public string InstanceType { get; }
        public string Location => string.Empty;
        public IType? Type => null;
        public bool HasPrimitiveValue => true;

        public IReadOnlyList<IElement> Children(string? name = null) => Array.Empty<IElement>();

        public T? Meta<T>() where T : class => null;
    }

    /// <summary>
    /// Minimal <see cref="IElement"/> whose <see cref="InstanceType"/> is the alternate name
    /// "ResourceReference" rather than "Reference", with a single "reference" child holding the
    /// reference string - exercises the other half of ExtractReferenceValue's type-name check.
    /// </summary>
    private sealed class ResourceReferenceElement : IElement
    {
        private readonly string _referenceValue;

        public ResourceReferenceElement(string referenceValue)
        {
            _referenceValue = referenceValue;
        }

        public string Name => string.Empty;
        public object? Value => null;
        public string InstanceType => "ResourceReference";
        public string Location => string.Empty;
        public IType? Type => null;
        public bool HasPrimitiveValue => false;

        public IReadOnlyList<IElement> Children(string? name = null) =>
            name is null or "reference"
                ? new IElement[] { new PrimitiveElement(_referenceValue, "string") }
                : Array.Empty<IElement>();

        public T? Meta<T>() where T : class => null;
    }
}
