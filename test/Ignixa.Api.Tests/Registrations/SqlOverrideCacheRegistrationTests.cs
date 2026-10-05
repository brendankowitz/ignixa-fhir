using Ignixa.Abstractions;
using Ignixa.Api.Registrations;
using Ignixa.Application.Features.Conformance;
using Ignixa.Application.Features.Search;
using Ignixa.Conformance.Events;
using Ignixa.Conformance.Events.Abstractions;
using Ignixa.Conformance.Events.Events;
using Ignixa.Conformance.Events.Models;
using Ignixa.DataLayer.SqlServer;
using Ignixa.DataLayer.SqlServer.Indexing;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Search.Definition;
using Ignixa.Search.Models;
using Ignixa.Specification.Extensions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;
using SearchParamType = Ignixa.Specification.ValueSets.Normative.SearchParamType;

namespace Ignixa.Api.Tests.Registrations;

public class SqlOverrideCacheRegistrationTests
{
    private const string OriginalUrl = "http://hl7.org/fhir/SearchParameter/Patient-identifier";
    private const string OverrideUrl = "http://example.org/SearchParameter/registered-identifier";

    [SqlFact]
    public async Task GivenPersistedOverride_WhenApiRegistryIsCold_ThenRegistrationSuppliesTenantDefinitionsBeforePreload()
    {
        var configured = Environment.GetEnvironmentVariable("TEST_SQL_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Run with a provisioned, owned SQL validation catalog.");
        var databaseName = $"IgnixaOverrideRegistration_{Guid.NewGuid():N}";
        var master = new SqlConnectionStringBuilder(configured) { InitialCatalog = "master" }.ConnectionString;
        var connectionString = new SqlConnectionStringBuilder(configured) { InitialCatalog = databaseName }.ConnectionString;
        await ExecuteAsync(master, $"CREATE DATABASE [{databaseName}]");
        try
        {
            await AssertRegistrationAsync(connectionString);
        }
        finally
        {
            using var pool = new SqlConnection(connectionString);
            SqlConnection.ClearPool(pool);
            await ExecuteAsync(master, $"DROP DATABASE [{databaseName}]");
        }
    }

    private static async Task AssertRegistrationAsync(string connectionString)
    {
        var tenantStore = Substitute.For<ITenantConfigurationStore>();
        tenantStore.GetTenantConfigurationAsync(1, Arg.Any<CancellationToken>())
            .Returns(new ValueTask<TenantConfiguration?>(new TenantConfiguration
            {
                TenantId = 1, DisplayName = "Override registry", FhirVersion = "4.0",
                Storage = new TenantStorageConfiguration { Type = "SqlServer", ConnectionString = connectionString }
            }));
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(Environments.Development);
        var deployer = new SchemaDeployer(
            tenantStore,
            environment,
            Options.Create(new SqlServerOptions { AutomaticSchemaDeploymentEnabled = true }),
            Substitute.For<ISchemaVersionResolver>(),
            NullLogger<SchemaDeployer>.Instance);
        await deployer.DeployIfEmptyAsync(1, CancellationToken.None);
        var sqlExecutionService = new SqlExecutionService(
            tenantStore,
            new ManagedIdentityConnectionStringValidator("Development", NullLogger<ManagedIdentityConnectionStringValidator>.Instance),
            NullLogger<SqlExecutionService>.Instance);
        short? originalId;
        using (var seed = new SqlServerSearchIndexReferenceDataCache(
            sqlExecutionService, 1, NullLogger<SqlServerSearchIndexReferenceDataCache>.Instance))
        {
            await seed.SyncSearchParametersToDatabaseAsync([OriginalUrl, OverrideUrl], null, CancellationToken.None);
            originalId = await seed.GetSearchParamIdAsync(OriginalUrl, CancellationToken.None);
            originalId.ShouldNotBeNull();
            var physicalOverrideId = await seed.GetSearchParamIdAsync(OverrideUrl, CancellationToken.None);
            physicalOverrideId.ShouldNotBeNull();
            physicalOverrideId.ShouldNotBe(originalId);
        }

        using var state = new ConformanceState();
        var eventStore = Substitute.For<ISourceEventStore>();
        eventStore.ReadAllAsync(Arg.Any<CancellationToken>()).Returns(Events());
        var baseManager = new SearchParameterDefinitionManager(
            FhirVersion.R4.GetSchemaProvider(), NullLogger<SearchParameterDefinitionManager>.Instance);
        var definitions = new CompositeSearchParameterDefinitionManager(
            baseManager, state, "4.0", NullLogger<CompositeSearchParameterDefinitionManager>.Instance,
            new SearchParameterResolutionOptions { EagerLoadPackageSearchParameters = false });
        var context = Substitute.For<IFhirVersionContext>();
        context.GetSearchParameterDefinitionManager(FhirVersion.R4, 1).Returns(definitions);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddIgnixaDataLayerServices(new ConfigurationBuilder().Build());
        services.AddSingleton(tenantStore);
        services.AddSingleton(context);
        services.AddSingleton(state);
        services.AddSingleton<ISqlExecutionService>(sqlExecutionService);
        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<SqlServerSearchIndexCacheRegistry>();

        var notReady = await Should.ThrowAsync<InvalidOperationException>(
            () => registry.GetOrCreateAsync(1, CancellationToken.None));
        notReady.Message.ShouldContain("conformance event replay");
        await state.InitializeFromEventsAsync(eventStore, CancellationToken.None);

        var cache = await registry.GetOrCreateAsync(1, CancellationToken.None);
        (await cache.GetSearchParamIdAsync(OverrideUrl, CancellationToken.None)).ShouldBe(originalId);
        registry.Invalidate(1).ShouldBeTrue();
        definitions.ClearCache();
        var replacement = await registry.GetOrCreateAsync(1, CancellationToken.None);
        (await replacement.GetSearchParamIdAsync(OverrideUrl, CancellationToken.None)).ShouldBe(originalId);
        context.Received(2).GetSearchParameterDefinitionManager(FhirVersion.R4, 1);
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
#pragma warning disable CA2100
        using var command = new SqlCommand(sql, connection);
#pragma warning restore CA2100
        await command.ExecuteNonQueryAsync();
    }

    private sealed class SqlFactAttribute : FactAttribute
    {
        public SqlFactAttribute()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TEST_SQL_CONNECTION_STRING")))
            {
                Skip = "Requires TEST_SQL_CONNECTION_STRING and isolated database creation.";
            }
        }
    }

    private static async IAsyncEnumerable<SourceEvent> Events()
    {
        await Task.CompletedTask;
        yield return new SourceEvent(1, "registry-override", nameof(SearchParameterActivated),
            new SearchParameterActivated(OverrideUrl, "identifier", "Patient", "Patient.identifier",
                SearchParamType.Token, "registration.package@1.0", new OverrideInfo(OriginalUrl, 1),
                1, null, null, null, null), DateTimeOffset.UtcNow);
    }
}
