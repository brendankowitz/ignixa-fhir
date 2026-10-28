// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Api.Events;
using Ignixa.Application.Events.Package;
using Ignixa.Application.Features.Conformance;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Features.SemanticSearch;
using Ignixa.Application.Infrastructure.Caching;
using Ignixa.Application.Tests.SemanticSearch;
using Ignixa.DataLayer.SqlServer.Indexing;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace Ignixa.Api.E2ETests._Infrastructure;

/// <summary>
/// A dedicated <see cref="IgnixaApiFixture"/> for semantic (vector) search E2E coverage: VectorSearch
/// enabled, backed by the test-only <see cref="DeterministicEmbeddingGenerator"/>, with its own owned
/// SQL database. A separate fixture -- rather than enabling VectorSearch on the shared
/// <see cref="Collections.E2ETestCollection"/> fixture every other E2E test depends on -- keeps this
/// feature's activation of a custom <c>special</c> SearchParameter from changing behavior other search
/// tests rely on (see <see cref="ConformanceApiFixture"/> for the same isolation pattern).
/// </summary>
/// <remarks>
/// <para>
/// <c>VectorSearch:Enabled</c> and the embedding deployment settings are set with
/// <see cref="IWebHostBuilder.UseSetting"/>, not <c>ConfigureAppConfiguration</c>: <c>AddIgnixaApi</c>
/// reads configuration eagerly from <c>Program.cs</c>'s top-level statements, before this fixture's
/// <c>ConfigureAppConfiguration</c> additions are visible (see the sibling remark on the GraphQL feature
/// switch in <see cref="IgnixaApiFixture.ConfigureWebHost"/>). <c>UseSetting</c> values land in host
/// configuration early enough to be seen.
/// </para>
/// <para>
/// The real Azure OpenAI-backed generator is swapped for <see cref="DeterministicEmbeddingGenerator"/>
/// by re-registering <see cref="SemanticSearchServiceRegistration.ProviderGeneratorKey"/> in
/// <see cref="IWebHostBuilder.ConfigureServices"/>: that callback runs after the production
/// <c>ConfigureServices</c> that calls <c>AddSemanticSearch</c>, so this registration is the one that
/// wins (see that key's own remarks for why it exists).
/// </para>
/// </remarks>
public sealed class SemanticSearchApiFixture : IgnixaApiFixture
{
    /// <summary>Code of the <c>special</c> SearchParameter activated by this fixture's tests.</summary>
    public const string SemanticParameterCode = "semantic-text";

    /// <summary>Canonical URL of the SearchParameter activated by this fixture's tests.</summary>
    public const string SemanticParameterUrl = "http://example.org/fhir/SearchParameter/semantic-text";

    private const string VectorSearchConfigExtensionUrl = "http://microsoft.com/fhir/StructureDefinition/vector-search-config";
    private const string PackageId = "test.semantic.search";
    private const string PackageVersion = "1.0.0";

    private readonly Lazy<Task> _activation;

    public SemanticSearchApiFixture() : base("IgnixaSemanticSearch")
    {
        _activation = new Lazy<Task>(() => ActivateSemanticSearchParameterOnAsync(Services));
    }

    /// <summary>
    /// Activates the <c>semantic-text</c> SearchParameter on Observation, exactly once no matter how
    /// many tests in the collection await this. Every test that searches on <c>semantic-text</c> or
    /// expects it in the CapabilityStatement must await this first.
    /// </summary>
    public Task EnsureSemanticSearchParameterActivatedAsync() => _activation.Value;

    /// <summary>
    /// Activates the same <c>semantic-text</c> SearchParameter as <see cref="EnsureSemanticSearchParameterActivatedAsync"/>,
    /// against any host's services -- shared with <see cref="SemanticSearchDisabledTests"/>, which needs
    /// the parameter activated on a plain <see cref="IgnixaApiFixture"/> with VectorSearch left disabled,
    /// so it cannot use this fixture or its instance-scoped activation cache.
    /// </summary>
    public static async Task ActivateSemanticSearchParameterOnAsync(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var searchParameter = new JsonObject
        {
            ["resourceType"] = "SearchParameter",
            ["id"] = SemanticParameterCode,
            ["url"] = SemanticParameterUrl,
            ["version"] = PackageVersion,
            ["name"] = "SemanticText",
            ["status"] = "active",
            ["code"] = SemanticParameterCode,
            ["base"] = new JsonArray("Observation"),
            ["type"] = "special",
            ["expression"] = "Observation.value.ofType(string) | Observation.note.text",
            ["extension"] = new JsonArray
            {
                new JsonObject
                {
                    ["url"] = VectorSearchConfigExtensionUrl,
                    ["extension"] = new JsonArray
                    {
                        new JsonObject { ["url"] = "extractionPolicy", ["valueCode"] = "concatenate" },
                        new JsonObject { ["url"] = "minimumScore", ["valueDecimal"] = 0 },
                    },
                },
            },
        };

        await services.GetRequiredService<IPackageResourceRepository>().UpsertAsync(new PackageResource
        {
            PackageId = PackageId,
            PackageVersion = PackageVersion,
            ResourceType = "SearchParameter",
            ResourceId = SemanticParameterCode,
            Canonical = SemanticParameterUrl,
            Version = PackageVersion,
            FhirVersion = "4.0.1",
            ResourceJson = searchParameter.ToJsonString(),
        }, CancellationToken.None);

        var pipeline = services.GetRequiredService<PackageActivationPipeline>();
        var result = await pipeline.ActivateAsync(PackageId, PackageVersion, CancellationToken.None);
        result.Success.ShouldBeTrue(
            $"Activating the semantic-text SearchParameter failed: {string.Join("; ", result.Issues.Select(issue => issue.Message))}");

        // Activation alone records the parameter in ConformanceState; the row generators that index
        // writes read dbo.SearchParam directly (see PackageLoadedSearchParameterSyncHandler's remarks),
        // so without this sync every Observation written after activation would have its semantic text
        // silently dropped from indexing. SqlServerTenantInitializer only runs this sync at tenant
        // startup, before this test-only package is ever activated.
        var syncHandler = new PackageLoadedSearchParameterSyncHandler(
            services.GetRequiredService<IFhirVersionContext>(),
            services.GetRequiredService<SqlServerSearchIndexCacheRegistry>(),
            services.GetRequiredService<ITenantConfigurationStore>(),
            services.GetRequiredService<ICapabilityCacheInvalidator>(),
            services.GetRequiredService<ILogger<PackageLoadedSearchParameterSyncHandler>>());
        await syncHandler.HandleAsync(
            new PackageLoadedEvent(PackageId, PackageVersion, TenantId: 1, DateTimeOffset.UtcNow),
            CancellationToken.None);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.UseSetting("VectorSearch:Enabled", "true");
        builder.UseSetting("VectorSearch:Embedding:Endpoint", "https://semantic-search-e2e.openai.azure.com/");
        builder.UseSetting("VectorSearch:Embedding:DeploymentName", "semantic-search-e2e-deployment");

        builder.ConfigureServices(services => services.AddKeyedSingleton<IEmbeddingGenerator<string, Embedding<float>>>(
            SemanticSearchServiceRegistration.ProviderGeneratorKey,
            (_, _) => new DeterministicEmbeddingGenerator()));
    }
}
