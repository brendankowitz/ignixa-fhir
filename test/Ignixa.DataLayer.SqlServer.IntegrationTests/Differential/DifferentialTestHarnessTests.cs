using Ignixa.DataLayer.SqlServer.IntegrationTests.Differential;
using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests.Differential;

public class DifferentialTestHarnessTests : IAsyncLifetime
{
    private DifferentialTestHarness _harness = null!;

    public async Task InitializeAsync() => _harness = await DifferentialTestHarness.CreateAsync(CancellationToken.None);
    public async Task DisposeAsync() => await _harness.DisposeAsync();

    [Fact]
    public async Task GivenTwoFreshlyDeployedDatabases_WhenSnapshottingDboResourceType_ThenBothAreIdentical()
    {
        // dbo.ResourceType is seeded identically on both sides -- NOT by the dacpac's
        // post-deployment script (Task 2's implementer found the real DDL has zero ResourceType
        // seed data; it's normally populated on-demand by the write path), but because BOTH
        // databases provision through the same TestTenantDatabase.CreateEmptyAsync() (Task 2),
        // which seeds one "Patient" row to unblock its own cache tests. Since Task 5's harness
        // provisions both LegacyRepository's and NewRepository's databases through that same
        // factory, the seed is symmetric -- a genuine zero-diff baseline proving the harness
        // itself works before any real resource data is written by either repository.
        var legacy = await _harness.SnapshotLegacyAsync("dbo.ResourceType", "1=1", CancellationToken.None);
        var @new = await _harness.SnapshotNewAsync("dbo.ResourceType", "1=1", CancellationToken.None);

        Should.NotThrow(() => _harness.AssertEquivalent(legacy, @new));
    }

    [Fact]
    public async Task GivenARealDivergenceInsertedDirectlyIntoOnlyOneDatabase_WhenSnapshottingBothAndAsserting_ThenAssertEquivalentThrows()
    {
        // The canary this harness was missing before this fix: proves AssertEquivalent actually
        // DETECTS a real difference, not just that it doesn't false-positive on identical data.
        // Without this test, a broken SnapshotLegacyAsync/SnapshotNewAsync split (e.g. both
        // accidentally reading the same physical database) would still pass every other test here.
        await _harness.InsertIntoNewDatabaseOnlyForTestingAsync(
            "INSERT INTO dbo.ResourceType (Name) VALUES ('CanaryOnlyOnNewSide')", CancellationToken.None);

        var legacy = await _harness.SnapshotLegacyAsync("dbo.ResourceType", "Name = 'CanaryOnlyOnNewSide'", CancellationToken.None);
        var @new = await _harness.SnapshotNewAsync("dbo.ResourceType", "Name = 'CanaryOnlyOnNewSide'", CancellationToken.None);

        Should.Throw<ShouldAssertException>(() => _harness.AssertEquivalent(legacy, @new));
    }

    [Fact]
    public void GivenTwoSnapshotsWithDifferingRowCounts_WhenAssertEquivalentCalled_ThenThrowsWithACountMismatchMessage()
    {
        var legacy = new RowStateSnapshot([new Dictionary<string, object?> { ["Id"] = 1 }], "dbo.TestTable");
        var @new = new RowStateSnapshot([], "dbo.TestTable");

        var exception = Should.Throw<ShouldAssertException>(() => _harness.AssertEquivalent(legacy, @new));
        exception.Message.ShouldContain("row count");
        exception.Message.ShouldContain("dbo.TestTable");
    }

    [Fact]
    public void GivenTwoSnapshotsDifferingOnlyInAnIgnoredColumn_WhenAssertEquivalentCalledWithThatColumnIgnored_ThenDoesNotThrow()
    {
        var legacy = new RowStateSnapshot([new Dictionary<string, object?> { ["Id"] = 1, ["ResourceSurrogateId"] = 1000L } ], "dbo.TestTable");
        var @new = new RowStateSnapshot([new Dictionary<string, object?> { ["Id"] = 1, ["ResourceSurrogateId"] = 2000L } ], "dbo.TestTable");

        Should.NotThrow(() => _harness.AssertEquivalent(legacy, @new, "ResourceSurrogateId"));
    }

