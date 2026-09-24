using System.Data;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Ignixa.Abstractions;
using Ignixa.Api.E2ETests._Infrastructure;
using Ignixa.Application.Features.Conformance;
using Ignixa.Application.Features.Search;
using Ignixa.DataLayer.SqlServer;
using Ignixa.DataLayer.SqlServer.Indexing;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit.Abstractions;

namespace Ignixa.Api.E2ETests;

public class SqlRemoteConformanceRefreshTests(ITestOutputHelper output)
{
    private const string Canonical = "http://example.org/SearchParameter/remote-patient-identifier";
    private const string SearchCode = "remote-identifier";
    private const string IdentifierSystem = "urn:remote-conformance";
    private const string PackageId = "test.remote.conformance";
    private const string PackageVersion = "1.0.0";

    [SqlFact]
    public async Task GivenTwoWarmedHosts_WhenRemoteRefreshFailsAfterReplay_ThenAnEmptyPollRetriesBeforePublishingAndIndexesWrites()
    {
        var configured = Environment.GetEnvironmentVariable("TEST_SQL_CONNECTION_STRING")
            ?? throw new InvalidOperationException("A SQL test connection is required.");
        var database = $"IgnixaRemoteConformance_{Guid.NewGuid():N}";
        var connectionString = new SqlConnectionStringBuilder(configured) { InitialCatalog = database }.ConnectionString;
        var master = new SqlConnectionStringBuilder(configured) { InitialCatalog = "master" }.ConnectionString;
        using var names = new SqlCommandBuilder();
        var quotedDatabase = names.QuoteIdentifier(database);
        await ExecuteDatabaseCommandAsync(master, $"CREATE DATABASE {quotedDatabase}");
        output.WriteLine($"Owned SQL catalog: {database}");
        try
        {
            await AssertRemoteRefreshAsync(connectionString);
        }
        finally
        {
            using var pool = new SqlConnection(connectionString);
            SqlConnection.ClearPool(pool);
            await ExecuteDatabaseCommandAsync(master, $"DROP DATABASE {quotedDatabase}");
            output.WriteLine($"Removed owned SQL catalog: {database}");
        }
    }

