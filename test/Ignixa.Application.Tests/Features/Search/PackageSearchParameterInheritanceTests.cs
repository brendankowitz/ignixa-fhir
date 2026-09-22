using Ignixa.Abstractions;
using Ignixa.Application.Features.Conformance;
using Ignixa.Application.Features.Search;
using Ignixa.Conformance.Events;
using Ignixa.Conformance.Events.Abstractions;
using Ignixa.Conformance.Events.Events;
using Ignixa.Search.Definition;
using Ignixa.Search.Exceptions;
using Ignixa.Search.Models;
using Ignixa.Specification.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using SearchParamType = Ignixa.Specification.ValueSets.Normative.SearchParamType;

namespace Ignixa.Application.Tests.Features.Search;

public class PackageSearchParameterInheritanceTests
{
    [Theory]
    [InlineData(FhirVersion.Stu3)]
    [InlineData(FhirVersion.R4)]
    [InlineData(FhirVersion.R4B)]
    [InlineData(FhirVersion.R5)]
    public async Task GivenPackageParameter_WhenLoadedEagerlyOrLazily_ThenInheritedParametersRemainAvailable(FhirVersion version)
    {
        var baseManager = new SearchParameterDefinitionManager(
            version.GetSchemaProvider(), NullLogger<SearchParameterDefinitionManager>.Instance);
        using var state = new ConformanceState();
        var store = Substitute.For<ISourceEventStore>();
        store.ReadAllAsync(Arg.Any<CancellationToken>()).Returns(Events());
        await state.InitializeFromEventsAsync(store, CancellationToken.None);

        foreach (bool eager in new[] { true, false })
        {
            var manager = new CompositeSearchParameterDefinitionManager(
                baseManager, state, null, NullLogger<CompositeSearchParameterDefinitionManager>.Instance,
                new SearchParameterResolutionOptions { EagerLoadPackageSearchParameters = eager });
            await manager.InitializeAsync();

            foreach (string resourceType in new[] { "Patient", "Observation", "Organization", "Bundle" })
            {
                foreach (string code in new[] { "_id", "_lastUpdated", "_tag" })
                {
                    manager.TryGetSearchParameter(resourceType, code, out var parameter)
                        .ShouldBeTrue($"{version}, eager={eager}: {resourceType}.{code}");
                    parameter.Url.ShouldBe(baseManager.GetSearchParameter(resourceType, code).Url);
                }
            }

            manager.GetSearchParameter("Patient", "package-custom").Expression.ShouldBe("Patient.active");
            manager.ClearCache();
            manager.GetSearchParameter("Patient", "_id").ShouldNotBeNull();
            manager.ReloadFromConformanceState();
            manager.GetSearchParameter("Patient", "_id").ShouldNotBeNull();
            manager.GetSearchParameter("Patient", "package-custom").ShouldNotBeNull();
        }
    }

    [Fact]
    public async Task GivenPackageOnlyResourceType_WhenLoadedEagerly_ThenItsOwnParameterRemainsAvailable()
    {
        var baseManager = new SearchParameterDefinitionManager(
            FhirVersion.R4.GetSchemaProvider(), NullLogger<SearchParameterDefinitionManager>.Instance);
        using var state = new ConformanceState();
        var store = Substitute.For<ISourceEventStore>();
        store.ReadAllAsync(Arg.Any<CancellationToken>()).Returns(Events("CustomPackageResource"));
        await state.InitializeFromEventsAsync(store, CancellationToken.None);
        var manager = new CompositeSearchParameterDefinitionManager(
            baseManager, state, null, NullLogger<CompositeSearchParameterDefinitionManager>.Instance,
            new SearchParameterResolutionOptions
            {
                EagerLoadPackageSearchParameters = true,
                FailStartupOnEagerLoadError = true,
            });

        await manager.InitializeAsync();

        manager.GetSearchParameter("CustomPackageResource", "package-custom").Expression.ShouldBe("CustomPackageResource.active");
    }

    [Theory]
    [InlineData("ViewDefinition", true)]
    [InlineData("ViewDefinition", false)]
    [InlineData("CustomPackageResource", true)]
    [InlineData("CustomPackageResource", false)]
    public async Task GivenPackageResource_WhenLoadedEagerlyOrLazily_ThenOwnAndUniversalParametersSurviveCacheReload(
        string resourceType, bool eager)
    {
        var baseManager = new SearchParameterDefinitionManager(
            FhirVersion.R4.GetSchemaProvider(), NullLogger<SearchParameterDefinitionManager>.Instance);
        using var state = new ConformanceState();
        var store = Substitute.For<ISourceEventStore>();
        store.ReadAllAsync(Arg.Any<CancellationToken>()).Returns(Events(resourceType, $"{resourceType}.id"));
        await state.InitializeFromEventsAsync(store, CancellationToken.None);
        var manager = new CompositeSearchParameterDefinitionManager(
            baseManager, state, null, NullLogger<CompositeSearchParameterDefinitionManager>.Instance,
            new SearchParameterResolutionOptions
            {
                EagerLoadPackageSearchParameters = eager,
                FailStartupOnEagerLoadError = true,
            });

        await manager.InitializeAsync();
        AssertParameters();
        manager.ClearCache();
        AssertParameters();
        manager.ReloadFromConformanceState();
        AssertParameters();

        void AssertParameters()
        {
            manager.GetSearchParameter(resourceType, "package-custom").Expression.ShouldBe($"{resourceType}.id");
            foreach (string code in new[] { "_id", "_lastUpdated", "_tag" })
            {
                manager.TryGetSearchParameter(resourceType, code, out var parameter)
                    .ShouldBeTrue($"{resourceType}.{code}, eager={eager}");
                parameter.Url.ShouldBe(baseManager.GetSearchParameter("Resource", code).Url);
            }
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GivenUnknownTypeWithoutPackageParameters_WhenRequested_ThenTypeRemainsUnsupported(bool eager)
    {
        var baseManager = new SearchParameterDefinitionManager(
            FhirVersion.R4.GetSchemaProvider(), NullLogger<SearchParameterDefinitionManager>.Instance);
        using var state = new ConformanceState();
        var store = Substitute.For<ISourceEventStore>();
        store.ReadAllAsync(Arg.Any<CancellationToken>()).Returns(Events());
        await state.InitializeFromEventsAsync(store, CancellationToken.None);
        var manager = new CompositeSearchParameterDefinitionManager(
            baseManager, state, null, NullLogger<CompositeSearchParameterDefinitionManager>.Instance,
            new SearchParameterResolutionOptions { EagerLoadPackageSearchParameters = eager });
        await manager.InitializeAsync();

        Should.Throw<SearchResourceNotSupportedException>(
            () => manager.GetSearchParameters("UnregisteredResource").ToList());
    }

    private static async IAsyncEnumerable<SourceEvent> Events(string resourceType = "Patient", string? expression = null)
    {
        await Task.CompletedTask;
        yield return new SourceEvent(
            1, "package-inheritance", nameof(SearchParameterActivated),
            new SearchParameterActivated(
                "http://example.org/SearchParameter/package-custom", "package-custom", resourceType,
                expression ?? $"{resourceType}.active", SearchParamType.Token, "inheritance.package@1.0", null, 1, null, null, null, null),
            DateTimeOffset.UtcNow);
    }
}
