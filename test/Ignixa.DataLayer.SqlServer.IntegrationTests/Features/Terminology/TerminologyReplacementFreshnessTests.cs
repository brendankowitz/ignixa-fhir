using System.Text.Json.Nodes;
using Ignixa.DataLayer.SqlServer.Features.Terminology;
using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Ignixa.Validation.Abstractions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests.Features.Terminology;

public sealed class TerminologyReplacementFreshnessTests(TerminologyReplacementFixture fixture)
    : IClassFixture<TerminologyReplacementFixture>, IDisposable
{
    private readonly MemoryCache _sharedCache = new(new MemoryCacheOptions());
    private TerminologyTestFixture Database => fixture.Database;

    [Fact]
    public async Task GivenCachedMissingSystem_WhenImported_ThenTheNextRequestFindsItsCode()
    {
        var canonical = NewCanonical();
        (await Service().LookupCodeAsync(canonical, "car", null, CancellationToken.None)).Found.ShouldBeFalse();

        await ImportCodeSystemAsync(canonical, TerminologyContractResources.CodeSystem(canonical, "1"));

        (await Service().LookupCodeAsync(canonical, "car", null, CancellationToken.None)).Found.ShouldBeTrue();
    }

    [Fact]
    public async Task GivenWarmPositiveAndNegativeLookups_WhenReplaced_ThenSuccessiveRequestsReadTheNewContent()
    {
        var canonical = NewCanonical();
        await ImportCodeSystemAsync(canonical, TerminologyContractResources.CodeSystem(canonical, "1"));
        var original = Service();
        (await original.LookupCodeAsync(canonical, "car", "1", CancellationToken.None)).Display.ShouldBe("Car");
        (await original.LookupCodeAsync(canonical, "truck", "1", CancellationToken.None)).Found.ShouldBeTrue();
        (await original.LookupCodeAsync(canonical, "new", "1", CancellationToken.None)).Found.ShouldBeFalse();
        var replacement = JsonNode.Parse(TerminologyContractResources.CodeSystem(canonical, "1", "New car"))!;
        replacement["concept"]![0]!["concept"]![1]!["code"] = "new";

        await ImportCodeSystemAsync(canonical, replacement.ToJsonString());

        var next = Service();
        (await next.LookupCodeAsync(canonical, "car", "1", CancellationToken.None)).Display.ShouldBe("New car");
        (await next.LookupCodeAsync(canonical, "truck", "1", CancellationToken.None)).Found.ShouldBeFalse();
        (await next.LookupCodeAsync(canonical, "new", "1", CancellationToken.None)).Found.ShouldBeTrue();
    }

    [Fact]
    public async Task GivenWarmExpansionAndValidation_WhenReplaced_ThenAddedAndRemovedCodesAreImmediatelyVisible()
    {
        var canonical = NewCanonical();
        var system = NewCanonical();
        await ImportValueSetAsync(canonical, system, "car");
        var original = Service();
        (await original.ExpandValueSetAsync(new ExpansionParameters(canonical), CancellationToken.None))!
            .Contains.Single().Code.ShouldBe("car");
        (await original.ValidateCodeAsync(system, "car", null, canonical, CancellationToken.None)).IsValid.ShouldBeTrue();
        (await original.ValidateCodeAsync(system, "new", null, canonical, CancellationToken.None)).IsValid.ShouldBeFalse();

        await ImportValueSetAsync(canonical, system, "new");

        var next = Service();
        (await next.ExpandValueSetAsync(new ExpansionParameters(canonical), CancellationToken.None))!
            .Contains.Single().Code.ShouldBe("new");
        (await next.ValidateCodeAsync(system, "car", null, canonical, CancellationToken.None)).IsValid.ShouldBeFalse();
        (await next.ValidateCodeAsync(system, "new", null, canonical, CancellationToken.None)).IsValid.ShouldBeTrue();
    }

    [Fact]
    public async Task GivenCachedUnimportedValueSetValidation_WhenImported_ThenTheNextRequestIsValid()
    {
        var canonical = NewCanonical();
        var system = NewCanonical();
        (await Service().ValidateCodeAsync(system, "car", null, canonical, CancellationToken.None)).IsValid.ShouldBeFalse();

        await ImportValueSetAsync(canonical, system, "car");

        (await Service().ValidateCodeAsync(system, "car", null, canonical, CancellationToken.None)).IsValid.ShouldBeTrue();
    }

    [Fact]
    public async Task GivenWarmDependentValidation_WhenCodeSystemChanges_ThenCaseRulesAndSuggestedDisplayAreRefreshed()
    {
        var system = NewCanonical();
        var valueSet = NewCanonical();
        await ImportCodeSystemAsync(system, TerminologyContractResources.CodeSystem(system, "1"));
        await ImportValueSetAsync(valueSet, system, "car");
        var original = Service();
        (await original.ValidateCodeAsync(system, "CAR", null, valueSet, CancellationToken.None)).IsValid.ShouldBeFalse();
        (await original.ValidateBindingAsync(valueSet, BindingStrength.Required, system, "car", "Car", "1", CancellationToken.None))
            .SuggestedDisplay.ShouldBeNull();

        await ImportCodeSystemAsync(system, TerminologyContractResources.CodeSystem(system, "1", "New car", caseSensitive: false));

        var next = Service();
        (await next.ValidateCodeAsync(system, "CAR", null, valueSet, CancellationToken.None)).IsValid.ShouldBeTrue();
        (await next.ValidateBindingAsync(valueSet, BindingStrength.Required, system, "car", "Car", "1", CancellationToken.None))
            .SuggestedDisplay.ShouldBe("New car");
    }

    [Fact]
    public async Task GivenAnOldReadFinishesAfterReplacement_WhenSubsequentRequestsRun_ThenItCannotRepopulateStaleResults()
    {
        var canonical = NewCanonical();
        await ImportCodeSystemAsync(canonical, TerminologyContractResources.CodeSystem(canonical, "1"));
        var gated = new GatedConceptRead(Database.SqlExecutionService);
        var oldRead = Service(gated).LookupCodeAsync(canonical, "car", "1", CancellationToken.None);
        try
        {
            await gated.Captured.Task.WaitAsync(TimeSpan.FromSeconds(20));
            await ImportCodeSystemAsync(canonical, TerminologyContractResources.CodeSystem(canonical, "1", "New car"));
            (await Service().LookupCodeAsync(canonical, "car", "1", CancellationToken.None)).Display.ShouldBe("New car");
        }
        finally
        {
            gated.Release.TrySetResult();
            await oldRead;
        }

        (await oldRead).Display.ShouldBe("Car");
        (await Service().LookupCodeAsync(canonical, "car", "1", CancellationToken.None)).Display.ShouldBe("New car");
    }

    private SqlServerTerminologyService Service(ISqlExecutionService? sql = null)
        => new(sql ?? Database.SqlExecutionService, 0, _sharedCache, NullLogger<SqlServerTerminologyService>.Instance);

    private async Task ImportCodeSystemAsync(string canonical, string json)
    {
        var package = await TerminologyContractResources.StoreAsync(Database, "CodeSystem", canonical, "1", json);
        var result = await Database.CreateSqlServerImporter().ImportCodeSystemAsync(1, package, CancellationToken.None);
        result.Success.ShouldBeTrue(result.ErrorMessage);
    }

    private async Task ImportValueSetAsync(string canonical, string system, string code)
    {
        var package = await TerminologyContractResources.StoreAsync(Database, "ValueSet", canonical, "1",
            TerminologyContractResources.ValueSet(canonical, "1", system, code));
        var result = await Database.CreateSqlServerImporter().ImportValueSetAsync(1, package, CancellationToken.None);
        result.Success.ShouldBeTrue(result.ErrorMessage);
    }

    private static string NewCanonical() => $"http://terminology-contract.example/{Guid.NewGuid():N}";

    public void Dispose() => _sharedCache.Dispose();

    private sealed class GatedConceptRead(ISqlExecutionService inner) : ISqlExecutionService
    {
        private int _captured;
        public TaskCompletionSource Captured { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<IReadOnlyList<T>> ExecuteReaderAsync<T>(int tenantId, SqlCommand command,
            Func<SqlDataReader, T> readRow, CancellationToken cancellationToken,
            SqlCommandIdempotency idempotency = SqlCommandIdempotency.Idempotent)
        {
            var rows = await inner.ExecuteReaderAsync(tenantId, command, readRow, cancellationToken, idempotency);
            if (command.CommandText.Contains("TermConcept tc", StringComparison.Ordinal)
                && Interlocked.Exchange(ref _captured, 1) == 0)
            {
                Captured.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
            return rows;
        }

        public Task<int> ExecuteNonQueryAsync(int tenantId, SqlCommand command, CancellationToken cancellationToken,
            SqlCommandIdempotency idempotency = SqlCommandIdempotency.Idempotent)
            => inner.ExecuteNonQueryAsync(tenantId, command, cancellationToken, idempotency);

        public Task<T> ExecuteInTransactionAsync<T>(int tenantId, Func<ISqlTransactionContext, CancellationToken, Task<T>> work,
            CancellationToken cancellationToken) => inner.ExecuteInTransactionAsync(tenantId, work, cancellationToken);

        public Task ExecuteInTransactionAsync(int tenantId, Func<ISqlTransactionContext, CancellationToken, Task> work,
            CancellationToken cancellationToken) => inner.ExecuteInTransactionAsync(tenantId, work, cancellationToken);
    }
}
