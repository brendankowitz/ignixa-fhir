// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Application.Features.Search;
using Ignixa.Search.Definition;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Specification.Generated;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Ignixa.Application.Tests.Infrastructure;

/// <summary>
/// The base URI provider used to be optional at four seams. A forgotten wiring at any of them produced
/// references stored in a different form from the rest of the system, with no error and no log line — the
/// GraphQL type module had exactly that defect. Each seam now demands the dependency, so the failure is a
/// compile error, and <see cref="NullFhirBaseUriProvider"/> is the one way to say "no base" out loud.
/// </summary>
public class FhirBaseUriProviderWiringTests
{
    [Fact]
    public void GivenNoProvider_WhenConstructingTheReferenceParser_ThenItThrows()
    {
        Should.Throw<ArgumentNullException>(
            () => new ReferenceSearchValueParser(new R4CoreSchemaProvider(), baseUriProvider: null!));
    }

    [Fact]
    public void GivenNoProvider_WhenCreatingASearchIndexer_ThenItThrows()
    {
        Should.Throw<ArgumentNullException>(() => SearchIndexerFactory.CreateInstance(
            new R4CoreSchemaProvider(),
            NullLoggerFactory.Instance,
            searchParameterDefinitionManager: null,
            baseUriProvider: null!));
    }

    [Fact]
    public void GivenNoProvider_WhenConstructingTheSearchOptionsBuilderFactory_ThenItThrows()
    {
        Should.Throw<ArgumentNullException>(() => new SearchOptionsBuilderFactory(
            Substitute.For<IFhirVersionContext>(),
            baseUriProvider: null!));
    }

    [Fact]
    public void GivenNoProvider_WhenConstructingTheVersionContext_ThenItThrows()
    {
        Should.Throw<ArgumentNullException>(() => new FhirVersionContext(
            NullLoggerFactory.Instance,
            new SearchParameterResolutionOptions(),
            baseUriProvider: null!));
    }

    [Fact]
    public void GivenTheExplicitOptOut_WhenRecognizingAnyBase_ThenNothingIsThisServer()
    {
        IFhirBaseUriProvider provider = NullFhirBaseUriProvider.Instance;

        provider.GetBaseUri().ShouldBeNull();
        provider.GetServiceBaseUris().ShouldBeEmpty();
        provider.IsServiceBaseUri(new Uri("https://fhir.example.org/")).ShouldBeFalse();
    }
}
