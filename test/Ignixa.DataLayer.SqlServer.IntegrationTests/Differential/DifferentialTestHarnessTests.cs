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
        var legacy = new RowStateSnapshot([new Dictionary<string, object?> { ["Id"] = 1 }]);
        var @new = new RowStateSnapshot([]);

        var exception = Should.Throw<ShouldAssertException>(() => _harness.AssertEquivalent(legacy, @new));
        exception.Message.ShouldContain("row count");
    }

    [Fact]
    public void GivenTwoSnapshotsDifferingOnlyInAnIgnoredColumn_WhenAssertEquivalentCalledWithThatColumnIgnored_ThenDoesNotThrow()
    {
        var legacy = new RowStateSnapshot([new Dictionary<string, object?> { ["Id"] = 1, ["ResourceSurrogateId"] = 1000L } ]);
        var @new = new RowStateSnapshot([new Dictionary<string, object?> { ["Id"] = 1, ["ResourceSurrogateId"] = 2000L } ]);

        Should.NotThrow(() => _harness.AssertEquivalent(legacy, @new, "ResourceSurrogateId"));
    }

    [Fact]
    public void GivenTwoSnapshotsWithIdenticalByteArrayColumns_WhenAssertEquivalentCalled_ThenDoesNotThrow()
    {
        // Proves the byte[] normalization (hex-string comparison, not reference equality) works --
        // without it, two snapshots holding equal-content-but-different-instance byte[] values
        // (the real shape RawResource takes) would spuriously fail every comparison.
        var legacy = new RowStateSnapshot([new Dictionary<string, object?> { ["Id"] = 1, ["RawResource"] = new byte[] { 1, 2, 3 } } ]);
        var @new = new RowStateSnapshot([new Dictionary<string, object?> { ["Id"] = 1, ["RawResource"] = new byte[] { 1, 2, 3 } } ]);

        Should.NotThrow(() => _harness.AssertEquivalent(legacy, @new));
    }
}
