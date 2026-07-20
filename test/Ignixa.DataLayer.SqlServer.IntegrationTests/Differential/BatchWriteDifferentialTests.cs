using Ignixa.Domain.Models;
using Ignixa.Serialization.SourceNodes;
using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests.Differential;

public class BatchWriteDifferentialTests : IAsyncLifetime
{
    private DifferentialTestHarness _harness = null!;

    public async Task InitializeAsync() => _harness = await DifferentialTestHarness.CreateAsync(CancellationToken.None);
    public async Task DisposeAsync() => await _harness.DisposeAsync();

    [Fact]
    public async Task GivenAFiveResourceBatchWrittenThroughBothRepositories_WhenSnapshottingDboResource_ThenAllFiveRowsAreEquivalent()
    {
        var operations = Enumerable.Range(0, 5)
            .Select(i => ("Patient", $"diff-batch-{i}",
                ResourceJsonNode.Parse($$"""{"resourceType":"Patient","id":"diff-batch-{{i}}"}"""),
                (IReadOnlyList<object>)[], "PUT", i))
            .ToArray();

        var legacyTx = await _harness.LegacyRepository.GetNextTransactionIdAsync(CancellationToken.None);
        await _harness.LegacyRepository.BatchWriteAsync(legacyTx, operations, CancellationToken.None);
        await _harness.LegacyRepository.CommitTransactionAsync(legacyTx, CancellationToken.None);

        var newTx = await _harness.NewRepository.GetNextTransactionIdAsync(CancellationToken.None);
        await _harness.NewRepository.BatchWriteAsync(newTx, operations, CancellationToken.None);
        await _harness.NewRepository.CommitTransactionAsync(newTx, CancellationToken.None);

        var legacySnapshot = await _harness.SnapshotLegacyAsync("dbo.Resource", "ResourceId LIKE 'diff-batch-%'", CancellationToken.None);
        var newSnapshot = await _harness.SnapshotNewAsync("dbo.Resource", "ResourceId LIKE 'diff-batch-%'", CancellationToken.None);

        // RawResource is ignored here for the same reason as SingleResourceCrudDifferentialTests
        // (Task 6): BatchWriteAsync bakes Meta.LastUpdated = transactionId.Value.ToDate() into the
        // compressed JSON before storage, and legacy/new each allocate their own independent
        // TransactionId, so the compressed bytes can never byte-match even for a semantically
        // identical resource.
        _harness.AssertEquivalent(legacySnapshot, newSnapshot, "ResourceSurrogateId", "TransactionId", "HistoryTransactionId", "RawResource");

        // Blanket-ignoring RawResource above throws away comparison of everything else riding along
        // in that column -- AssertResourceContentEquivalent decompresses both sides, strips only
        // meta.lastUpdated, and deep-compares the rest. AssertEquivalent above sorts its OWN local
        // copies of the rows for its column-diff comparison, but never mutates legacySnapshot.Rows /
        // newSnapshot.Rows themselves, so those lists carry no row-order guarantee. Explicitly sort
        // both snapshots' rows here by ResourceId -- distinct per row for these 5 batch-written
        // resources -- so row N on one sorted list genuinely corresponds to row N on the other before
        // indexing into them below.
        legacySnapshot.Rows.Count.ShouldBe(5);
        newSnapshot.Rows.Count.ShouldBe(5);
        var sortedLegacySnapshot = SortByResourceId(legacySnapshot);
        var sortedNewSnapshot = SortByResourceId(newSnapshot);
        for (var rowIndex = 0; rowIndex < 5; rowIndex++)
        {
            var legacyRawResource = DifferentialTestHarness.ExtractRawResourceBytes(sortedLegacySnapshot, rowIndex);
            var newRawResource = DifferentialTestHarness.ExtractRawResourceBytes(sortedNewSnapshot, rowIndex);
            _harness.AssertResourceContentEquivalent(legacyRawResource, newRawResource);
        }
    }

    /// <summary>
    /// Builds a new <see cref="RowStateSnapshot"/> with rows ordered by their <c>ResourceId</c> column
    /// value, ascending. <see cref="RowStateSnapshot.Rows"/> carries no row-order guarantee on its own
    /// (see the comment above), so callers that need row N on one snapshot to correspond to row N on
    /// another must sort both by a column that is actually present and distinct per row in their data
    /// -- here, ResourceId.
    /// </summary>
    private static RowStateSnapshot SortByResourceId(RowStateSnapshot snapshot) => new(
        snapshot.Rows.OrderBy(row => (string)row["ResourceId"]!, StringComparer.Ordinal).ToList(),
        snapshot.TableName);
}
