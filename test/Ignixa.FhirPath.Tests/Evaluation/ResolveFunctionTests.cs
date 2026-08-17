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
        // Arrange - Firely (5.13.1/6.0.1) and HAPI (org.hl7.fhir.core 8.10.0) both short-circuit a
        // bare '#' at root scope and never consult the host resolver. This test proves Ignixa does the
        // same: resolving '#' at root (where in-instance lookup returns null) with an ElementResolver
        // that would return a non-null element must still return empty, proving the resolver was never
        // consulted.
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
        // pre-existing behaviour and Firely's ScopedNode, which only short-circuits '#' for a
        // ScopedNode and otherwise defers to the external resolver.
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
        // non-contained root (measured against Firely 5.13.1/6.0.1); bare '#' only resolves to the
        // container from inside a contained resource's own scope (see the sibling test below), not
        // at root/self scope.
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
        // Arrange
        var observation = ToElement(ObservationWithContainedPatientJson);
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
        // Measured against Firely 5.13.1/6.0.1 (ScopedNode): resolving '#' from inside a contained
        // resource's own scope yields the parent (RootResource), never the contained resource being
        // evaluated - consistent with R4 references.html §2.3.0.8 ("there is only one container
        // resource"). This mirrors ValidationState.EnterContainedResource, which sets RootResource
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
        var bundle = ToElement(BundleWithTwoEntriesSharingContainedIdJson);
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
        var parameters = ToElement(ParametersWithContainedFragmentsJson);
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
    public void GivenBareHashFromContainedInsideBundleEntry_WhenResolving_ThenReturnsEntryResourceNotBundle()
    {
        // Arrange - bare '#' from inside a contained resource resolves to that contained resource's
        // container, which inside a Bundle entry is the ENTRY resource (Patient patA), never the
        // Bundle root (R4 references.html §2.3.0.8: "there is only one container resource"). Children()
        // returns a fresh wrapper each call, so the entry resource cannot be compared by identity;
        // assert on its content instead.
        var bundle = ToElement(BundleWithTwoEntriesSharingContainedIdJson);
        var containedOrg = bundle.Children("entry")[0].Children("resource").Single().Children("contained").Single();
        var expr = _parser.Parse("'#'.resolve()");
        var context = new EvaluationContext { Resource = containedOrg, RootResource = bundle };

        // Act
        var result = _evaluator.Evaluate(containedOrg, expr, context).Single();

        // Assert
        result.InstanceType.ShouldBe("Patient");
        result.Children("id").Single().Value.ShouldBe("patA");
    }

    private const string BundleWithTwoEntriesSharingContainedIdJson = @"{
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
            },
            {
                ""resource"": {
                    ""resourceType"": ""Patient"",
                    ""id"": ""patB"",
                    ""managingOrganization"": { ""reference"": ""#org1"" },
                    ""contained"": [ { ""resourceType"": ""Organization"", ""id"": ""org1"", ""name"": ""OrgB"" } ]
                }
            }
        ]
    }";

    private const string ParametersWithContainedFragmentsJson = @"{
        ""resourceType"": ""Parameters"",
        ""parameter"": [
            {
                ""name"": ""top"",
                ""resource"": {
                    ""resourceType"": ""Patient"",
                    ""id"": ""ptop"",
                    ""managingOrganization"": { ""reference"": ""#org1"" },
                    ""contained"": [ { ""resourceType"": ""Organization"", ""id"": ""org1"", ""name"": ""TopOrg"" } ]
                }
            },
            {
                ""name"": ""group"",
                ""part"": [
                    {
                        ""name"": ""nested"",
                        ""resource"": {
                            ""resourceType"": ""Patient"",
                            ""id"": ""pnested"",
                            ""managingOrganization"": { ""reference"": ""#org1"" },
                            ""contained"": [ { ""resourceType"": ""Organization"", ""id"": ""org1"", ""name"": ""NestedOrg"" } ]
                        }
                    }
                ]
            }
        ]
    }";

    /// <summary>
    /// Minimal <see cref="IElement"/> whose <see cref="Children"/> always throws, used to prove that
    /// a pathological instance cannot make <c>resolve()</c> throw while building its in-instance index.
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
}
