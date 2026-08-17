// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Ignixa.Specification.Extensions;
using Shouldly;
using Xunit;

namespace Ignixa.FhirPath.Tests.Evaluation;

/// <summary>
/// Unit tests for <see cref="ReferenceIndex"/> contained, bundle, container-scope, and miss
/// resolution.
/// </summary>
public class ReferenceIndexTests
{
    private readonly IFhirSchemaProvider _r4Provider = FhirVersion.R4.GetSchemaProvider();

    private IElement ToElement(string json) =>
        ResourceJsonNode.Parse(json).ToElement(_r4Provider);

    [Fact]
    public void GivenContainedResource_WhenResolvingFragment_ThenReturnsContained()
    {
        // Arrange
        var element = ToElement(@"{
            ""resourceType"": ""Patient"",
            ""id"": ""example"",
            ""contained"": [
                { ""resourceType"": ""Practitioner"", ""id"": ""p1"" }
            ]
        }");
        var index = ReferenceIndex.Build(element);

        // Act
        var resolved = index.Resolve("#p1");

        // Assert
        resolved.ShouldNotBeNull();
        resolved!.InstanceType.ShouldBe("Practitioner");
    }

    [Fact]
    public void GivenBundle_WhenResolvingByTypeAndId_ThenReturnsEntryResource()
    {
        // Arrange
        var element = ToElement(@"{
            ""resourceType"": ""Bundle"",
            ""type"": ""collection"",
            ""entry"": [
                {
                    ""fullUrl"": ""http://example.org/fhir/Patient/1"",
                    ""resource"": { ""resourceType"": ""Patient"", ""id"": ""1"" }
                }
            ]
        }");
        var index = ReferenceIndex.Build(element);

        // Act
        var byTypeId = index.Resolve("Patient/1");
        var byFullUrl = index.Resolve("http://example.org/fhir/Patient/1");

        // Assert
        byTypeId.ShouldNotBeNull();
        byTypeId!.InstanceType.ShouldBe("Patient");
        byFullUrl.ShouldNotBeNull();
        byFullUrl!.InstanceType.ShouldBe("Patient");
    }

    [Fact]
    public void GivenBundleEntryWithVersionId_WhenResolvingVersionedReference_ThenReturnsEntryResource()
    {
        // Arrange
        var element = ToElement(@"{
            ""resourceType"": ""Bundle"",
            ""type"": ""collection"",
            ""entry"": [
                {
                    ""resource"": {
                        ""resourceType"": ""Patient"",
                        ""id"": ""1"",
                        ""meta"": { ""versionId"": ""3"" }
                    }
                }
            ]
        }");
        var index = ReferenceIndex.Build(element);

        // Act
        var resolved = index.Resolve("Patient/1/_history/3");

        // Assert
        resolved.ShouldNotBeNull();
        resolved!.InstanceType.ShouldBe("Patient");
    }

    [Fact]
    public void GivenUnknownReference_WhenResolving_ThenReturnsNull()
    {
        // Arrange
        var element = ToElement(@"{
            ""resourceType"": ""Patient"",
            ""id"": ""example"",
            ""contained"": [ { ""resourceType"": ""Practitioner"", ""id"": ""p1"" } ]
        }");
        var index = ReferenceIndex.Build(element);

        // Act & Assert
        index.Resolve("#missing").ShouldBeNull();
        index.Resolve("Patient/999").ShouldBeNull();
        index.Resolve(string.Empty).ShouldBeNull();
    }

    [Fact]
    public void GivenNonBundleRoot_WhenResolvingRelativeReference_ThenReturnsNull()
    {
        // Arrange
        var element = ToElement(@"{ ""resourceType"": ""Patient"", ""id"": ""1"" }");
        var index = ReferenceIndex.Build(element);

        // Act & Assert
        index.Resolve("Patient/1").ShouldBeNull();
    }

    [Fact]
    public void GivenResourceRoot_WhenResolvingBareHashViaResolve_ThenReturnsNull()
    {
        // Arrange - Resolve(string) has no notion of the current evaluation scope, so it can
        // never decide what bare '#' means; ResolveContainerScope handles that instead.
        var element = ToElement(@"{
            ""resourceType"": ""Patient"",
            ""id"": ""example"",
            ""contained"": [ { ""resourceType"": ""Practitioner"", ""id"": ""p1"" } ]
        }");
        var index = ReferenceIndex.Build(element);

        // Act
        var resolved = index.Resolve("#");

        // Assert
        resolved.ShouldBeNull();
    }

