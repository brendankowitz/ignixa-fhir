using Ignixa.Abstractions;
using Ignixa.DataLayer.SqlServer.Indexing;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Search.Definition;
using Ignixa.Search.Expressions;
using Ignixa.Search.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IO;
using NSubstitute;
using Shouldly;
using SearchParamType = Ignixa.Specification.ValueSets.Normative.SearchParamType;

namespace Ignixa.DataLayer.SqlServer.Tests;

public sealed class SqlServerTenantServiceFactorySearchDefinitionsTests : IDisposable
{
    private const int TenantId = 1;
    private readonly EmptySqlExecutionService _sql = new();
    private readonly SqlServerSearchIndexCacheRegistry _cacheRegistry;

    public SqlServerTenantServiceFactorySearchDefinitionsTests()
        => _cacheRegistry = new SqlServerSearchIndexCacheRegistry(_sql, NullLoggerFactory.Instance);

    public void Dispose() => _cacheRegistry.Dispose();

    [Theory]
    [InlineData("Pending")]
    [InlineData("Staged")]
    [InlineData("Disabling")]
    public async Task GivenANonSearchableCompartmentMembershipParameter_WhenCompilingACompartmentSearch_ThenTheSqlDoesNotUseIt(
        string lifecycleState)
    {
        var (isSupported, isHiddenByTransition) = lifecycleState switch
        {
            "Pending" => (true, false),
            "Staged" => (false, true),
            "Disabling" => (true, true),
            _ => throw new ArgumentOutOfRangeException(nameof(lifecycleState)),
        };
        var subject = new SearchParameterInfo(
            "subject",
            "subject",
            SearchParamType.Reference,
            new Uri("http://hl7.org/fhir/SearchParameter/Observation-subject"),
            baseResourceTypes: ["Observation"])
        {
            IsSearchable = false,
            IsSupported = isSupported,
            IsHiddenByTransition = isHiddenByTransition,
        };
        var sql = await CompileCompartmentSearchAsync(subject);

        sql.ShouldNotContain("dbo.ReferenceSearchParam");
    }

    private async Task<string> CompileCompartmentSearchAsync(SearchParameterInfo subject)
    {
        var allDefinitions = Substitute.For<ISearchParameterDefinitionManager>();
        allDefinitions.TryGetSearchParameter(
                Arg.Any<string>(),
                Arg.Any<string>(),
                out Arg.Any<SearchParameterInfo>())
            .Returns(callInfo =>
            {
                if (callInfo.ArgAt<string>(0) == "Observation" &&
                    callInfo.ArgAt<string>(1) == "subject")
                {
                    callInfo[2] = subject;
                    return true;
                }

                callInfo[2] = null!;
                return false;
            });
        var searchableDefinitions = new SearchableSearchParameterDefinitionManager(allDefinitions);
        var initializer = new SqlServerTenantInitializer(
            Substitute.For<ISchemaDeployer>(),
            _cacheRegistry,
            NullLogger<SqlServerTenantInitializer>.Instance);
        var factory = new SqlServerTenantServiceFactory(
            SingleTenantStore(),
            NullLoggerFactory.Instance,
            new RecyclableMemoryStreamManager(),
            initializer,
            new ManagedIdentityConnectionStringValidator(
                "Development",
                NullLogger<ManagedIdentityConnectionStringValidator>.Instance),
            _sql,
            (_, _) => searchableDefinitions);
        var service = await factory.GetSearchServiceAsync(TenantId, CancellationToken.None);
        var options = new SearchOptions
        {
            ResourceType = "Observation",
            Expression = new CompartmentSearchExpression(
                "Patient",
                "patient-id",
                new HashSet<string> { "Observation" }),
        };

        _ = await service.CountAsync(options, CancellationToken.None);

        allDefinitions.Received().TryGetSearchParameter(
            "Observation",
            "subject",
            out Arg.Any<SearchParameterInfo>());
        return _sql.LastCommandText.ShouldNotBeNull();
    }

    private static ITenantConfigurationStore SingleTenantStore()
    {
        var tenant = new TenantConfiguration
        {
            TenantId = TenantId,
            DisplayName = "Test Tenant",
            FhirVersion = "4.0",
            Storage = new TenantStorageConfiguration
            {
                Type = "SqlServer",
                ConnectionString = "Server=localhost;Database=Ignixa;Integrated Security=true;",
            },
        };
        var store = Substitute.For<ITenantConfigurationStore>();
        store.Mode.Returns(TenantMode.Isolated);
        store.GetTenantConfigurationAsync(TenantId, Arg.Any<CancellationToken>()).Returns(tenant);
        store.GetAllTenantsAsync(Arg.Any<CancellationToken>()).Returns([tenant]);
        return store;
    }

    /// <summary>Records the last reader command and answers it with no rows.</summary>
    private sealed class EmptySqlExecutionService : ISqlExecutionService
    {
        public string? LastCommandText { get; private set; }

        public Task<IReadOnlyList<TResult>> ExecuteReaderAsync<TResult>(
            int tenantId,
            SqlCommand command,
            Func<SqlDataReader, TResult> readRow,
            CancellationToken cancellationToken,
            SqlCommandIdempotency idempotency = SqlCommandIdempotency.Idempotent)
        {
            LastCommandText = command.CommandText;
            return Task.FromResult<IReadOnlyList<TResult>>([]);
        }

        public Task<int> ExecuteNonQueryAsync(
            int tenantId, SqlCommand command, CancellationToken cancellationToken, SqlCommandIdempotency idempotency) =>
            Task.FromResult(0);

        public Task<TResult> ExecuteInTransactionAsync<TResult>(
            int tenantId, Func<ISqlTransactionContext, CancellationToken, Task<TResult>> work, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task ExecuteInTransactionAsync(
            int tenantId, Func<ISqlTransactionContext, CancellationToken, Task> work, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
