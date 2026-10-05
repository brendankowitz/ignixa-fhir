using System.Data;
using System.Net;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Ignixa.Abstractions;
using Ignixa.Api.E2ETests._Infrastructure;
using Ignixa.Api.Services;
using Ignixa.Application.Features.Conformance;
using Ignixa.Application.Features.Search;
using Ignixa.Conformance.Events.Abstractions;
using Ignixa.DataLayer.SqlServer;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;

namespace Ignixa.Api.E2ETests;

public class SqlActivationConcurrencyTests
{
    private const string FirstRoot = "http://hl7.org/fhir/SearchParameter/Patient-identifier";
    private const string SecondRoot = "http://example.org/SearchParameter/race-other";
    private const string ContendedCanonical = "http://example.org/SearchParameter/race-override";

    [SqlFact]
    public async Task GivenTwoHostsValidatedDifferentRoots_WhenOneCommitsBeforeTheOtherAppends_ThenOnlyTheWinnerIsPublished()
    {
        var configured = Environment.GetEnvironmentVariable("TEST_SQL_CONNECTION_STRING")
            ?? throw new InvalidOperationException("A SQL test connection is required.");
        var database = $"IgnixaActivationRace_{Guid.NewGuid():N}";
        var connectionString = new SqlConnectionStringBuilder(configured) { InitialCatalog = database }.ConnectionString;
        var master = new SqlConnectionStringBuilder(configured) { InitialCatalog = "master" }.ConnectionString;
        using var names = new SqlCommandBuilder();
        var quoted = names.QuoteIdentifier(database);
        await ExecuteAsync(master, $"CREATE DATABASE {quoted}");
        try
        {
            await AssertConcurrentActivationAsync(connectionString);
        }
        finally
        {
            using var pool = new SqlConnection(connectionString);
            SqlConnection.ClearPool(pool);
            await ExecuteAsync(master, $"DROP DATABASE {quoted}");
        }
    }

    private static async Task AssertConcurrentActivationAsync(string connectionString)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var cancellationToken = timeout.Token;
        var gateA = new AppendGate();
        var gateB = new AppendGate();
        await using var templateA = new GatedFixture(gateA);
        await using var templateB = new GatedFixture(gateB);
        long finalCount;
        await using (var hostA = CreateHost(templateA, connectionString))
        {
            using var clientA = hostA.CreateClient();
            await WaitForHostedReplayAsync(hostA.Services, cancellationToken);
            await StoreAsync(hostA.Services, "hl7.fhir.r4.core", "identifier", FirstRoot, null);
            await StoreAsync(hostA.Services, "hl7.fhir.r4.core", "other", SecondRoot, null);
            var pipelineA = hostA.Services.GetRequiredService<PackageActivationPipeline>();
            (await pipelineA.ActivateAsync("hl7.fhir.r4.core", "1", cancellationToken)).Success.ShouldBeTrue();
            var stateA = hostA.Services.GetRequiredService<ConformanceState>();

            await using var hostB = CreateHost(templateB, connectionString);
            using var clientB = hostB.CreateClient();
            await WaitForHostedReplayAsync(hostB.Services, cancellationToken);
            var stateB = hostB.Services.GetRequiredService<ConformanceState>();
            var pipelineB = hostB.Services.GetRequiredService<PackageActivationPipeline>();
            stateB.LastProcessedEventId.ShouldBe(stateA.LastProcessedEventId);
            ReferenceEquals(stateA, stateB).ShouldBeFalse();
            long snapshotPosition = stateA.LastProcessedEventId;
            long beforeCount = await CountAsync(connectionString);

            await StoreAsync(hostA.Services, "race.winner", "identifier", ContendedCanonical, FirstRoot);
            gateA.Arm();
            var first = pipelineA.ActivateAsync("race.winner", "1", cancellationToken);
            Task<ActivationResult>? second = null;
            try
            {
                await gateA.Reached.Task.WaitAsync(cancellationToken);
                await StoreAsync(hostB.Services, "race.loser", "other", ContendedCanonical, SecondRoot);
                gateB.Arm();
                second = pipelineB.ActivateAsync("race.loser", "1", cancellationToken);
                await gateB.Reached.Task.WaitAsync(cancellationToken);

                // Both pipelines have finished local validation; neither store has taken its append lock.
                stateA.LastProcessedEventId.ShouldBe(snapshotPosition);
                stateB.LastProcessedEventId.ShouldBe(snapshotPosition);
                stateA.FindByCanonical(ContendedCanonical).ShouldBeNull();
                stateB.FindByCanonical(ContendedCanonical).ShouldBeNull();

                gateA.Release.TrySetResult();
                (await first).Success.ShouldBeTrue();
                gateB.Release.TrySetResult();
                var rejected = await second;

                rejected.Success.ShouldBeFalse();
                rejected.Issues.ShouldContain(issue => issue.Code == "CONFORMANCE_CONFLICT");
                stateB.LastProcessedEventId.ShouldBe(snapshotPosition);
                stateB.FindByCanonical(ContendedCanonical).ShouldBeNull();
                stateB.Packages.ShouldNotContainKey("race.loser@1");
                finalCount = await CountAsync(connectionString);
                finalCount.ShouldBe(beforeCount + 2);

                await stateB.CatchUpAsync(hostB.Services.GetRequiredService<ISourceEventStore>(), cancellationToken);
                var retry = await pipelineB.ActivateAsync("race.loser", "1", cancellationToken);
                retry.Success.ShouldBeFalse();
                retry.Issues.ShouldContain(issue => issue.Code == "SP_STORAGE_IDENTITY");
                (await CountAsync(connectionString)).ShouldBe(finalCount);
            }
            finally
            {
                gateA.Release.TrySetResult();
                gateB.Release.TrySetResult();
                await first;
                if (second is not null)
                {
                    await second;
                }
            }
        }

