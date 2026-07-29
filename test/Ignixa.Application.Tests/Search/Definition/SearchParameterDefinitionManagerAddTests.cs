// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa. All rights reserved.
// Licensed under the MIT License (MIT).
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Search.Definition;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ignixa.Application.Tests.Search.Definition;

public class SearchParameterDefinitionManagerAddTests
{
    private readonly R4CoreSchemaProvider _schema = new();
    private readonly SearchParameterDefinitionManager _manager;

    public SearchParameterDefinitionManagerAddTests()
    {
        _manager = new SearchParameterDefinitionManager(_schema, NullLogger<SearchParameterDefinitionManager>.Instance);
    }

    [Fact]
    public void GivenASearchParameterAddedAtRuntime_WhenAnotherIsAddedLater_ThenBothAreRegistered()
    {
        // A host that registers SearchParameters as they are created calls this once per definition.
        // The second call must not disturb the definitions the first one produced.
        _manager.AddNewSearchParameters(new[] { CustomPatientParameter("first", "first-code") });
        _manager.AddNewSearchParameters(new[] { CustomPatientParameter("second", "second-code") });

        Assert.True(_manager.TryGetSearchParameter("Patient", "first-code", out _));
        Assert.True(_manager.TryGetSearchParameter("Patient", "second-code", out _));
    }

    [Fact]
    public void GivenASearchParameterAddedAtRuntime_WhenAnotherIsAddedLater_ThenTheResourceTypeParameterKeepsItsIdentity()
    {
        // _type is injected by the builder rather than read from the definition bundle. Re-running the
        // build must reuse the instance already published: callers hold references to it, and status
        // changes are applied by mutating that instance.
        Assert.True(_manager.TryGetSearchParameter("Patient", "_type", out Ignixa.Search.Models.SearchParameterInfo before));

        _manager.AddNewSearchParameters(new[] { CustomPatientParameter("third", "third-code") });

        Assert.True(_manager.TryGetSearchParameter("Patient", "_type", out Ignixa.Search.Models.SearchParameterInfo after));
        Assert.Same(before, after);
    }

    private IElement CustomPatientParameter(string id, string code)
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
              "type": "string",
              "expression": "Patient.name.family"
            }
            """;

        return ResourceJsonNode.Parse(json).ToElement(_schema);
    }
}
