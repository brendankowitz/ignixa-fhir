using Ignixa.Abstractions;
using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Ignixa.Domain.Models;
using Ignixa.Serialization.SourceNodes;
using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests;

/// <summary>
/// Composition proof, not a coverage checklist: each of these repository methods already has its own
/// dedicated test (see <see cref="SqlServerFhirRepositoryCrudTests"/> and
/// <see cref="SqlServerFhirRepositoryBatchTests"/>). What is only tested here is that they compose --
/// a create, an update, a batch write under an explicit transaction, a delete, and a hard delete of a
/// resource that never existed, run back to back against one database, leaving exactly the row state
/// this test names.
/// </summary>
public class SqlServerFhirRepositoryWorkflowTests : IAsyncLifetime
{
    private TestTenantDatabase _database = null!;
    private SqlServerFhirRepository _repository = null!;

    public async Task InitializeAsync()
    {
        _database = await TestTenantDatabase.CreateSqlServerFhirRepositoryAsync();
        _repository = _database.Repository;
    }

    public async Task DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task GivenACreateUpdateBatchDeleteSequence_WhenRunEndToEnd_ThenEveryStepsRowStateIsExactlyAsExpected()
    {
        var patient = new ResourceWrapper("Patient", "workflow-1", "1", DateTimeOffset.UtcNow,
            ResourceJsonNode.Parse("""{"resourceType":"Patient","id":"workflow-1"}"""),
            new ResourceRequest("PUT", "Patient/workflow-1"));

        var created = await _repository.CreateOrUpdateAsync(patient, CancellationToken.None);
        created.Key.VersionId.ShouldBe("1");

        var updated = await _repository.CreateOrUpdateAsync(patient with { }, CancellationToken.None);
        updated.Key.VersionId.ShouldBe("2");

        var batchTransactionId = await _repository.GetNextTransactionIdAsync(CancellationToken.None);
        var batchOperations = new (string, string, ResourceJsonNode, IReadOnlyList<object>, string, int)[]
        {
            ("Observation", "workflow-obs-1", ResourceJsonNode.Parse("""{"resourceType":"Observation","id":"workflow-obs-1"}"""), [], "PUT", 0),
        };
        var batchKeys = await _repository.BatchWriteAsync(batchTransactionId, batchOperations, CancellationToken.None);
        await _repository.CommitTransactionAsync(batchTransactionId, CancellationToken.None);
        batchKeys.Select(key => key.VersionId).ShouldBe(["1"]);

        var deletedKey = await _repository.DeleteAsync(
            new ResourceKey("Observation", "workflow-obs-1"),
            new ResourceRequest("DELETE", "Observation/workflow-obs-1"),
            cancellationToken: CancellationToken.None);
        deletedKey!.VersionId.ShouldBe("2");

        // Composes safely against a resource that was never created: a no-op, not a throw, and it must
        // not disturb the rows written above.
        var patientTypeId = await _database.ExecuteScalarAsync<short>("SELECT ResourceTypeId FROM dbo.ResourceType WHERE Name = 'Patient'");
        await _repository.HardDeleteResourceAsync(patientTypeId, "workflow-never-created", CancellationToken.None);

        var patientVersions = await _database.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.Resource WHERE ResourceId = 'workflow-1'");
        patientVersions.ShouldBe(2);
        var currentPatientVersion = await _database.ExecuteScalarAsync<int>(
            "SELECT Version FROM dbo.Resource WHERE ResourceId = 'workflow-1' AND IsHistory = 0");
        currentPatientVersion.ShouldBe(2);

        var observationVersions = await _database.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.Resource WHERE ResourceId = 'workflow-obs-1'");
        observationVersions.ShouldBe(2);
        var tombstoneCount = await _database.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.Resource WHERE ResourceId = 'workflow-obs-1' AND IsHistory = 0 AND IsDeleted = 1 AND Version = 2");
        tombstoneCount.ShouldBe(1);

        var fetchedPatient = await _repository.GetAsync(new ResourceKey("Patient", "workflow-1"), CancellationToken.None);
        fetchedPatient!.VersionId.ShouldBe("2");
        fetchedPatient.IsDeleted.ShouldBeFalse();

        var fetchedObservation = await _repository.GetAsync(new ResourceKey("Observation", "workflow-obs-1"), CancellationToken.None);
        fetchedObservation!.IsDeleted.ShouldBeTrue();

        var resourceTypeNames = await _database.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.ResourceType WHERE Name IN ('Patient', 'Observation')");
        resourceTypeNames.ShouldBe(2);
    }
}
