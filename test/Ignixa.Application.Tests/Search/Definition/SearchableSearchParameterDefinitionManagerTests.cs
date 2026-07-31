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

public class SearchableSearchParameterDefinitionManagerTests
{
    private const string CustomCode = "custom-code";
    private const string CustomUrl = "http://example.org/fhir/SearchParameter/custom";

    private readonly R4CoreSchemaProvider _schema = new();
    private readonly SearchParameterDefinitionManager _inner;

    public SearchableSearchParameterDefinitionManagerTests()
    {
        _inner = new SearchParameterDefinitionManager(_schema, NullLogger<SearchParameterDefinitionManager>.Instance);
        _inner.AddNewSearchParameters(new[] { CustomPatientParameter() });
    }

    [Fact]
    public void GivenAParameterThatIsSupportedButNotSearchable_WhenLookedUpByDefault_ThenItIsNotReturned()
    {
        // A parameter that has been registered but not yet reindexed is "supported" without being
        // searchable: its index rows do not exist yet, so applying it as a filter would silently
        // return too few resources. It must stay hidden unless the caller has opted in.
        SetStatus(isSearchable: false, isSupported: true);

        var searchable = new SearchableSearchParameterDefinitionManager(_inner);

        searchable.TryGetSearchParameter("Patient", CustomCode, out _).ShouldBeFalse();
        searchable.TryGetSearchParameter(new Uri(CustomUrl), out _).ShouldBeFalse();
        Should.Throw<SearchParameterNotSupportedException>(() => searchable.GetSearchParameter("Patient", CustomCode));
        searchable.GetSearchParameters("Patient").ShouldNotContain(p => p.Code == CustomCode);
        searchable.AllSearchParameters.ShouldNotContain(p => p.Code == CustomCode);

        searchable.TryGetSearchParameters("Patient", out IEnumerable<SearchParameterInfo> byResourceType).ShouldBeTrue();
        byResourceType.ShouldNotContain(p => p.Code == CustomCode);
    }

    [Fact]
    public void GivenAParameterThatIsSupportedButNotSearchable_WhenPartialIndexingIsRequested_ThenItIsReturned()
    {
        SetStatus(isSearchable: false, isSupported: true);

        var searchable = new SearchableSearchParameterDefinitionManager(_inner, () => true);

        searchable.TryGetSearchParameter("Patient", CustomCode, out _).ShouldBeTrue();
        searchable.TryGetSearchParameter(new Uri(CustomUrl), out _).ShouldBeTrue();
        searchable.GetSearchParameter("Patient", CustomCode).Code.ShouldBe(CustomCode);
        searchable.GetSearchParameter(new Uri(CustomUrl)).Code.ShouldBe(CustomCode);
        searchable.GetSearchParameters("Patient").ShouldContain(p => p.Code == CustomCode);
        searchable.AllSearchParameters.ShouldContain(p => p.Code == CustomCode);

        searchable.TryGetSearchParameters("Patient", out IEnumerable<SearchParameterInfo> byResourceType).ShouldBeTrue();
        byResourceType.ShouldContain(p => p.Code == CustomCode);
    }

    [Fact]
    public void GivenASearchableParameter_WhenLookedUpByDefault_ThenItIsReturned()
    {
        SetStatus(isSearchable: true, isSupported: true);

        var searchable = new SearchableSearchParameterDefinitionManager(_inner);

        searchable.TryGetSearchParameter("Patient", CustomCode, out _).ShouldBeTrue();
        searchable.TryGetSearchParameter(new Uri(CustomUrl), out _).ShouldBeTrue();
        searchable.GetSearchParameter("Patient", CustomCode).Code.ShouldBe(CustomCode);
        searchable.GetSearchParameters("Patient").ShouldContain(p => p.Code == CustomCode);
        searchable.AllSearchParameters.ShouldContain(p => p.Code == CustomCode);

        searchable.TryGetSearchParameters("Patient", out IEnumerable<SearchParameterInfo> byResourceType).ShouldBeTrue();
        byResourceType.ShouldContain(p => p.Code == CustomCode);
    }

    [Fact]
    public void GivenASearchableParameterThatIsNotSupported_WhenPartialIndexingIsRequested_ThenEveryAccessorAgrees()
    {
        // IsSearchable and IsSupported are independent flags, so this combination is reachable. The
        // collection accessors and the singular lookups must return the same answer about the same object.
        SetStatus(isSearchable: true, isSupported: false);

        var searchable = new SearchableSearchParameterDefinitionManager(_inner, () => true);

        searchable.TryGetSearchParameter("Patient", CustomCode, out _).ShouldBeTrue();
        searchable.TryGetSearchParameter(new Uri(CustomUrl), out _).ShouldBeTrue();
        searchable.GetSearchParameter("Patient", CustomCode).Code.ShouldBe(CustomCode);
        searchable.GetSearchParameter(new Uri(CustomUrl)).Code.ShouldBe(CustomCode);
        searchable.GetSearchParameters("Patient").ShouldContain(p => p.Code == CustomCode);
        searchable.AllSearchParameters.ShouldContain(p => p.Code == CustomCode);

        searchable.TryGetSearchParameters("Patient", out IEnumerable<SearchParameterInfo> byResourceType).ShouldBeTrue();
        byResourceType.ShouldContain(p => p.Code == CustomCode);
    }

