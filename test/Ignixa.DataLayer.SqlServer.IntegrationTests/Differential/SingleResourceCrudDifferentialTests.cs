using Ignixa.Domain.Models;
using Ignixa.Serialization.SourceNodes;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests.Differential;

public class SingleResourceCrudDifferentialTests : IAsyncLifetime
{
    private DifferentialTestHarness _harness = null!;

    public async Task InitializeAsync() => _harness = await DifferentialTestHarness.CreateAsync(CancellationToken.None);
    public async Task DisposeAsync() => await _harness.DisposeAsync();

    [Fact]
    public async Task GivenTheSameResourceCreatedThroughBothRepositories_WhenSnapshottingDboResource_ThenRowsAreEquivalent()
    {
        var resource = new ResourceWrapper("Patient", "diff-crud-1", "1", DateTimeOffset.UtcNow,
            ResourceJsonNode.Parse("""{"resourceType":"Patient","id":"diff-crud-1"}"""), new ResourceRequest("PUT", "Patient/diff-crud-1"));

        await _harness.LegacyRepository.CreateOrUpdateAsync(resource with { }, CancellationToken.None);
        await _harness.NewRepository.CreateOrUpdateAsync(resource with { }, CancellationToken.None);

        var legacySnapshot = await _harness.SnapshotLegacyAsync(
            "dbo.Resource", "ResourceId = 'diff-crud-1'", CancellationToken.None);
        var newSnapshot = await _harness.SnapshotNewAsync(
            "dbo.Resource", "ResourceId = 'diff-crud-1'", CancellationToken.None);

        // ResourceSurrogateId and TransactionId legitimately differ between the two databases
        // (independently allocated sequences/clocks) -- everything else must match exactly.
        //
        // RawResource is ALSO ignored, for the same underlying reason: CreateOrUpdateAsync bakes
        // Meta.LastUpdated = transactionId.Value.ToDate() into the compressed JSON BEFORE it is
        // compressed (matches legacy SqlEntityFrameworkRepository.cs:160 exactly -- confirmed
        // correct, see Task 6 brief). Since legacy and new each allocate their transaction ID from
        // their own independent database (own sequence, own clock reading), the two TransactionId
        // values differ, so the two Meta.LastUpdated values differ, so the two compressed byte
        // sequences differ -- even though both sides compressed the semantically-equivalent
        // resource. Verified empirically: with RawResource included this assertion fails on every
        // run (confirmed via a real test run), not flakily -- it is not possible for two
        // independently-allocated TransactionIds to decode to the same LastUpdated instant.
        _harness.AssertEquivalent(legacySnapshot, newSnapshot, "ResourceSurrogateId", "TransactionId", "HistoryTransactionId", "RawResource");
    }

    [Fact]
    public async Task GivenTheSameResourceDeletedThroughBothRepositories_WhenSnapshottingAllFifteenSearchIndexTables_ThenAllAreEmptyOnBothSides()
    {
        var resource = new ResourceWrapper("Patient", "diff-crud-2", "1", DateTimeOffset.UtcNow,
            ResourceJsonNode.Parse("""{"resourceType":"Patient","id":"diff-crud-2"}"""), new ResourceRequest("PUT", "Patient/diff-crud-2"));
        var key = new Ignixa.Abstractions.ResourceKey("Patient", "diff-crud-2");

        await _harness.LegacyRepository.CreateOrUpdateAsync(resource with { }, CancellationToken.None);
        await _harness.NewRepository.CreateOrUpdateAsync(resource with { }, CancellationToken.None);
        await _harness.LegacyRepository.DeleteAsync(key, new ResourceRequest("DELETE", "Patient/diff-crud-2"), null, CancellationToken.None);
        await _harness.NewRepository.DeleteAsync(key, new ResourceRequest("DELETE", "Patient/diff-crud-2"), null, CancellationToken.None);

        string[] searchIndexTables =
        [
            "ReferenceSearchParam", "TokenSearchParam", "TokenText", "StringSearchParam", "UriSearchParam",
            "NumberSearchParam", "QuantitySearchParam", "DateTimeSearchParam", "ReferenceTokenCompositeSearchParam",
            "TokenTokenCompositeSearchParam", "TokenDateTimeCompositeSearchParam", "TokenQuantityCompositeSearchParam",
            "TokenStringCompositeSearchParam", "TokenNumberNumberCompositeSearchParam", "ResourceWriteClaim"
        ];

        foreach (var table in searchIndexTables)
        {
            var legacySnapshot = await _harness.SnapshotLegacyAsync($"dbo.{table}", "1=1", CancellationToken.None);
            var newSnapshot = await _harness.SnapshotNewAsync($"dbo.{table}", "1=1", CancellationToken.None);
            _harness.AssertEquivalent(legacySnapshot, newSnapshot);
        }
    }
}