    [Fact]
    public void GivenBundleRoot_WhenResolvingBareHashViaResolve_ThenReturnsNull()
    {
        // Arrange
        var element = ToElement(@"{
            ""resourceType"": ""Bundle"",
            ""type"": ""collection"",
            ""entry"": [
                {
                    ""resource"": { ""resourceType"": ""Patient"", ""id"": ""1"" }
                }
            ]
        }");
        var index = ReferenceIndex.Build(element);

        // Act
        var resolved = index.Resolve("#");

        // Assert
        resolved.ShouldBeNull();
    }

    [Fact]
    public void GivenContainedResource_WhenResolvingContainerScope_ThenReturnsRoot()
    {
        // Arrange
        var element = ToElement(@"{
            ""resourceType"": ""Patient"",
            ""id"": ""example"",
            ""contained"": [ { ""resourceType"": ""Practitioner"", ""id"": ""p1"" } ]
        }");
        var index = ReferenceIndex.Build(element);
        var contained = element.Children("contained").Single();

        // Act
        var resolved = index.ResolveContainerScope(contained);

        // Assert
        resolved.ShouldBeSameAs(element);
    }

    [Fact]
    public void GivenContainedResourceWithEmptyId_WhenResolvingContainerScope_ThenStillReturnsRoot()
    {
        // Arrange - IndexContained skips empty ids for the #id lookup table, but containment
        // membership must not depend on whether the contained resource has an id at all.
        var element = ToElement(@"{
            ""resourceType"": ""Patient"",
            ""id"": ""example"",
            ""contained"": [ { ""resourceType"": ""Practitioner"", ""id"": """" } ]
        }");
        var index = ReferenceIndex.Build(element);
        var contained = element.Children("contained").Single();

        // Act
        var resolved = index.ResolveContainerScope(contained);

        // Assert
        resolved.ShouldBeSameAs(element);
    }

    [Fact]
    public void GivenRootItself_WhenResolvingContainerScope_ThenReturnsNull()
    {
        // Arrange - the root is not one of its own contained resources, so evaluating '#' at root
        // scope must not resolve to the root; matches Firely's ScopedNodeOnBaseTests, which asserts
        // Resolve("#") is null for a non-contained root.
        var element = ToElement(@"{
            ""resourceType"": ""Patient"",
            ""id"": ""example"",
            ""contained"": [ { ""resourceType"": ""Practitioner"", ""id"": ""p1"" } ]
        }");
        var index = ReferenceIndex.Build(element);

        // Act
        var resolved = index.ResolveContainerScope(element);

        // Assert
        resolved.ShouldBeNull();
    }

    [Fact]
    public void GivenBundleEntryResource_WhenResolvingContainerScope_ThenReturnsNull()
    {
        // Arrange - a Bundle entry resource is indexed by fullUrl/Type-id, not as a contained
        // resource, so it must not be recognized as being in containment scope of the Bundle.
        var element = ToElement(@"{
            ""resourceType"": ""Bundle"",
            ""type"": ""collection"",
            ""entry"": [
                {
                    ""resource"": { ""resourceType"": ""Patient"", ""id"": ""1"" }
                }
            ]
        }");
        var index = ReferenceIndex.Build(element);
        var entryResource = element.Children("entry").Single().Children("resource").Single();

        // Act
        var resolved = index.ResolveContainerScope(entryResource);

        // Assert
        resolved.ShouldBeNull();
    }

    [Fact]
    public void GivenNullCurrentResource_WhenResolvingContainerScope_ThenReturnsNull()
    {
        // Arrange
        var element = ToElement(@"{ ""resourceType"": ""Patient"", ""id"": ""example"" }");
        var index = ReferenceIndex.Build(element);

        // Act
        var resolved = index.ResolveContainerScope(null);

        // Assert
        resolved.ShouldBeNull();
    }

    [Fact]
    public void GivenUnknownFragment_WhenResolving_ThenStillReturnsNull()
    {
        // Arrange
        var element = ToElement(@"{
            ""resourceType"": ""Patient"",
            ""id"": ""example"",
            ""contained"": [ { ""resourceType"": ""Practitioner"", ""id"": ""p1"" } ]
        }");
        var index = ReferenceIndex.Build(element);

        // Act & Assert
        index.Resolve("#unknown").ShouldBeNull();
    }

    [Fact]
    public void GivenParametersRoot_WhenResolvingTopLevelParameterResourceByTypeAndId_ThenReturnsResource()
    {
        // Arrange
        var element = BuildParametersWithNestedResources();
        var index = ReferenceIndex.Build(element);

        // Act
        var resolved = index.Resolve("Patient/1");

        // Assert
        resolved.ShouldNotBeNull();
        resolved!.InstanceType.ShouldBe("Patient");
    }

    [Fact]
    public void GivenParametersRoot_WhenResolvingResourceNestedUnderPart_ThenRecursionFindsResource()
    {
        // Arrange - the resource lives under parameter.part.resource, one level deeper than the
        // top-level parameter.resource case; this only resolves if IndexParameterList's recursive
        // call into "part" actually runs.
        var element = BuildParametersWithNestedResources();
        var index = ReferenceIndex.Build(element);

        // Act
        var resolved = index.Resolve("Practitioner/2");

        // Assert
        resolved.ShouldNotBeNull();
        resolved!.InstanceType.ShouldBe("Practitioner");
    }

    [Fact]
    public void GivenParametersRoot_WhenResolvingByFullUrl_ThenReturnsNull()
    {
        // Arrange - a Parameters entry has no fullUrl, unlike a Bundle entry, so no Type/id
        // resource should ever be reachable through a fullUrl-shaped key.
        var element = BuildParametersWithNestedResources();
        var index = ReferenceIndex.Build(element);

        // Act & Assert
        index.Resolve("http://example.org/fhir/Patient/1").ShouldBeNull();
    }

    [Fact]
    public void GivenParametersRoot_WhenResolvingUnknownTypeAndId_ThenReturnsNull()
    {
        // Arrange
        var element = BuildParametersWithNestedResources();
        var index = ReferenceIndex.Build(element);

        // Act & Assert
        index.Resolve("Patient/999").ShouldBeNull();
    }

    [Fact]
    public void GivenBundleEntryWithContained_WhenResolvingFragmentScopedToThatEntry_ThenReturnsThatEntrysContained()
    {
        // Arrange - the fragment #org1 lives only inside a Bundle entry resource, never on the
        // Bundle root (a Bundle is not a DomainResource), so it resolves only when the lookup is
        // scoped to the enclosing entry via that entry resource's Location prefix.
        var element = ToElement(BundleWithTwoEntriesSharingContainedIdJson);
        var index = ReferenceIndex.Build(element);
        var entryA = element.Children("entry")[0].Children("resource").Single();

        // Act
        var resolved = index.Resolve("#org1", entryA.Location);

        // Assert
        resolved.ShouldNotBeNull();
        resolved!.InstanceType.ShouldBe("Organization");
        resolved.Children("name").Single().Value.ShouldBe("OrgA");
    }

    [Fact]
    public void GivenTwoBundleEntriesSharingContainedId_WhenResolvingFragmentPerEntry_ThenEachResolvesToItsOwnContained()
    {
        // Arrange - both entries contain an Organization with id "org1" but different names. This is
        // the containment-isolation guarantee (R4 references.html §2.3.0.8): a fragment inside entry
        // A must never see entry B's contained pool. If the two pools were merged into one, the
        // first-wins TryAdd would make both look up the same "org1" and this test would fail.
        var element = ToElement(BundleWithTwoEntriesSharingContainedIdJson);
        var index = ReferenceIndex.Build(element);
        var entryA = element.Children("entry")[0].Children("resource").Single();
        var entryB = element.Children("entry")[1].Children("resource").Single();

        // Act
        var resolvedA = index.Resolve("#org1", entryA.Location);
        var resolvedB = index.Resolve("#org1", entryB.Location);

        // Assert
        resolvedA!.Children("name").Single().Value.ShouldBe("OrgA");
        resolvedB!.Children("name").Single().Value.ShouldBe("OrgB");
    }

    [Fact]
    public void GivenBundleEntryContained_WhenResolvingFragmentWithEmptyFocusLocation_ThenReturnsNull()
    {
        // Arrange - an empty focus Location must fall back to the ROOT container's contained pool,
        // which for a Bundle is empty; it must never behave as a wildcard that matches an entry's
        // scope. A null focus Location is treated the same way.
        var element = ToElement(BundleWithTwoEntriesSharingContainedIdJson);
        var index = ReferenceIndex.Build(element);

        // Act & Assert
        index.Resolve("#org1", string.Empty).ShouldBeNull();
        index.Resolve("#org1", null).ShouldBeNull();
    }

    [Fact]
    public void GivenParametersEntriesAtTopAndUnderPart_WhenResolvingFragmentPerEntry_ThenEachResolvesWithinItsOwnContainer()
    {
        // Arrange - a top-level parameter.resource and a resource nested under parameter.part.resource
        // each contain an Organization with the same id "org1"; each fragment must resolve within its
        // own container boundary, exactly as for Bundle entries.
        var element = ToElement(ParametersWithContainedFragmentsJson);
        var index = ReferenceIndex.Build(element);
        var topResource = element.Children("parameter")[0].Children("resource").Single();
        var nestedResource = element.Children("parameter")[1].Children("part").Single().Children("resource").Single();

        // Act
        var resolvedTop = index.Resolve("#org1", topResource.Location);
        var resolvedNested = index.Resolve("#org1", nestedResource.Location);

        // Assert
        resolvedTop!.Children("name").Single().Value.ShouldBe("TopOrg");
        resolvedNested!.Children("name").Single().Value.ShouldBe("NestedOrg");
    }

    [Fact]
    public void GivenContainedResourceWithEmptyLocation_WhenResolvingContainerScope_ThenReturnsNull()
    {
        // Arrange - several first-party IElement implementations return string.Empty for Location.
        // An empty Location must never be treated as a member of any container's contained pool,
        // otherwise bare '#' would falsely resolve to the root (Finding 2).
        var element = ToElement(@"{
            ""resourceType"": ""Patient"",
            ""id"": ""example"",
            ""contained"": [ { ""resourceType"": ""Practitioner"", ""id"": ""p1"" } ]
        }");
        var index = ReferenceIndex.Build(element);

        // Act
        var resolved = index.ResolveContainerScope(new StubElement(string.Empty));

        // Assert
        resolved.ShouldBeNull();
    }

    [Fact]
    public void GivenContainedResourceWithNullLocation_WhenResolvingContainerScope_ThenReturnsNullWithoutThrowing()
    {
        // Arrange - a hand-rolled IElement may return null for Location; HashSet/Dictionary lookups
        // with StringComparer.Ordinal throw on null, so resolve() must guard against it (Finding 2).
        var element = ToElement(@"{
            ""resourceType"": ""Patient"",
            ""id"": ""example"",
            ""contained"": [ { ""resourceType"": ""Practitioner"", ""id"": ""p1"" } ]
        }");
        var index = ReferenceIndex.Build(element);

        // Act
        var resolved = index.ResolveContainerScope(new StubElement(null!));

        // Assert
        resolved.ShouldBeNull();
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

    private IElement BuildParametersWithNestedResources() => ToElement(@"{
        ""resourceType"": ""Parameters"",
        ""parameter"": [
            {
                ""name"": ""topLevel"",
                ""resource"": { ""resourceType"": ""Patient"", ""id"": ""1"" }
            },
            {
                ""name"": ""group"",
                ""part"": [
                    {
                        ""name"": ""nested"",
                        ""resource"": { ""resourceType"": ""Practitioner"", ""id"": ""2"" }
                    }
                ]
            }
        ]
    }");

    /// <summary>
    /// Minimal <see cref="IElement"/> that exposes only a caller-supplied <see cref="Location"/>,
    /// used to prove that an empty or null Location is never treated as a container-scope member.
    /// </summary>
    private sealed class StubElement : IElement
    {
        public StubElement(string location) => Location = location;

        public string Name => string.Empty;
        public object? Value => null;
        public string InstanceType => "Practitioner";
        public string Location { get; }
        public IType? Type => null;
        public bool HasPrimitiveValue => false;

        public IReadOnlyList<IElement> Children(string? name = null) => Array.Empty<IElement>();

        public T? Meta<T>() where T : class => null;
    }
}