    [Fact]
    public void GivenAParameterThatIsNeitherSearchableNorSupported_WhenPartialIndexingIsRequested_ThenEveryAccessorRefusesIt()
    {
        SetStatus(isSearchable: false, isSupported: false);

        var searchable = new SearchableSearchParameterDefinitionManager(_inner, () => true);

        searchable.TryGetSearchParameter("Patient", CustomCode, out _).ShouldBeFalse();
        searchable.TryGetSearchParameter(new Uri(CustomUrl), out _).ShouldBeFalse();
        Should.Throw<SearchParameterNotSupportedException>(() => searchable.GetSearchParameter("Patient", CustomCode));
        Should.Throw<SearchParameterNotSupportedException>(() => searchable.GetSearchParameter(new Uri(CustomUrl)));
        searchable.GetSearchParameters("Patient").ShouldNotContain(p => p.Code == CustomCode);
        searchable.AllSearchParameters.ShouldNotContain(p => p.Code == CustomCode);

        searchable.TryGetSearchParameters("Patient", out IEnumerable<SearchParameterInfo> byResourceType).ShouldBeTrue();
        byResourceType.ShouldNotContain(p => p.Code == CustomCode);
    }

    [Fact]
    public void GivenADelegateWhoseAnswerChanges_WhenTheSameSequenceIsEnumeratedTwice_ThenBothPassesAgree()
    {
        // The delegate is consulted once when the accessor is called, not once per element, so the sequence it
        // hands back is a snapshot: re-enumerating it cannot pick up an answer the caller never asked for.
        SetStatus(isSearchable: false, isSupported: true);

        var calls = 0;
        var searchable = new SearchableSearchParameterDefinitionManager(_inner, () => calls++ == 0);

        IEnumerable<SearchParameterInfo> parameters = searchable.GetSearchParameters("Patient");

        parameters.ShouldContain(p => p.Code == CustomCode);
        parameters.ShouldContain(p => p.Code == CustomCode);
        calls.ShouldBe(1);
    }

    [Fact]
    public void GivenADelegateThatThrows_WhenAnAccessorIsCalled_ThenItFaultsAtTheCallRatherThanAtEnumeration()
    {
        // AllSearchParameters is a property and GetSearchParameters returns a deferred sequence, so a
        // per-element delegate would surface the host's failure at the consumer's first MoveNext with no
        // frame naming this class. Snapshotting at entry keeps the fault on the caller's own stack.
        var searchable = new SearchableSearchParameterDefinitionManager(
            _inner,
            () => throw new InvalidOperationException("host refused"));

        Should.Throw<InvalidOperationException>(() => searchable.GetSearchParameters("Patient"));
        Should.Throw<InvalidOperationException>(() => _ = searchable.AllSearchParameters);
        Should.Throw<InvalidOperationException>(
            () => searchable.TryGetSearchParameters("Patient", out IEnumerable<SearchParameterInfo> _));
    }

    [Fact]
    public void GivenAnUnknownResourceType_WhenSearchParametersAreRequested_ThenItReportsMissing()
    {
        var searchable = new SearchableSearchParameterDefinitionManager(_inner);

        searchable.TryGetSearchParameters("NotAResourceType", out IEnumerable<SearchParameterInfo> parameters).ShouldBeFalse();
        parameters.ShouldBeNull();
    }

    [Fact]
    public void GivenAnUnknownDefinitionUri_WhenLookedUp_ThenItReportsMissingRatherThanFaulting()
    {
        var searchable = new SearchableSearchParameterDefinitionManager(_inner);

        searchable.TryGetSearchParameter(new Uri("http://example.org/fhir/SearchParameter/absent"), out SearchParameterInfo value).ShouldBeFalse();
        value.ShouldBeNull();
    }

    private void SetStatus(bool isSearchable, bool isSupported)
    {
        _inner.TryGetSearchParameter(new Uri(CustomUrl), out SearchParameterInfo parameter).ShouldBeTrue();

        parameter.IsSearchable = isSearchable;
        parameter.IsSupported = isSupported;
    }

    private IElement CustomPatientParameter()
    {
        string json = $$"""
            {
              "resourceType": "SearchParameter",
              "id": "custom",
              "url": "{{CustomUrl}}",
              "name": "custom",
              "status": "active",
              "code": "{{CustomCode}}",
              "base": [ "Patient" ],
              "type": "string",
              "expression": "Patient.name.family"
            }
            """;

        return ResourceJsonNode.Parse(json).ToElement(_schema);
    }
}
