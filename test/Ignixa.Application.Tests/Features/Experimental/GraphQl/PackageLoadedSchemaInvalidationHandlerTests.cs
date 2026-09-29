// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Application.Events.Package;
using Ignixa.Application.Features.Experimental.GraphQl.Events;
using Ignixa.Application.Features.Experimental.GraphQl.Schema;
using Ignixa.Application.Features.Search;
using Ignixa.Search.Definition;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.Features.Experimental.GraphQl;

public class PackageLoadedSchemaInvalidationHandlerTests
{
    private static readonly PackageLoadedEvent PackageLoaded =
        new("local.ignixa.sqlonfhir", "2.1.0", 0, DateTimeOffset.UtcNow);

    private readonly IFhirVersionContext _versionContext = Substitute.For<IFhirVersionContext>();
    private readonly FhirTypeModuleCatalog _catalog;
    private readonly PackageLoadedSchemaInvalidationHandler _handler;

    public PackageLoadedSchemaInvalidationHandlerTests()
    {
        _versionContext.GetBaseSchemaProvider(Arg.Any<FhirVersion>())
            .Returns(call => Substitute.For<IFhirSchemaProvider>());
        _versionContext.GetSearchParameterDefinitionManager(Arg.Any<FhirVersion>())
            .Returns(call => Substitute.For<ISearchParameterDefinitionManager>());

        _catalog = new FhirTypeModuleCatalog(
            _versionContext,
            Substitute.For<IFhirBaseUriProvider>(),
            NullLoggerFactory.Instance);
        _handler = new PackageLoadedSchemaInvalidationHandler(
            _catalog,
            NullLogger<PackageLoadedSchemaInvalidationHandler>.Instance);
    }

    [Fact]
    public async Task GivenNoSchemaBuilt_WhenPackageLoaded_ThenNoFhirVersionIsLoaded()
    {
        await _handler.HandleAsync(PackageLoaded, CancellationToken.None);

        _versionContext.DidNotReceive().GetBaseSchemaProvider(Arg.Any<FhirVersion>());
        _versionContext.DidNotReceive().GetSearchParameterDefinitionManager(Arg.Any<FhirVersion>());
        _catalog.Created.ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenOnlyTheR4SchemaBuilt_WhenPackageLoaded_ThenOnlyR4IsNotifiedAndNoOtherVersionIsLoaded()
    {
        var r4Module = _catalog.GetOrCreate(FhirVersion.R4);
        int r4Notifications = 0;
        r4Module.TypesChanged += (_, _) => r4Notifications++;

        await _handler.HandleAsync(PackageLoaded, CancellationToken.None);

        r4Notifications.ShouldBe(1);
        _catalog.Created.ShouldBe([r4Module]);
        _versionContext.Received(1).GetSearchParameterDefinitionManager(FhirVersion.R4);
        _versionContext.DidNotReceive().GetSearchParameterDefinitionManager(
            Arg.Is<FhirVersion>(version => version != FhirVersion.R4));
        _versionContext.DidNotReceive().GetBaseSchemaProvider(
            Arg.Is<FhirVersion>(version => version != FhirVersion.R4));
    }

    [Fact]
    public void GivenAVersionRequestedTwice_WhenCreated_ThenTheSameModuleIsReturned()
    {
        var first = _catalog.GetOrCreate(FhirVersion.R5);
        var second = _catalog.GetOrCreate(FhirVersion.R5);

        second.ShouldBeSameAs(first);
        _versionContext.Received(1).GetSearchParameterDefinitionManager(FhirVersion.R5);
    }

    [Fact]
    public void GivenModuleCreationFails_WhenRequestedAgain_ThenCreationIsRetried()
    {
        _versionContext.GetSearchParameterDefinitionManager(FhirVersion.R4B)
            .Returns(
                _ => throw new InvalidOperationException("transient"),
                _ => Substitute.For<ISearchParameterDefinitionManager>());

        Should.Throw<InvalidOperationException>(() => _catalog.GetOrCreate(FhirVersion.R4B));
        _catalog.Created.ShouldBeEmpty();

        _catalog.GetOrCreate(FhirVersion.R4B).ShouldNotBeNull();
        _catalog.Created.Count().ShouldBe(1);
    }
}