        await using var restartTemplate = new IgnixaApiFixture();
        await using var restarted = CreateHost(restartTemplate, connectionString);
        using var client = restarted.CreateClient();
        await WaitForHostedReplayAsync(restarted.Services, cancellationToken);
        var replayed = restarted.Services.GetRequiredService<ConformanceState>();
        replayed.IsInitialized.ShouldBeTrue();
        replayed.Packages.ShouldContainKey("race.winner@1");
        replayed.Packages.ShouldNotContainKey("race.loser@1");
        replayed.FindByCanonical(ContendedCanonical)!.OverridesCanonical.ShouldBe(FirstRoot);
        var definitions = restarted.Services.GetRequiredService<IFhirVersionContext>()
            .GetSearchParameterDefinitionManager(FhirVersion.R4, 1);
        definitions.GetSearchParameter("Patient", "identifier").Url.ShouldBe(new Uri(ContendedCanonical));
        using var request = new HttpRequestMessage(HttpMethod.Get, "/tenant/1/Patient?identifier=none");
        request.Headers.Add("Prefer", "handling=strict");
        using var response = await client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await CountAsync(connectionString)).ShouldBe(finalCount);
    }

    private static async Task WaitForHostedReplayAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var initializer = services.GetServices<IHostedService>().OfType<ConformanceStateInitializerService>().Single();
        await (initializer.ExecuteTask ?? throw new InvalidOperationException("The real conformance initializer was not started."))
            .WaitAsync(cancellationToken);
    }

    private static async Task StoreAsync(IServiceProvider services, string package, string code, string canonical, string? derived)
    {
        var json = new System.Text.Json.Nodes.JsonObject
        {
            ["resourceType"] = "SearchParameter", ["id"] = code, ["url"] = canonical,
            ["version"] = "1", ["name"] = "ConcurrentParameter", ["status"] = "active",
            ["code"] = code, ["base"] = new System.Text.Json.Nodes.JsonArray("Patient"),
            ["type"] = "token", ["expression"] = "Patient.identifier"
        };
        if (derived is not null)
        {
            json["derivedFrom"] = derived;
        }
        await services.GetRequiredService<IPackageResourceRepository>().UpsertAsync(new PackageResource
        {
            PackageId = package, PackageVersion = "1", ResourceType = "SearchParameter",
            ResourceId = code, Canonical = canonical, Version = "1", FhirVersion = "4.0.1",
            ResourceJson = json.ToJsonString()
        }, CancellationToken.None);
    }

    private static WebApplicationFactory<Program> CreateHost(IgnixaApiFixture template, string connectionString) =>
        template.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Tenants:Configurations:1:Storage:ConnectionString"] = connectionString,
                ["Tenants:Configurations:1:Storage:Type"] = "SqlServer",
                ["Tenants:Configurations:0:Storage:Type"] = "SqlServer",
                ["Conformance:SyncIntervalSeconds"] = "3600"
            })));

    private static async Task<long> CountAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        using var command = new SqlCommand("SELECT COUNT_BIG(*) FROM dbo.SourceEvents", connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static async Task ExecuteAsync(string connectionString, string statement)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        using var command = new SqlCommand("EXEC sys.sp_executesql @statement", connection);
        command.Parameters.Add("@statement", SqlDbType.NVarChar, -1).Value = statement;
        await command.ExecuteNonQueryAsync();
    }

    private sealed class GatedFixture(AppendGate gate) : IgnixaApiFixture
    {
        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.UseServiceProviderFactory(new AutofacServiceProviderFactory(container =>
                container.RegisterDecorator<ISqlExecutionService>((_, _, inner) => new GatedExecutionService(inner, gate))));
            return base.CreateHost(builder);
        }
    }

    private sealed class AppendGate
    {
        private int _armed;
        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Arm() => Interlocked.Exchange(ref _armed, 1);

        public async Task BeforeCommandAsync(SqlCommand command, CancellationToken cancellationToken)
        {
            bool isAppendBoundary = command.CommandText.Contains("SourceEvents", StringComparison.Ordinal)
                && (command.CommandText.Contains("TABLOCKX", StringComparison.Ordinal)
                    || command.CommandText.StartsWith("INSERT INTO dbo.SourceEvents", StringComparison.Ordinal));
            if (isAppendBoundary && Interlocked.Exchange(ref _armed, 0) == 1)
            {
                Reached.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
        }
    }

    private sealed class GatedExecutionService(ISqlExecutionService inner, AppendGate gate) : ISqlExecutionService
    {
        public async Task<IReadOnlyList<T>> ExecuteReaderAsync<T>(
            int tenantId, SqlCommand command, Func<SqlDataReader, T> readRow, CancellationToken cancellationToken,
            SqlCommandIdempotency idempotency = SqlCommandIdempotency.Idempotent)
        {
            await gate.BeforeCommandAsync(command, cancellationToken);
            return await inner.ExecuteReaderAsync(tenantId, command, readRow, cancellationToken, idempotency);
        }
        public Task<int> ExecuteNonQueryAsync(int tenantId, SqlCommand command, CancellationToken cancellationToken,
            SqlCommandIdempotency idempotency = SqlCommandIdempotency.Idempotent) =>
            inner.ExecuteNonQueryAsync(tenantId, command, cancellationToken, idempotency);
        public Task<T> ExecuteInTransactionAsync<T>(int tenantId,
            Func<ISqlTransactionContext, CancellationToken, Task<T>> work, CancellationToken cancellationToken) =>
            inner.ExecuteInTransactionAsync(tenantId, (context, token) =>
                work(new GatedContext(context, gate), token), cancellationToken);
        public Task ExecuteInTransactionAsync(int tenantId,
            Func<ISqlTransactionContext, CancellationToken, Task> work, CancellationToken cancellationToken) =>
            inner.ExecuteInTransactionAsync(tenantId, (context, token) =>
                work(new GatedContext(context, gate), token), cancellationToken);

        private sealed class GatedContext(ISqlTransactionContext inner, AppendGate gate) : ISqlTransactionContext
        {
            public async Task<int> ExecuteNonQueryAsync(SqlCommand command, CancellationToken cancellationToken)
            {
                await gate.BeforeCommandAsync(command, cancellationToken);
                return await inner.ExecuteNonQueryAsync(command, cancellationToken);
            }
            public async Task<IReadOnlyList<T>> ExecuteReaderAsync<T>(
                SqlCommand command, Func<SqlDataReader, T> readRow, CancellationToken cancellationToken)
            {
                await gate.BeforeCommandAsync(command, cancellationToken);
                return await inner.ExecuteReaderAsync(command, readRow, cancellationToken);
            }
        }
    }

    private sealed class SqlFactAttribute : FactAttribute
    {
        public SqlFactAttribute()
        {
            if (Environment.GetEnvironmentVariable("TEST_USE_FILESYSTEM")?.Equals("true", StringComparison.OrdinalIgnoreCase) == true)
            {
                Skip = "Requires independent SQL-backed activation hosts.";
            }
        }
    }
}