    private async Task AssertRemoteRefreshAsync(string connectionString)
    {
        await using var templateA = new IgnixaApiFixture();
        var gate = new ReferenceSyncGate();
        await using var templateB = new InterceptedApiFixture(gate);
        await using var hostA = CreateHost(templateA, connectionString, pollSeconds: 3600);
        await using var hostB = CreateHost(templateB, connectionString, pollSeconds: 1);
        using var clientA = hostA.CreateClient();
        using var clientB = hostB.CreateClient();
        var stateA = hostA.Services.GetRequiredService<ConformanceState>();
        var stateB = hostB.Services.GetRequiredService<ConformanceState>();
        ReferenceEquals(stateA, stateB).ShouldBeFalse();
        stateA.IsInitialized.ShouldBeTrue();
        stateB.IsInitialized.ShouldBeTrue();
        var versionsB = hostB.Services.GetRequiredService<IFhirVersionContext>();
        var warmDefinitions = versionsB.GetSearchParameterDefinitionManager(FhirVersion.R4, 1);
        var warmIndexer = versionsB.GetSearchIndexer(FhirVersion.R4, 1);
        var registryB = hostB.Services.GetRequiredService<SqlServerSearchIndexCacheRegistry>();
        var warmCache = await registryB.GetOrCreateAsync(1, CancellationToken.None);
        (await warmCache.GetSearchParamIdAsync(Canonical, CancellationToken.None)).ShouldBeNull();
        var cacheA = await hostA.Services.GetRequiredService<SqlServerSearchIndexCacheRegistry>()
            .GetOrCreateAsync(1, CancellationToken.None);
        ReferenceEquals(cacheA, warmCache).ShouldBeFalse();

        var identifier = Guid.NewGuid().ToString("N");
        var beforeId = $"remote-before-{identifier}";
        var afterId = $"remote-after-{identifier}";
        await PutPatientAsync(clientB, beforeId, identifier);
        await AssertUnsupportedAsync(clientA, identifier);
        await AssertUnsupportedAsync(clientB, identifier);
        await AssertCapabilityAsync(clientB, expected: false);
        var beforeCursor = stateB.LastProcessedEventId;
        gate.Arm(() => stateB.FindByCanonical(Canonical) is not null);

        // Both resource extraction and activation use the production SQL repository and pipeline.
        var packages = hostA.Services.GetRequiredService<IPackageResourceRepository>();
        await packages.UpsertAsync(new PackageResource
        {
            PackageId = PackageId,
            PackageVersion = PackageVersion,
            ResourceType = "SearchParameter",
            ResourceId = SearchCode,
            Canonical = Canonical,
            Version = PackageVersion,
            FhirVersion = "4.0.1",
            ResourceJson = $$"""
                {"resourceType":"SearchParameter","id":"{{SearchCode}}","url":"{{Canonical}}",
                 "version":"{{PackageVersion}}","name":"RemotePatientIdentifier","status":"active",
                 "code":"{{SearchCode}}","base":["Patient"],"type":"token","expression":"Patient.identifier"}
                """
        }, CancellationToken.None);
        var activation = await hostA.Services.GetRequiredService<PackageActivationPipeline>()
            .ActivateAsync(PackageId, PackageVersion, CancellationToken.None);
        activation.Success.ShouldBeTrue();
        stateA.FindByCanonical(Canonical).ShouldNotBeNull();

        try
        {
            await AssertCaughtUpAsync(stateB, stateA.LastProcessedEventId);
            output.WriteLine($"Remote projection caught up: {beforeCursor} -> {stateB.LastProcessedEventId}.");
            await AssertSignalAsync(gate.FirstReadStarted.Task,
                "Remote polling must synchronize the existing SQL reference cache after replay.");
            var appliedCursor = stateB.LastProcessedEventId;
            appliedCursor.ShouldBeGreaterThan(beforeCursor);
            appliedCursor.ShouldBe(stateA.LastProcessedEventId);
            output.WriteLine($"Applied cursor {beforeCursor} -> {appliedCursor}; first reference read is blocked.");

            // The projection is newer, but writes must not receive new definitions/indexers while their
            // existing reference cache still lacks the canonical's physical SQL identity.
            ReferenceEquals(versionsB.GetSearchIndexer(FhirVersion.R4, 1), warmIndexer).ShouldBeTrue();
            ReferenceEquals(versionsB.GetSearchParameterDefinitionManager(FhirVersion.R4, 1), warmDefinitions)
                .ShouldBeTrue();
            await AssertUnsupportedAsync(clientB, identifier);
            await AssertCapabilityAsync(clientB, expected: false);
            (await warmCache.GetSearchParamIdAsync(Canonical, CancellationToken.None)).ShouldBeNull();

            gate.FailFirstRead.TrySetResult();
            await AssertSignalAsync(gate.RetryReadStarted.Task,
                "An empty poll must retry failed consumer refresh even though the applied cursor already advanced.");
            stateB.LastProcessedEventId.ShouldBe(appliedCursor);
            stateA.LastProcessedEventId.ShouldBe(appliedCursor);
            output.WriteLine($"Retry at unchanged cursor {appliedCursor}; no second activation or event append.");
            ReferenceEquals(versionsB.GetSearchIndexer(FhirVersion.R4, 1), warmIndexer).ShouldBeTrue();
            await AssertUnsupportedAsync(clientB, identifier);
            gate.AllowRetry.TrySetResult();

            await AssertEventuallySupportedAsync(clientB, identifier);
            ReferenceEquals(versionsB.GetSearchIndexer(FhirVersion.R4, 1), warmIndexer).ShouldBeFalse();
            ReferenceEquals(await registryB.GetOrCreateAsync(1, CancellationToken.None), warmCache).ShouldBeTrue();
            warmDefinitions.GetSearchParameter("Patient", SearchCode).Url.ShouldBe(new Uri(Canonical));
            (await warmCache.GetSearchParamIdAsync(Canonical, CancellationToken.None)).ShouldNotBeNull();
            await AssertCapabilityAsync(clientB, expected: true, waitForRefresh: true);
            await PutPatientAsync(clientB, afterId, identifier);
            await AssertPatientsAsync(clientB, identifier, afterId);
            await AssertPatientsAsync(clientA, identifier, afterId);
            await AssertPhysicalIndexAsync(connectionString, identifier);
            output.WriteLine("B's post-refresh write has a physical token index row and is searchable from both hosts.");
        }
        finally
        {
            gate.FailFirstRead.TrySetResult();
            gate.AllowRetry.TrySetResult();
        }
    }

