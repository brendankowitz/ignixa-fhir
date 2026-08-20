// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa. All rights reserved.
// Licensed under the MIT License (MIT).
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Search.Definition;
using Ignixa.Search.Exceptions;
using Ignixa.Search.Models;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Ignixa.Application.Tests.Search.Definition;

/// <summary>Regression coverage for deleting a derived (<c>{url}#identifier</c>) search parameter directly.
/// <see cref="SearchParameterDefinitionManager.DeleteSearchParameter"/> used to remove it from the
/// lookups and then immediately re-register it via <see cref="ReferenceIdentifierSearchParameterRegistrar.Register"/>
/// because its source reference parameter was still present - a success-shaped no-op that never actually
/// deleted anything.</summary>
public class SearchParameterDefinitionManagerDeleteTests
{
    private readonly R4CoreSchemaProvider _schema = new();
    private readonly SearchParameterDefinitionManager _manager;

    public SearchParameterDefinitionManagerDeleteTests()
    {
        _manager = new SearchParameterDefinitionManager(_schema, NullLogger<SearchParameterDefinitionManager>.Instance);
        _manager.AddNewSearchParameters(new[] { CustomReferenceParameter("custom-ref", "custom-ref") });
    }

    [Fact]
    public void GivenADerivedIdentifierSearchParameter_WhenDeletedDirectly_ThenBadSearchRequestExceptionIsThrownAndItRemainsRegistered()
    {
        _manager.TryGetSearchParameter("Patient", "custom-ref:identifier", out SearchParameterInfo before).ShouldBeTrue();

        Should.Throw<BadSearchRequestException>(() => _manager.DeleteSearchParameter(before.Url.ToString()));

        _manager.TryGetSearchParameter("Patient", "custom-ref:identifier", out SearchParameterInfo after).ShouldBeTrue();
        after.ShouldBeSameAs(before);
        _manager.TryGetSearchParameter("Patient", "custom-ref", out _).ShouldBeTrue();
    }

    [Fact]
    public void GivenASourceReferenceSearchParameter_WhenDeleted_ThenItsDerivedIdentifierParameterIsRemovedToo()
    {
        _manager.DeleteSearchParameter("http://example.org/fhir/SearchParameter/custom-ref");

        _manager.TryGetSearchParameter("Patient", "custom-ref", out _).ShouldBeFalse();
        _manager.TryGetSearchParameter("Patient", "custom-ref:identifier", out _).ShouldBeFalse();
    }

    private IElement CustomReferenceParameter(string id, string code)
    {
        string json = $$"""
            {
              "resourceType": "SearchParameter",
              "id": "{{id}}",
              "url": "http://example.org/fhir/SearchParameter/{{id}}",
              "name": "{{id}}",
              "status": "active",
              "code": "{{code}}",
              "base": [ "Patient" ],
              "type": "reference",
              "expression": "Patient.generalPractitioner"
            }
            """;

        return ResourceJsonNode.Parse(json).ToElement(_schema);
    }
}
