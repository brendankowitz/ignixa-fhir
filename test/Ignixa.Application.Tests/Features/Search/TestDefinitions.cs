using Ignixa.Abstractions;
using Ignixa.Application.Features.Conformance;
using Ignixa.Application.Features.Search;
using Ignixa.Search.Definition;
using Ignixa.Specification.Extensions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ignixa.Application.Tests.Features.Search;

/// <summary>Real definition managers and version contexts over a conformance projection.</summary>
internal static class TestDefinitions
{
    public static FhirVersionContext CreateContext() =>
        new(NullLoggerFactory.Instance, new SearchParameterResolutionOptions { EagerLoadPackageSearchParameters = true }, NullFhirBaseUriProvider.Instance);

    /// <summary>Publishes <paramref name="state"/> as the definitions of one tenant, as a refresh does.</summary>
    public static FhirVersionContext Publish(
        this FhirVersionContext context,
        ConformanceState state,
        FhirVersion version = FhirVersion.R4,
        int tenantId = 1)
    {
        context.PublishConformanceDefinitionsSnapshot(
            version,
            tenantId,
            context.CreateConformanceDefinitionsSnapshot(version, tenantId, state.CreateSnapshot(), state.LastProcessedEventId));
        return context;
    }

    public static async Task<CompositeSearchParameterDefinitionManager> CreateManagerAsync(
        ConformanceState state,
        bool eager = true,
        FhirVersion version = FhirVersion.R4)
    {
        var manager = new CompositeSearchParameterDefinitionManager(
            BaseManager(version),
            state.CreateSnapshot(),
            null,
            NullLogger<CompositeSearchParameterDefinitionManager>.Instance,
            new SearchParameterResolutionOptions { EagerLoadPackageSearchParameters = eager, FailStartupOnEagerLoadError = true });
        await manager.InitializeAsync();
        return manager;
    }

    public static SearchParameterDefinitionManager BaseManager(FhirVersion version = FhirVersion.R4) =>
        new(version.GetSchemaProvider(), NullLogger<SearchParameterDefinitionManager>.Instance);
}