    private static WebApplicationFactory<Program> CreateHost(
        IgnixaApiFixture template, string connectionString, int pollSeconds) =>
        template.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Tenants:Configurations:1:Storage:ConnectionString"] = connectionString,
                    ["Tenants:Configurations:1:Storage:Type"] = "SqlServer",
                    ["Tenants:Configurations:0:Storage:Type"] = "SqlServer",
                    ["Tenants:Configurations:0:Storage:InheritConnectionStringFromTenant"] = "1",
                    ["Conformance:SyncIntervalSeconds"] = pollSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)
                })));

    private static async Task AssertSignalAsync(Task signal, string reason)
    {
        var completed = await Task.WhenAny(signal, Task.Delay(TimeSpan.FromSeconds(20)));
        ReferenceEquals(completed, signal).ShouldBeTrue(reason);
        await signal;
    }

    private static async Task AssertCaughtUpAsync(ConformanceState state, long expectedCursor)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (state.LastProcessedEventId < expectedCursor && !timeout.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        state.LastProcessedEventId.ShouldBe(expectedCursor, "The real hosted poller must replay A's persisted activation.");
    }

    private static async Task ExecuteDatabaseCommandAsync(string connectionString, string statement)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        using var command = new SqlCommand("EXEC sys.sp_executesql @statement", connection);
        command.Parameters.Add("@statement", SqlDbType.NVarChar, -1).Value = statement;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task PutPatientAsync(HttpClient client, string id, string identifier)
    {
        using var content = new StringContent($$"""
            {"resourceType":"Patient","id":"{{id}}",
             "identifier":[{"system":"{{IdentifierSystem}}","value":"{{identifier}}"}]}
            """, Encoding.UTF8, "application/fhir+json");
        using var response = await client.PutAsync($"/tenant/1/Patient/{id}", content);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, body);
    }

    private static async Task<HttpResponseMessage> SearchAsync(HttpClient client, string identifier)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"/tenant/1/Patient?{SearchCode}={Uri.EscapeDataString($"{IdentifierSystem}|{identifier}")}");
        request.Headers.Add("Prefer", "handling=strict");
        return await client.SendAsync(request);
    }

    private static async Task AssertUnsupportedAsync(HttpClient client, string identifier)
    {
        using var response = await SearchAsync(client, identifier);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, body);
        body.ShouldContain(SearchCode);
    }

    private static async Task AssertEventuallySupportedAsync(HttpClient client, string identifier)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (true)
        {
            using var response = await SearchAsync(client, identifier);
            if (response.StatusCode == HttpStatusCode.OK || timeout.IsCancellationRequested)
            {
                response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }
    }

    private static async Task AssertPatientsAsync(HttpClient client, string identifier, params string[] ids)
    {
        using var response = await SearchAsync(client, identifier);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        var entries = JsonNode.Parse(body)!["entry"]!.AsArray();
        entries.Select(entry => entry!["resource"]!["id"]!.GetValue<string>()).Order().ShouldBe(ids.Order());
    }

    private static async Task AssertCapabilityAsync(HttpClient client, bool expected, bool waitForRefresh = false)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (true)
        {
            using var response = await client.GetAsync("/tenant/1/metadata");
            var body = await response.Content.ReadAsStringAsync();
            response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
            var resources = JsonNode.Parse(body)!["rest"]!.AsArray()[0]!["resource"]!.AsArray();
            var patient = resources.Single(resource => resource!["type"]!.GetValue<string>() == "Patient");
            var parameters = patient!["searchParam"]!.AsArray();
            var advertised = parameters.Any(parameter => parameter!["name"]!.GetValue<string>() == SearchCode);
            if (advertised == expected || !waitForRefresh || timeout.IsCancellationRequested)
            {
                advertised.ShouldBe(expected);
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }
    }

    private static async Task AssertPhysicalIndexAsync(string connectionString, string identifier)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        using var command = new SqlCommand(
            """
            SELECT COUNT(*)
            FROM dbo.TokenSearchParam t JOIN dbo.SearchParam p ON t.SearchParamId = p.SearchParamId
            WHERE p.Uri = @Canonical AND t.Code = @Code
            """, connection);
        command.Parameters.Add("@Canonical", SqlDbType.VarChar, 128).Value = Canonical;
        command.Parameters.Add("@Code", SqlDbType.NVarChar, 256).Value = identifier;
        Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture)
            .ShouldBe(1, "Only B's post-refresh write should have been indexed under the new physical identity.");
    }

    private sealed class InterceptedApiFixture(ReferenceSyncGate gate) : IgnixaApiFixture
    {
        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.UseServiceProviderFactory(new AutofacServiceProviderFactory(container =>
                container.RegisterDecorator<ISqlExecutionService>((_, _, inner) => new GatedSqlExecutionService(inner, gate))));
            return base.CreateHost(builder);
        }
    }

    private sealed class ReferenceSyncGate
    {
        private Func<bool>? _isReady;
        private int _attempts;

        public TaskCompletionSource FirstReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource FailFirstRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource RetryReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AllowRetry { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Arm(Func<bool> isReady) => _isReady = isReady;

        public async Task BeforeReadAsync(int tenantId, SqlCommand command, CancellationToken cancellationToken)
        {
            if (tenantId != 1 || _isReady?.Invoke() != true ||
                command.CommandText != "SELECT SearchParamId, Uri FROM dbo.SearchParam")
            {
                return;
            }

            var attempt = Interlocked.Increment(ref _attempts);
            if (attempt == 1)
            {
                cancellationToken.CanBeCanceled.ShouldBeTrue("The hosted poller's cancellation must reach SQL.");
                FirstReadStarted.TrySetResult();
                await FailFirstRead.Task.WaitAsync(cancellationToken);
                throw new InvalidOperationException("Injected one-time reference refresh read failure after conformance replay.");
            }

            if (attempt == 2)
            {
                RetryReadStarted.TrySetResult();
                await AllowRetry.Task.WaitAsync(cancellationToken);
            }
        }
    }

    private sealed class GatedSqlExecutionService(ISqlExecutionService inner, ReferenceSyncGate gate) : ISqlExecutionService
    {
        public async Task<IReadOnlyList<TResult>> ExecuteReaderAsync<TResult>(
            int tenantId, SqlCommand command, Func<SqlDataReader, TResult> readRow,
            CancellationToken cancellationToken, SqlCommandIdempotency idempotency = SqlCommandIdempotency.Idempotent)
        {
            await gate.BeforeReadAsync(tenantId, command, cancellationToken);
            return await inner.ExecuteReaderAsync(tenantId, command, readRow, cancellationToken, idempotency);
        }

        public Task<int> ExecuteNonQueryAsync(int tenantId, SqlCommand command, CancellationToken cancellationToken,
            SqlCommandIdempotency idempotency = SqlCommandIdempotency.Idempotent) =>
            inner.ExecuteNonQueryAsync(tenantId, command, cancellationToken, idempotency);

        public Task<TResult> ExecuteInTransactionAsync<TResult>(
            int tenantId, Func<ISqlTransactionContext, CancellationToken, Task<TResult>> work,
            CancellationToken cancellationToken) => inner.ExecuteInTransactionAsync(tenantId, work, cancellationToken);

        public Task ExecuteInTransactionAsync(
            int tenantId, Func<ISqlTransactionContext, CancellationToken, Task> work,
            CancellationToken cancellationToken) => inner.ExecuteInTransactionAsync(tenantId, work, cancellationToken);
    }

    private sealed class SqlFactAttribute : FactAttribute
    {
        public SqlFactAttribute()
        {
            if (Environment.GetEnvironmentVariable("TEST_USE_FILESYSTEM")?.Equals("true", StringComparison.OrdinalIgnoreCase) == true)
            {
                Skip = "Requires independent SQL-backed Web hosts and persisted package activation.";
            }
        }
    }
}
