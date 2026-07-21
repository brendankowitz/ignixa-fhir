using Ignixa.DataLayer.SqlServer.IntegrationTests.Differential;
using Ignixa.Domain.Models;
using Ignixa.Serialization.SourceNodes;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests.Differential;

public class ComprehensiveWorkflowDifferentialTests : IAsyncLifetime
{
    private DifferentialTestHarness _harness = null!;

    public async Task InitializeAsync() => _harness = await DifferentialTestHarness.CreateAsync(CancellationToken.None);
    public async Task DisposeAsync() => await _harness.DisposeAsync();

    [Fact]
    public async Task GivenARealisticCreateUpdateBatchDeleteWorkflowRunOnBothRepositories_WhenSnapshottingEveryAffectedTable_ThenAllRowsAreEquivalent()
    {
        // Exercises CreateOrUpdateAsync, GetNextTransactionIdAsync, BatchWriteAsync,
        // CommitTransactionAsync, and DeleteAsync in one realistic sequence per repository -- proving
        // composition, not exhaustive method coverage. GetAsync/the 3 history methods/
        // GetStalledTransactionsAsync/GetExpiredResourcesAsync each already have dedicated coverage in
        // Tasks 6, 8, and 9 and are deliberately NOT re-exercised here -- this test's value is the
        // multi-method sequence, not a full-surface checklist.
        //
        // HardDeleteResourceAsync is deliberately NOT included in this shared loop: Task 9 confirmed
        // legacy's HardDeleteResourceAsync (SqlEntityFrameworkRepository.cs:989-1026) throws
        // SqlException("Incorrect syntax near '@p2'") unconditionally, on every call, for any
        // resource -- see ExpiryAndHardDeleteDifferentialTests, which already proves the new port's
        // HardDeleteResourceAsync genuinely deletes everything it should AND precisely characterizes
        // legacy's confirmed bug. Running HardDeleteResourceAsync against LegacyRepository inside this
        // shared loop would abort the loop on the legacy iteration and silently reduce this test's
        // coverage of the other 11 methods -- that is exactly the trap this task's brief warned
        // against. Task 9's coverage is sufficient; no additional legacy-side assertion is added here.
        foreach (var isLegacy in new[] { true, false })
        {
            var repository = isLegacy ? _harness.LegacyRepository : _harness.NewRepository;

            var patient = new ResourceWrapper("Patient", "workflow-1", "1", DateTimeOffset.UtcNow,
                ResourceJsonNode.Parse("""{"resourceType":"Patient","id":"workflow-1"}"""), new ResourceRequest("PUT", "Patient/workflow-1"));
            await repository.CreateOrUpdateAsync(patient, CancellationToken.None);
            await repository.CreateOrUpdateAsync(patient with { }, CancellationToken.None);

            var batchTx = await repository.GetNextTransactionIdAsync(CancellationToken.None);
            var batchOps = new (string, string, ResourceJsonNode, IReadOnlyList<object>, string, int)[]
            {
                ("Observation", "workflow-obs-1", ResourceJsonNode.Parse("""{"resourceType":"Observation","id":"workflow-obs-1"}"""), [], "PUT", 0),
            };
            await repository.BatchWriteAsync(batchTx, batchOps, CancellationToken.None);
            await repository.CommitTransactionAsync(batchTx, CancellationToken.None);

            await repository.DeleteAsync(new Ignixa.Abstractions.ResourceKey("Observation", "workflow-obs-1"), new ResourceRequest("DELETE", "Observation/workflow-obs-1"), ct: CancellationToken.None);
        }

        // Also exercises HardDeleteResourceAsync -- but on the new port ONLY, per the reasoning above.
        // Proves it composes safely (no-ops without throwing) against a resource that was never
        // created, immediately after the multi-method sequence above. Resolves ResourceTypeId by
        // querying dbo.ResourceType directly (the "Patient" type row already exists from the loop's
        // writes above) rather than writing another probe resource -- writing one only on the new
        // side would leave dbo.Resource with an extra row on the new side and break the row-count
        // parity the snapshot comparison below depends on.
        var newTypeId = await GetResourceTypeIdAsync(isLegacy: false, "Patient");
        await _harness.NewRepository.HardDeleteResourceAsync(newTypeId, "never-created-but-hard-delete-must-no-op-safely", CancellationToken.None);

        string[] tablesToCompare = ["dbo.Resource", "dbo.Transactions", "dbo.ResourceType"];
        foreach (var table in tablesToCompare)
        {
            var legacySnapshot = await _harness.SnapshotLegacyAsync(table, "1=1", CancellationToken.None);
            var newSnapshot = await _harness.SnapshotNewAsync(table, "1=1", CancellationToken.None);
            // Every ignored column here is a genuinely independently-allocated identifier/timestamp
            // (each database allocates its own sequence values/clock reads) -- none represent a
            // real behavioral divergence. ResourceTypeId specifically: ResourceType rows are
            // IDENTITY-keyed per database, so the same logical type ("Patient") gets a different
            // numeric ID on each side even though the Name column (compared, not ignored) matches.
            // EndDate (dbo.Transactions): confirmed by a real run to differ by ~2 seconds between
            // legacy and new -- CommitTransactionAsync stamps it with each side's own wall-clock read
            // at the moment that side's commit executes, so it can never match across two separately
            // executed sequential calls even against semantically identical data (same reasoning as
            // CreateDate/HeartbeatDate below).
            // RawResource is ignored for the same reason as SingleResourceCrudDifferentialTests/
            // BatchWriteDifferentialTests (Tasks 6/7): CreateOrUpdateAsync/BatchWriteAsync bake
            // Meta.LastUpdated (derived from each side's independently-allocated TransactionId) into
            // the compressed JSON before storage, so the compressed bytes can never byte-match even
            // for semantically identical resources.
            _harness.AssertEquivalent(legacySnapshot, newSnapshot,
                "ResourceSurrogateId", "TransactionId", "HistoryTransactionId",
                "SurrogateIdRangeFirstValue", "SurrogateIdRangeLastValue", "CreateDate", "HeartbeatDate",
                "EndDate", "ResourceTypeId", "RawResource");
        }

        // RawResource is blanket-ignored above -- AssertResourceContentEquivalent decompresses both
        // sides, strips only meta.lastUpdated, and deep-compares the rest, so a real
        // serialization/compression bug still fails loudly instead of riding along inside an ignored
        // column.
        var legacyResourceSnapshot = await _harness.SnapshotLegacyAsync("dbo.Resource", "1=1", CancellationToken.None);
        var newResourceSnapshot = await _harness.SnapshotNewAsync("dbo.Resource", "1=1", CancellationToken.None);
        var sortedLegacyResourceSnapshot = SortByResourceIdThenVersion(legacyResourceSnapshot);
        var sortedNewResourceSnapshot = SortByResourceIdThenVersion(newResourceSnapshot);
        for (var rowIndex = 0; rowIndex < sortedLegacyResourceSnapshot.Rows.Count; rowIndex++)
        {
            var legacyRawResource = DifferentialTestHarness.ExtractRawResourceBytes(sortedLegacyResourceSnapshot, rowIndex);
            var newRawResource = DifferentialTestHarness.ExtractRawResourceBytes(sortedNewResourceSnapshot, rowIndex);
            _harness.AssertResourceContentEquivalent(legacyRawResource, newRawResource);
        }
    }

