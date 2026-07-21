using Ignixa.DataLayer.SqlServer.IntegrationTests.Differential;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Serialization.SourceNodes;
using Microsoft.Data.SqlClient;
using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests.Differential;

public class ExpiryAndHardDeleteDifferentialTests : IAsyncLifetime
{
    private DifferentialTestHarness _harness = null!;

    public async Task InitializeAsync() => _harness = await DifferentialTestHarness.CreateAsync(CancellationToken.None);
    public async Task DisposeAsync() => await _harness.DisposeAsync();

    /// <summary>
    /// This test previously ran a live differential comparison of <c>HardDeleteResourceAsync</c>
    /// between the legacy EF-based repository and the new port. That comparison is no longer
    /// possible: legacy's <c>HardDeleteResourceAsync</c> (<c>SqlEntityFrameworkRepository.cs:989-1026</c>)
    /// has a genuine, pre-existing production bug (see the second half of this test) that makes it
    /// throw on every call, for any resource -- confirmed by direct reproduction, not specific to
    /// this test's data. Instead this test proves two things directly: the new port's
    /// <c>HardDeleteResourceAsync</c> genuinely deletes everything it should (part 1), and the
    /// legacy bug is precisely characterized so a future "fix" that changes this failure mode is
    /// caught immediately (part 2), rather than the bug silently existing unnoticed the way it did
    /// before this test suite exercised it for the first time.
    /// </summary>
    [Fact]
    public async Task GivenAHardDeleteOnTheNewPort_WhenComparedAgainstLegacysConfirmedBrokenHardDelete_ThenTheNewPortSucceedsAndLegacyThrowsTheKnownBug()
    {
        // Part 1: the new port's HardDeleteResourceAsync genuinely removes everything -- current
        // version, history, every one of the 15 search-index tables, and the TTL row.
        var resource = new ResourceWrapper("Patient", "diff-hard-delete-1", "1", DateTimeOffset.UtcNow,
            ResourceJsonNode.Parse("""{"resourceType":"Patient","id":"diff-hard-delete-1"}"""), new ResourceRequest("PUT", "Patient/diff-hard-delete-1"))
        {
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1)
        };

        // Two writes so both the current version and a history row exist for this ResourceId.
        await _harness.NewRepository.CreateOrUpdateAsync(resource with { }, CancellationToken.None);
        await _harness.NewRepository.CreateOrUpdateAsync(resource with { }, CancellationToken.None);

        var newTypeId = (await _harness.SnapshotNewAsync("dbo.ResourceType", "Name = 'Patient'", CancellationToken.None))
            .Rows.Single()["ResourceTypeId"];

        // ResourceTypeId is SMALLINT, so the snapshot row's boxed value is Int16 -- unbox directly
        // rather than double-casting through int, which throws InvalidCastException on a boxed Int16.
        await _harness.NewRepository.HardDeleteResourceAsync((short)newTypeId!, "diff-hard-delete-1", CancellationToken.None);

        var newResourceSnapshot = await _harness.SnapshotNewAsync("dbo.Resource", "ResourceId = 'diff-hard-delete-1'", CancellationToken.None);
        newResourceSnapshot.Rows.ShouldBeEmpty("dbo.Resource should have no rows -- current or history -- left for this ResourceId.");

        var newTtlSnapshot = await _harness.SnapshotNewAsync("dbo.ResourceTtl", "ResourceId = 'diff-hard-delete-1'", CancellationToken.None);
        newTtlSnapshot.Rows.ShouldBeEmpty("dbo.ResourceTtl should have no row left for this ResourceId.");

        string[] searchIndexTables =
        [
            "ReferenceSearchParam", "TokenSearchParam", "TokenText", "StringSearchParam", "UriSearchParam",
            "NumberSearchParam", "QuantitySearchParam", "DateTimeSearchParam", "ReferenceTokenCompositeSearchParam",
            "TokenTokenCompositeSearchParam", "TokenDateTimeCompositeSearchParam", "TokenQuantityCompositeSearchParam",
            "TokenStringCompositeSearchParam", "TokenNumberNumberCompositeSearchParam", "ResourceWriteClaim"
        ];

        // This harness's "new" database is isolated per-test (see DifferentialTestHarness.CreateAsync),
        // so "1=1" is equivalent to "rows belonging to this resource" here -- nothing else was ever
        // written to it.
        foreach (var table in searchIndexTables)
        {
            var snapshot = await _harness.SnapshotNewAsync($"dbo.{table}", "1=1", CancellationToken.None);
            snapshot.Rows.ShouldBeEmpty($"dbo.{table} should have no rows left after hard delete.");
        }

        // Part 2: legacy's HardDeleteResourceAsync is genuinely, unconditionally broken -- not
        // specific to this resource's data. Root cause (confirmed by reading
        // SqlEntityFrameworkRepository.cs:989-1026 directly, and reproduced with a real run against
        // this same harness): line 1013 wraps the dynamically-built batch of 15 DELETE statements in
        // FormattableString.Invariant($"{deleteStatements}"), nested inside the outer $@"..." string
        // passed to ExecuteSqlInterpolatedAsync. EF Core's ExecuteSqlInterpolatedAsync ALWAYS
        // parameterizes every {...} interpolation hole -- by design, to prevent SQL injection --
        // regardless of whether the value is itself already a string of raw SQL text.
        // FormattableString.Invariant(...) does not escape this; it just evaluates to a plain
        // string, which still gets bound as one opaque parameter value (@p2) rather than spliced
        // into the SQL command text. The actual SQL sent to the server ends up with a bare parameter
        // reference where 15 DELETE statements should be, which is not valid T-SQL on its own:
        // "Incorrect syntax near '@p2'."
        //
        // This is a genuine, pre-existing production bug in legacy code -- out of scope to fix here
        // per Phase D's design doc (legacy is slated for full retirement, not incremental fixes).
        // It was previously undetected because no test exercised this method before this test suite
        // did. It backs HardDeleteResourceAsync, which the TTL-cleanup background job uses, meaning
        // that job has likely been silently failing whenever it tries to hard-delete an expired
        // resource for as long as this bug has existed.
        //
        // If legacy code is ever changed in a way that fixes this bug, this assertion starts failing
        // loudly -- that is the correct behavior: it forces someone to notice and update this test,
        // rather than the test silently passing either way.
        var legacyResource = new ResourceWrapper("Patient", "diff-hard-delete-legacy-1", "1", DateTimeOffset.UtcNow,
            ResourceJsonNode.Parse("""{"resourceType":"Patient","id":"diff-hard-delete-legacy-1"}"""), new ResourceRequest("PUT", "Patient/diff-hard-delete-legacy-1"));
        await _harness.LegacyRepository.CreateOrUpdateAsync(legacyResource with { }, CancellationToken.None);

        var legacyTypeId = (await _harness.SnapshotLegacyAsync("dbo.ResourceType", "Name = 'Patient'", CancellationToken.None))
            .Rows.Single()["ResourceTypeId"];

        var legacyException = await Should.ThrowAsync<SqlException>(() =>
            _harness.LegacyRepository.HardDeleteResourceAsync((short)legacyTypeId!, "diff-hard-delete-legacy-1", CancellationToken.None));

        legacyException.Message.ShouldContain("@p2");
    }
}
