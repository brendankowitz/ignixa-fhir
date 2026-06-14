// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using Ignixa.Abstractions;
using Ignixa.Application.Features.Experimental.GraphQl.Models;
using Ignixa.Application.Features.Experimental.GraphQl.Resolvers;
using Ignixa.Search.Indexing.SearchValues;
using NSubstitute;
using Shouldly;
using ResourceKey = Ignixa.Application.Features.Experimental.GraphQl.Models.ResourceKey;

namespace Ignixa.Application.Tests.Features.Experimental.GraphQl;

public class ReferenceResolverTests
{
    private readonly IReferenceSearchValueParser _parser;

    public ReferenceResolverTests()
    {
        var schemaProvider = Substitute.For<IFhirSchemaProvider>();
        schemaProvider.ResourceTypeNames.Returns(new HashSet<string> { "Patient", "Observation" });
        _parser = new ReferenceSearchValueParser(schemaProvider);
    }

    private static JsonElement Resource(string resourceType, string id)
        => JsonSerializer.Deserialize<JsonElement>($$"""{"resourceType":"{{resourceType}}","id":"{{id}}"}""");

    private Task<ReferenceResolver.ReferenceResolution> ResolveAsync(
        string? reference,
        bool isOptional = false,
        string? typeFilter = null,
        Func<ResourceKey, Task<JsonElement?>>? loadAsync = null)
        => ReferenceResolver.ResolveAsync(
            reference,
            isOptional,
            typeFilter,
            _parser,
            loadAsync ?? (_ => Task.FromResult<JsonElement?>(null)),
            CancellationToken.None);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("#contained-1")]
    [InlineData("urn:uuid:9b8d8e3c-1111-2222-3333-444455556666")]
    [InlineData("URN:OID:1.2.3")]
    [InlineData("just-a-bare-string")]
    public async Task GivenUnresolvableShape_WhenResolving_ThenOutcomeIsNotSupported(string? reference)
    {
        var resolution = await ResolveAsync(reference);

        resolution.Outcome.ShouldBe(ReferenceResolver.Outcome.NotSupported);
        resolution.Resource.ShouldBeNull();
    }

    [Fact]
    public async Task GivenResolvedNull_WhenResolving_ThenOutcomeIsNotFound()
    {
        var resolution = await ResolveAsync(
            "Patient/p1",
            loadAsync: _ => Task.FromResult<JsonElement?>(null));

        resolution.Outcome.ShouldBe(ReferenceResolver.Outcome.NotFound);
        resolution.Resource.ShouldBeNull();
    }

    [Fact]
    public async Task GivenResolvableReference_WhenResolving_ThenOutcomeIsResolved()
    {
        var loaded = Resource("Patient", "p1");
        var resolution = await ResolveAsync(
            "Patient/p1",
            loadAsync: _ => Task.FromResult<JsonElement?>(loaded));

        resolution.Outcome.ShouldBe(ReferenceResolver.Outcome.Resolved);
        resolution.Resource.ShouldNotBeNull();
        resolution.Resource!.Value.GetProperty("id").GetString().ShouldBe("p1");
    }

    [Fact]
    public async Task GivenTypeFilterMismatch_WhenResolving_ThenReturnsTypeMismatchWithoutLoading()
    {
        var loadCalled = false;

        var resolution = await ResolveAsync(
            "Patient/p1",
            typeFilter: "Observation",
            loadAsync: _ =>
            {
                loadCalled = true;
                return Task.FromResult<JsonElement?>(Resource("Patient", "p1"));
            });

        resolution.Outcome.ShouldBe(ReferenceResolver.Outcome.TypeMismatch);
        resolution.Resource.ShouldBeNull();
        loadCalled.ShouldBeFalse();
    }

    [Fact]
    public async Task GivenTypeFilterMatch_WhenResolving_ThenResolves()
    {
        var loaded = Resource("Patient", "p1");
        var resolution = await ResolveAsync(
            "Patient/p1",
            typeFilter: "Patient",
            loadAsync: _ => Task.FromResult<JsonElement?>(loaded));

        resolution.Outcome.ShouldBe(ReferenceResolver.Outcome.Resolved);
        resolution.Resource!.Value.GetProperty("id").GetString().ShouldBe("p1");
    }

    [Fact]
    public async Task GivenLoadAsyncReceivesParsedKey_WhenResolving_ThenKeyMatchesReference()
    {
        ResourceKey? observed = null;

        await ResolveAsync(
            "https://example.com/fhir/Observation/obs-7",
            loadAsync: key =>
            {
                observed = key;
                return Task.FromResult<JsonElement?>(Resource("Observation", "obs-7"));
            });

        observed.ShouldNotBeNull();
        observed!.ResourceType.ShouldBe("Observation");
        observed.ResourceId.ShouldBe("obs-7");
    }

    [Fact]
    public void GivenDefaultReference_WhenNotSupportedOrNotFound_ThenReportsError()
    {
        ReferenceResolver.ShouldReportError(ReferenceResolver.Outcome.NotSupported, isOptional: false).ShouldBeTrue();
        ReferenceResolver.ShouldReportError(ReferenceResolver.Outcome.NotFound, isOptional: false).ShouldBeTrue();
    }

    [Fact]
    public void GivenOptionalReference_WhenNotSupportedOrNotFound_ThenSuppressesError()
    {
        ReferenceResolver.ShouldReportError(ReferenceResolver.Outcome.NotSupported, isOptional: true).ShouldBeFalse();
        ReferenceResolver.ShouldReportError(ReferenceResolver.Outcome.NotFound, isOptional: true).ShouldBeFalse();
    }

    [Fact]
    public void GivenTypeMismatchOrResolved_WhenDecidingReporting_ThenNeverReports()
    {
        ReferenceResolver.ShouldReportError(ReferenceResolver.Outcome.TypeMismatch, isOptional: false).ShouldBeFalse();
        ReferenceResolver.ShouldReportError(ReferenceResolver.Outcome.Resolved, isOptional: false).ShouldBeFalse();
    }

    [Theory]
    [InlineData(null, "the reference is empty")]
    [InlineData("", "the reference is empty")]
    [InlineData("#x", "contained reference resolution is not supported")]
    [InlineData("urn:uuid:abc", "urn reference resolution is not supported")]
    [InlineData("garbage", "a resource type and id could not be parsed from the reference")]
    public void GivenUnsupportedReference_WhenDescribing_ThenReturnsExpectedReason(string? reference, string expected)
    {
        ReferenceResolver.DescribeUnsupported(reference).ShouldBe(expected);
    }
}