    /// <summary>
    /// Builds a new <see cref="RowStateSnapshot"/> with rows ordered by (ResourceId, Version), both
    /// present and distinct-per-row across this test's data. See
    /// <c>BatchWriteDifferentialTests.SortByResourceId</c> (Task 7) for the same rationale:
    /// <see cref="RowStateSnapshot.Rows"/> carries no row-order guarantee, so callers pairing up rows
    /// across two snapshots by index must sort both explicitly first.
    /// </summary>
    private static RowStateSnapshot SortByResourceIdThenVersion(RowStateSnapshot snapshot) => new(
        snapshot.Rows
            .OrderBy(row => (string)row["ResourceId"]!, StringComparer.Ordinal)
            .ThenBy(row => Convert.ToInt32(row["Version"]))
            .ToList(),
        snapshot.TableName);

    private async Task<short> GetResourceTypeIdAsync(bool isLegacy, string resourceTypeName)
    {
        // Resolves a real ResourceTypeId by querying dbo.ResourceType directly -- the type row
        // already exists from the loop's earlier writes to this resourceTypeName, so no additional
        // probe write is needed (and none should be added: an extra write on only one side would
        // break the row-count parity later snapshot comparisons depend on).
        var snapshot = isLegacy
            ? await _harness.SnapshotLegacyAsync("dbo.ResourceType", $"Name = '{resourceTypeName}'", CancellationToken.None)
            : await _harness.SnapshotNewAsync("dbo.ResourceType", $"Name = '{resourceTypeName}'", CancellationToken.None);
        // ResourceTypeId is SMALLINT, so the snapshot row's boxed value is Int16 -- unbox directly
        // rather than double-casting through int, which throws InvalidCastException on a boxed Int16
        // (see ExpiryAndHardDeleteDifferentialTests, Task 9, for the same fix).
        return (short)snapshot.Rows.Single()["ResourceTypeId"]!;
    }
}
