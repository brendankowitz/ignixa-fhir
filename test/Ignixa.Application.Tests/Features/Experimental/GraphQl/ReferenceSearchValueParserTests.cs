// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Search.Indexing.SearchValues;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.Features.Experimental.GraphQl;

public class ReferenceSearchValueParserTests
{
    private readonly IReferenceSearchValueParser _parser;

    public ReferenceSearchValueParserTests()
    {
        var schemaProvider = Substitute.For<IFhirSchemaProvider>();
        schemaProvider.ResourceTypeNames.Returns(new HashSet<string> { "Patient", "Observation" });
        _parser = new ReferenceSearchValueParser(schemaProvider);
    }

    [Theory]
    [InlineData("Patient/123", "Patient", true)]
    [InlineData("Patient/123", "Observation", false)]
    [InlineData("Observation/456", "Observation", true)]
    public void GivenTypeFilter_WhenCheckingReference_ThenFiltersCorrectly(
        string reference, string typeFilter, bool shouldMatch)
    {
        var parsed = _parser.Parse(reference);
        parsed.ResourceType.ShouldNotBeNull();

        var matches = string.Equals(parsed.ResourceType, typeFilter, StringComparison.Ordinal);
        matches.ShouldBe(shouldMatch);
    }

    [Theory]
    [InlineData("Patient/123", "Patient", "123")]
    [InlineData("Observation/obs-1", "Observation", "obs-1")]
    public void GivenRelativeReference_WhenParsing_ThenReturnsResourceTypeAndId(
        string reference, string expectedType, string expectedId)
    {
        var parsed = _parser.Parse(reference);

        parsed.ResourceType.ShouldBe(expectedType);
        parsed.ResourceId.ShouldBe(expectedId);
    }

    [Theory]
    [InlineData("https://example.com/fhir/Patient/456", "Patient", "456")]
    [InlineData("http://server.org/base/Observation/obs-2", "Observation", "obs-2")]
    public void GivenAbsoluteReference_WhenParsing_ThenReturnsResourceTypeAndId(
        string reference, string expectedType, string expectedId)
    {
        var parsed = _parser.Parse(reference);

        parsed.ResourceType.ShouldBe(expectedType);
        parsed.ResourceId.ShouldBe(expectedId);
    }

    [Theory]
    [InlineData("Patient/123/_history/2", "Patient", "123")]
    public void GivenVersionedReference_WhenParsing_ThenReturnsResourceKeyWithoutVersion(
        string reference, string expectedType, string expectedId)
    {
        var parsed = _parser.Parse(reference);

        parsed.ResourceType.ShouldBe(expectedType);
        parsed.ResourceId.ShouldBe(expectedId);
    }

    [Theory]
    [InlineData("urn:uuid:some-guid")]
    [InlineData("just-a-string")]
    public void GivenNonResolvableReference_WhenParsing_ThenResourceTypeIsNull(string reference)
    {
        var parsed = _parser.Parse(reference);

        parsed.ResourceType.ShouldBeNull();
    }
}
