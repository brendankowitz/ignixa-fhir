using Ignixa.Abstractions;
using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Exceptions;
using Ignixa.Domain.Models;
using Ignixa.Serialization.SourceNodes;
using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests;

public class AtomicResourceWriteTests : IAsyncLifetime
{
    private TestTenantDatabase _database = null!;

    public async Task InitializeAsync() =>
        _database = await TestTenantDatabase.CreateSqlServerFhirRepositoryAsync();

    public Task DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task GivenCancelledTransaction_WhenStorageIsCalled_ThenNothingIsAllocatedOrWritten()
    {
        var allocationsBefore = await _database.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.Transactions");
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        IAtomicFhirRepository repository = _database.Repository;

        await Should.ThrowAsync<OperationCanceledException>(() =>
            repository.WriteTransactionAsync([Patient("cancelled", "0")], cancellation.Token));

        (await _database.Repository.GetAsync(new ResourceKey("Patient", "cancelled"))).ShouldBeNull();
        (await _database.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.Transactions")).ShouldBe(allocationsBefore);
    }

    [Fact]
    public async Task GivenTwoTransactionsWithOneExpectedVersion_WhenCommittedConcurrently_ThenOnlyOneSucceeds()
    {
        await _database.Repository.CreateOrUpdateAsync(Patient("contended", null));
        IAtomicFhirRepository repository = _database.Repository;
        async Task<bool> CommitAsync()
        {
            try
            {
                await repository.WriteTransactionAsync([Patient("contended", "1")], CancellationToken.None);
                return true;
            }
            catch (PreconditionFailedException)
            {
                return false;
            }
        }

        var results = await Task.WhenAll(CommitAsync(), CommitAsync());

        results.Count(success => success).ShouldBe(1);
        (await _database.Repository.GetAsync(new ResourceKey("Patient", "contended")))!.VersionId.ShouldBe("2");
        (await _database.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.Resource WHERE ResourceId = 'contended'")).ShouldBe(2);
    }

    [Fact]
    public async Task GivenASqlFailureDuringCoreInsert_WhenTransactionWritten_ThenAllResourcesRollBack()
    {
        await _database.ExecuteNonQueryAsync("""
            CREATE TRIGGER dbo.RejectAtomicResourceInsert ON dbo.Resource AFTER INSERT AS
            BEGIN
              IF EXISTS (SELECT 1 FROM inserted WHERE ResourceId = 'rejected')
                THROW 51000, 'Atomic resource rollback probe', 1;
            END
            """);
        IAtomicFhirRepository repository = _database.Repository;

        await Should.ThrowAsync<Microsoft.Data.SqlClient.SqlException>(() =>
            repository.WriteTransactionAsync([Patient("first", "0"), Patient("rejected", "0")], CancellationToken.None));

        (await _database.Repository.GetAsync(new ResourceKey("Patient", "first"))).ShouldBeNull();
        (await _database.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.Resource")).ShouldBe(0);
    }

    [Fact]
    public async Task GivenAnExistingResource_WhenReadingUnsupportedVersionSyntax_ThenItDoesNotReturnLatest()
    {
        await _database.Repository.CreateOrUpdateAsync(Patient("versioned", null));

        await Should.ThrowAsync<BadRequestException>(async () =>
            await _database.Repository.GetAsync(new ResourceKey("Patient", "versioned", "invalid_version")));
    }

    [Theory]
    [InlineData("6ba7b810-9dad-11d1-80b4-00c04fd430c8")]
    [InlineData("release-A")]
    [InlineData("01")]
    [InlineData("2147483648")]
    public async Task GivenALegalUnknownVersion_WhenReadingStorage_ThenReturnsNullWithoutReadingLatest(string version)
    {
        await _database.Repository.CreateOrUpdateAsync(Patient("opaque-version", null));

        (await _database.Repository.GetAsync(new ResourceKey("Patient", "opaque-version", version))).ShouldBeNull();
    }

    [Fact]
    public async Task GivenAWrongExpectedVersionAtTheMergeBoundary_WhenTheProposedVersionIsHigher_ThenSqlRejectsWithoutMutation()
    {
        await _database.Repository.CreateOrUpdateAsync(Patient("sql-precondition", null));
        var proposed = Patient("sql-precondition", "2");
        proposed.Resource.Meta.VersionId = "3";
        var (transactionId, _) = await _database.MergeRepository.BeginTransactionAsync(1);

        await Should.ThrowAsync<PreconditionFailedException>(() =>
            _database.MergeRepository.MergeResourcesAsync(transactionId, true, [proposed], [0]));

        (await _database.Repository.GetAsync(new ResourceKey("Patient", "sql-precondition")))!.VersionId.ShouldBe("1");
        (await _database.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.Resource WHERE ResourceId = 'sql-precondition'")).ShouldBe(1);
    }

    private static ResourceWrapper Patient(string id, string? expected) => new(
        "Patient", id, "1", DateTimeOffset.UtcNow,
        ResourceJsonNode.Parse($$"""{"resourceType":"Patient","id":"{{id}}"}"""),
        new ResourceRequest("PUT", $"Patient/{id}"))
    {
        ExpectedVersionId = expected,
        TenantId = 1
    };
}
