// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Application.Features.Experimental.GraphQl.Models;
using Ignixa.Application.Features.Experimental.GraphQl.Resolvers;
using Shouldly;

namespace Ignixa.Application.Tests.Features.Experimental.GraphQl;

public class FieldResolverTests
{
    [Theory]
    [InlineData("Patient/123", "Patient", "123")]
    [InlineData("Observation/obs-1", "Observation", "obs-1")]
    public void GivenRelativeReference_WhenParsing_ThenReturnsResourceKey(
        string reference, string expectedType, string expectedId)
    {
        var result = FieldResolver.ParseFhirReference(reference);

        result.ShouldNotBeNull();
        result!.ResourceType.ShouldBe(expectedType);
        result.ResourceId.ShouldBe(expectedId);
    }

    [Theory]
    [InlineData("https://example.com/fhir/Patient/456", "Patient", "456")]
    [InlineData("http://server.org/base/Observation/obs-2", "Observation", "obs-2")]
    public void GivenAbsoluteReference_WhenParsing_ThenReturnsResourceKey(
        string reference, string expectedType, string expectedId)
    {
        var result = FieldResolver.ParseFhirReference(reference);

        result.ShouldNotBeNull();
        result!.ResourceType.ShouldBe(expectedType);
        result.ResourceId.ShouldBe(expectedId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("#contained-ref")]
    [InlineData("urn:uuid:some-guid")]
    [InlineData("just-a-string")]
    public void GivenUnresolvableReference_WhenParsing_ThenReturnsNull(string? reference)
    {
        var result = FieldResolver.ParseFhirReference(reference);

        result.ShouldBeNull();
    }
}