    [Fact]
    public void GivenMultiRowSnapshotsWhereIgnoredColumnNameSortsBeforeDiscriminatingColumn_WhenAssertEquivalentCalledWithThatColumnIgnored_ThenRowsPairCorrectlyAndDoesNotThrow()
    {
        // Proves Finding 1's fix: BuildSortKey must exclude ignored columns from the sort key.
        //
        // BuildSortKey orders columns ALPHABETICALLY BY NAME before concatenating them into the
        // composite sort key, and StringComparer.Ordinal resolves a comparison on the first
        // differing character -- so whichever column's name sorts first alphabetically dominates
        // the key. Naming the ignored column "ResourceSurrogateId" (R) against a discriminating
        // "Code" (C) -- as an earlier version of this test did -- puts "Code=" first in the key
        // and "Code" alone decides sort order under BOTH the buggy and fixed algorithms, so that
        // choice can never actually exercise the fix. This test instead names the ignored column
        // "AaaSurrogateId" (A), which sorts BEFORE "Code" (C), so it genuinely dominates the sort
        // key whenever it participates in it.
        //
        // Both rows are identical on the only non-ignored column ("Code"), but each side's
        // AaaSurrogateId is independently allocated and, crucially, ordered OPPOSITELY relative to
        // Code between legacy and new (legacy: A/1000 then B/2000; new: B/1000 then A/9000). Under
        // the OLD (buggy) algorithm, which still hashes AaaSurrogateId into the sort key, legacy
        // sorts as [A/1000, B/2000] (key dominated by "1000" < "2000") while new sorts as
        // [B/1000, A/9000] (key dominated by "1000" < "9000") -- pairing row 0 of each side
        // ("A" vs "B") and producing a spurious Code mismatch despite the data being equivalent on
        // every non-ignored column. Under the FIXED algorithm, which excludes AaaSurrogateId from
        // the key, both sides sort purely by Code ([A, B] on both sides), pairing correctly and
        // producing no mismatch.
        var legacy = new RowStateSnapshot(
            [
                new Dictionary<string, object?> { ["Code"] = "A", ["AaaSurrogateId"] = 1000L },
                new Dictionary<string, object?> { ["Code"] = "B", ["AaaSurrogateId"] = 2000L },
            ],
            "dbo.TestTable");
        var @new = new RowStateSnapshot(
            [
                new Dictionary<string, object?> { ["Code"] = "B", ["AaaSurrogateId"] = 1000L },
                new Dictionary<string, object?> { ["Code"] = "A", ["AaaSurrogateId"] = 9000L },
            ],
            "dbo.TestTable");

        Should.NotThrow(() => _harness.AssertEquivalent(legacy, @new, "AaaSurrogateId"));
    }

    [Fact]
    public void GivenAColumnValueMismatch_WhenAssertEquivalentCalled_ThenThrowsWithMessageContainingTheTableName()
    {
        var legacy = new RowStateSnapshot([new Dictionary<string, object?> { ["Id"] = 1, ["Code"] = "A" }], "dbo.TestTable");
        var @new = new RowStateSnapshot([new Dictionary<string, object?> { ["Id"] = 1, ["Code"] = "B" }], "dbo.TestTable");

        var exception = Should.Throw<ShouldAssertException>(() => _harness.AssertEquivalent(legacy, @new));
        exception.Message.ShouldContain("dbo.TestTable");
    }

    [Fact]
    public void GivenTwoSnapshotsWithIdenticalByteArrayColumns_WhenAssertEquivalentCalled_ThenDoesNotThrow()
    {
        // Proves the byte[] normalization (hex-string comparison, not reference equality) works --
        // without it, two snapshots holding equal-content-but-different-instance byte[] values
        // (the real shape RawResource takes) would spuriously fail every comparison.
        var legacy = new RowStateSnapshot([new Dictionary<string, object?> { ["Id"] = 1, ["RawResource"] = new byte[] { 1, 2, 3 } } ], "dbo.TestTable");
        var @new = new RowStateSnapshot([new Dictionary<string, object?> { ["Id"] = 1, ["RawResource"] = new byte[] { 1, 2, 3 } } ], "dbo.TestTable");

        Should.NotThrow(() => _harness.AssertEquivalent(legacy, @new));
    }
}
