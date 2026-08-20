// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Search.Definition;
using Ignixa.Search.Models;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;
using Ignixa.Specification.ValueSets.Normative;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Ignixa.Application.Tests.Search.Definition;

public class ReferenceIdentifierSearchParameterRegistrationTests
{
    private readonly R4CoreSchemaProvider _schemaProvider = new();

    [Fact]
    public void GivenBuiltInReferenceParameter_WhenManagerIsConstructed_ThenDerivedParameterResolvesByCodeAndUrl()
    {
        var manager = CreateManager();
        SearchParameterInfo original = manager.GetSearchParameter("Encounter", "subject");

        bool resolved = ReferenceIdentifierSearchParameterFactory.TryResolve(
            manager,
            original,
            out SearchParameterInfo derived);

        resolved.ShouldBeTrue();
        manager.GetSearchParameter("Encounter", "subject:identifier").ShouldBeSameAs(derived);
        manager.GetSearchParameter(ReferenceIdentifierSearchParameterFactory.DeriveUrl(original)).ShouldBeSameAs(derived);
    }

    [Fact]
    public void GivenNonReferenceParameter_WhenManagerIsConstructed_ThenNoDerivedParameterIsRegistered()
    {
        var manager = CreateManager();
        SearchParameterInfo original = manager.GetSearchParameter("Patient", "identifier");

        bool resolved = ReferenceIdentifierSearchParameterFactory.TryResolve(
            manager,
            original,
            out _);

        original.Type.ShouldBe(SearchParamType.Token);
        resolved.ShouldBeFalse();
        manager.TryGetSearchParameter("Patient", "identifier:identifier", out _).ShouldBeFalse();
    }

    [Fact]
    public void GivenReferenceParameterAddedAtRuntime_WhenRegistrationCompletes_ThenDerivedParameterResolves()
    {
        var manager = CreateManager();

        manager.AddNewSearchParameters([CustomReferenceParameter()]);

        SearchParameterInfo original = manager.GetSearchParameter("Patient", "managing-organization");
        ReferenceIdentifierSearchParameterFactory.TryResolve(manager, original, out SearchParameterInfo derived).ShouldBeTrue();
        manager.GetSearchParameter("Patient", "managing-organization:identifier").ShouldBeSameAs(derived);
    }

    [Fact]
    public void GivenExistingDerivedParameter_WhenAnotherParameterIsAdded_ThenExistingInstanceIsPreserved()
    {
        var manager = CreateManager();
        SearchParameterInfo existing = manager.GetSearchParameter("Encounter", "subject:identifier");

        manager.AddNewSearchParameters([CustomReferenceParameter()]);

        manager.GetSearchParameter("Encounter", "subject:identifier").ShouldBeSameAs(existing);
    }

    [Fact]
    public void GivenUnchangedRegistry_WhenSearchParametersAreReadRepeatedly_ThenPublishedSnapshotIsReused()
    {
        var manager = CreateManager();

        IEnumerable<SearchParameterInfo> first = manager.GetSearchParameters("Patient");
        IEnumerable<SearchParameterInfo> second = manager.GetSearchParameters("Patient");

        second.ShouldBeSameAs(first);
    }

    [Fact]
    public void GivenPublishedRegistry_WhenReferenceParameterIsAdded_ThenCompleteReplacementSnapshotIsPublished()
    {
        var manager = CreateManager();
        IEnumerable<SearchParameterInfo> before = manager.GetSearchParameters("Patient");

        manager.AddNewSearchParameters([CustomReferenceParameter()]);

        IEnumerable<SearchParameterInfo> after = manager.GetSearchParameters("Patient");
        after.ShouldNotBeSameAs(before);
        after.ShouldContain(parameter => parameter.Code == "managing-organization");
        after.ShouldContain(parameter => parameter.Code == "managing-organization:identifier");
    }

    private SearchParameterDefinitionManager CreateManager()
    {
        return new SearchParameterDefinitionManager(
            _schemaProvider,
            NullLogger<SearchParameterDefinitionManager>.Instance);
    }

    private IElement CustomReferenceParameter()
    {
        const string json = """
            {
              "resourceType": "SearchParameter",
              "id": "patient-managing-organization",
              "url": "http://example.org/fhir/SearchParameter/patient-managing-organization",
              "name": "managing-organization",
              "status": "active",
              "code": "managing-organization",
              "base": [ "Patient" ],
              "type": "reference",
              "expression": "Patient.managingOrganization",
              "target": [ "Organization" ]
            }
            """;

        return ResourceJsonNode.Parse(json).ToElement(_schemaProvider);
    }
}
