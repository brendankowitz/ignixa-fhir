// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa. All rights reserved.
// Licensed under the MIT License (MIT).
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Search.Definition;
using Ignixa.Search.Indexing;
using Ignixa.Search.Models;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Ignixa.Application.Tests.Search.Definition;

/// <summary>Regression coverage for the <see cref="SearchParameterInfo.GetHashCode"/> / Equals consistency fix.
/// Every rebuild injects a fresh <c>_type</c> definition carrying an expression, while the generated bundle
/// already published one under the same canonical URL with a null expression. Equals calls them the same
/// parameter on the URL alone; the old hash combined the expression too, so the two landed in different buckets,
/// both survived the builder's HashSet union, and the following <c>ToDictionary</c> on Code threw on the
/// duplicate key. Hashing on Url alone collapses them onto the entry already in the dictionary.</summary>
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
        // A host that registers SearchParameters as they are created calls this once per definition, and every
        // call rebuilds. Each rebuild re-injects _type, so the duplicate-key throw fires on the first call
        // already; asserting on two proves the second rebuild does not disturb what the first produced either.
        _manager.AddNewSearchParameters(new[] { CustomPatientParameter("first", "first-code") });
        _manager.AddNewSearchParameters(new[] { CustomPatientParameter("second", "second-code") });

        _manager.TryGetSearchParameter("Patient", "first-code", out _).ShouldBeTrue();
        _manager.TryGetSearchParameter("Patient", "second-code", out _).ShouldBeTrue();
    }

    [Fact]
    public void GivenASearchParameterAddedAtRuntime_WhenAnotherIsAddedLater_ThenTheResourceTypeParameterKeepsItsIdentity()
    {
        // Deduplicating on Url keeps the incumbent and drops the newly injected _type, so a caller that took a
        // reference before the rebuild still holds the live instance. That matters because status flags
        // (IsSearchable, IsSupported) are applied by mutating the instance, not by replacing the entry.
        _manager.TryGetSearchParameter("Patient", "_type", out SearchParameterInfo before).ShouldBeTrue();

        _manager.AddNewSearchParameters(new[] { CustomPatientParameter("third", "third-code") });

        _manager.TryGetSearchParameter("Patient", "_type", out SearchParameterInfo after).ShouldBeTrue();
        after.ShouldBeSameAs(before);
    }

    [Fact]
    public void GivenASearchParameterAddedAtRuntime_WhenTheDefinitionsAreRebuilt_ThenResourceTypeResolvesToOneInstanceEverywhere()
    {
        // Counting _type per accessor proves nothing: both are dictionary values keyed on the field being
        // counted (per-type by Code, AllSearchParameters by Url), so a second entry is unrepresentable there.
        // Reference identity across the URL lookup, the base type and two concrete types is the observable
        // evidence that the two colliding definitions collapsed onto one object rather than being split
        // across the two lookups, which would let a status mutation applied through one path go unseen by
        // the other.
        _manager.AddNewSearchParameters(new[] { CustomPatientParameter("fourth", "fourth-code") });

        _manager.TryGetSearchParameter(SearchParameterNames.ResourceTypeUri, out SearchParameterInfo byUrl).ShouldBeTrue();

        _manager.GetSearchParameter(KnownResourceTypes.Resource, "_type").ShouldBeSameAs(byUrl);
        _manager.GetSearchParameter("Patient", "_type").ShouldBeSameAs(byUrl);
        _manager.GetSearchParameter("Observation", "_type").ShouldBeSameAs(byUrl);
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
