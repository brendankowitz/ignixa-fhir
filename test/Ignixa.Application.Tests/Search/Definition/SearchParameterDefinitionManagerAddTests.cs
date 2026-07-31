// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa. All rights reserved.
// Licensed under the MIT License (MIT).
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Search.Definition;
using Ignixa.Search.Models;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
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

        _manager.TryGetSearchParameter("Patient", "first-code", out _).ShouldBeTrue();
        _manager.TryGetSearchParameter("Patient", "second-code", out _).ShouldBeTrue();
    }

    [Fact]
    public void GivenASearchParameterAddedAtRuntime_WhenAnotherIsAddedLater_ThenTheResourceTypeParameterKeepsItsIdentity()
    {
        // _type is injected by the builder rather than read from the definition bundle. Re-running the
        // build must reuse the instance already published: callers hold references to it, and status
        // changes are applied by mutating that instance.
        _manager.TryGetSearchParameter("Patient", "_type", out SearchParameterInfo before).ShouldBeTrue();

        _manager.AddNewSearchParameters(new[] { CustomPatientParameter("third", "third-code") });

        _manager.TryGetSearchParameter("Patient", "_type", out SearchParameterInfo after).ShouldBeTrue();
        after.ShouldBeSameAs(before);
    }

    [Fact]
    public void GivenASearchParameterAddedAtRuntime_WhenTheDefinitionsAreRebuilt_ThenResourceTypeIsRegisteredExactlyOnce()
    {
        // A second _type instance sharing the canonical Url compares equal but hashes differently, so a
        // HashSet keeps both and the per-resource rebuild throws on the duplicate code. Assert the count
        // directly so a duplicate that happens not to throw is still caught.
        _manager.AddNewSearchParameters(new[] { CustomPatientParameter("fourth", "fourth-code") });

        _manager.GetSearchParameters("Patient").Count(p => p.Code == "_type").ShouldBe(1);
        _manager.AllSearchParameters.Count(p => p.Code == "_type").ShouldBe(1);
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
