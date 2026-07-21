using Ignixa.DataLayer.SqlServer.IntegrationTests.Differential;
using Ignixa.Domain.Exceptions;
using Ignixa.Domain.Models;
using Ignixa.Serialization.SourceNodes;
using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests.Differential;

public class ErrorHandlingParityDifferentialTests : IAsyncLifetime
{
    private DifferentialTestHarness _harness = null!;

    public async Task InitializeAsync() => _harness = await DifferentialTestHarness.CreateAsync(CancellationToken.None);
    public async Task DisposeAsync() => await _harness.DisposeAsync();

    [Fact]
    public async Task GivenTheSameVersionMergedTwiceDirectlyThroughTheMergeRepository_WhenBothRepositoriesAreExercised_ThenBothThrowPreconditionFailedException()
    {
        // An earlier draft raced two concurrent CreateOrUpdateAsync calls hoping one would lose and
        // hit SQL error 50409 -- nondeterministic, since if the two calls happen to serialize (very
        // possible against a fast local SQL Server instance) both succeed and the test fails
        // spuriously. This version forces the SQL-level condition directly and deterministically:
        // call MergeResourcesAsync twice with the SAME explicit target version, bypassing
        // IFhirRepository's own client-side "read current version first" logic entirely, so the
        // conflict is guaranteed to occur inside the stored procedure itself -- proving
        // SqlMergeRepository's/SqlServerMergeRepository's catch (SqlException ex) when
        // (ex.Number == 50409) mapping is present and identical on both implementations.
        var legacyTx = await _harness.LegacyRepository.GetNextTransactionIdAsync(CancellationToken.None);
        var legacyResource = new ResourceWrapper("Patient", "diff-conflict-1", "1", DateTimeOffset.UtcNow,
            ResourceJsonNode.Parse("""{"resourceType":"Patient","id":"diff-conflict-1"}"""), new ResourceRequest("PUT", "Patient/diff-conflict-1"));
        await _harness.LegacyMergeRepository.MergeResourcesAsync(
            legacyTx.Value, singleTransaction: true, [legacyResource], [0], CancellationToken.None);

        var newTx = await _harness.NewRepository.GetNextTransactionIdAsync(CancellationToken.None);
        var newResource = new ResourceWrapper("Patient", "diff-conflict-1", "1", DateTimeOffset.UtcNow,
            ResourceJsonNode.Parse("""{"resourceType":"Patient","id":"diff-conflict-1"}"""), new ResourceRequest("PUT", "Patient/diff-conflict-1"));
        await _harness.NewMergeRepository.MergeResourcesAsync(
            newTx.Value, singleTransaction: true, [newResource], [0], CancellationToken.None);

        // Second merge, SAME explicit version ("1") for the SAME (ResourceTypeId, ResourceId) --
        // MergeResources itself (not any client-side pre-check) must detect and reject this.
        var legacyException = await Should.ThrowAsync<Exception>(() =>
            _harness.LegacyMergeRepository.MergeResourcesAsync(
                legacyTx.Value, singleTransaction: true, [legacyResource with { }], [1], CancellationToken.None));
        var newException = await Should.ThrowAsync<Exception>(() =>
            _harness.NewMergeRepository.MergeResourcesAsync(
                newTx.Value, singleTransaction: true, [newResource with { }], [1], CancellationToken.None));

        legacyException.ShouldBeOfType<PreconditionFailedException>();
        newException.ShouldBeOfType<PreconditionFailedException>();
    }

    [Fact]
    public async Task GivenABatchWriteThatCollidesOnTheSameTransactionSurrogateSlotTwice_WhenCalledOnBothRepositories_ThenBothThrowResourceVersionConflictExceptionWithTheSameMessageTemplate()
    {
        // Corrected during implementation: the brief's original draft expected a stale-version
        // BatchWriteAsync call (reusing an earlier, already-superseded TransactionId) to throw
        // InvalidOperationException with a "Version constraint violation" message. Reading the real
        // source (SqlServerFhirRepository.BatchWriteAsync and its legacy twin
        // SqlEntityFrameworkRepository.BatchWriteAsync) shows that check can never actually fire
        // through the public BatchWriteAsync contract: `newVersion` is always computed as
        // `existing.MaxVersion + 1` from the SAME freshly-fetched snapshot the guard compares it
        // against, so `newVersion <= existing.MaxVersion` is always false -- it is dead code on both
        // implementations, not a reachable error path. The real, deterministic client-side conflict
        // both implementations DO throw is the surrogate-id guard just below it
        // (`newSurrogateId <= existing.MaxSurrogateId`), reached by reusing the exact same
        // TransactionId and entryIndex for the exact same resource twice in a row: the second
        // BatchWriteAsync call computes a surrogate ID equal to (not less than, but the guard uses
        // <=) the one the first call already persisted. Both repositories throw
        // ResourceVersionConflictException from this guard -- confirmed by an actual run against real
        // SQL Server.
        //
        // Message text, NOT literal equality: a first attempt asserted the raw messages were
        // byte-for-byte identical, on the theory that identical call sequences against two freshly
        // provisioned, empty databases would allocate identical TransactionId values. A real run
        // disproved that: legacy and new consistently differed by a constant offset (e.g.
        // ...82161000 vs ...82081000), because each database's Transactions IDENTITY sequence
        // independently absorbs its own schema/setup overhead before this test's own calls begin --
        // there is no lockstep guarantee. The embedded SurrogateId digits are therefore genuinely
        // divergent, expected values (like ResourceSurrogateId/TransactionId elsewhere in this
        // harness), not a real behavioral difference. This asserts the exception TYPE, the FHIR
        // StatusCode mapping, and the message TEMPLATE (digits normalized out) match -- proving
        // identical error-handling behavior without depending on cross-database identity-sequence
        // alignment that does not exist.
        var resource = new ResourceWrapper("Patient", "diff-conflict-2", "1", DateTimeOffset.UtcNow,
            ResourceJsonNode.Parse("""{"resourceType":"Patient","id":"diff-conflict-2"}"""), new ResourceRequest("PUT", "Patient/diff-conflict-2"));
        await _harness.LegacyRepository.CreateOrUpdateAsync(resource with { }, CancellationToken.None);
        await _harness.NewRepository.CreateOrUpdateAsync(resource with { }, CancellationToken.None);

        var legacyTx = await _harness.LegacyRepository.GetNextTransactionIdAsync(CancellationToken.None);
        var newTx = await _harness.NewRepository.GetNextTransactionIdAsync(CancellationToken.None);

        var legacyOperations = new (string, string, ResourceJsonNode, IReadOnlyList<object>, string, int)[]
        {
            ("Patient", "diff-conflict-2", ResourceJsonNode.Parse("""{"resourceType":"Patient","id":"diff-conflict-2"}"""), [], "PUT", 0),
        };
        var newOperations = new (string, string, ResourceJsonNode, IReadOnlyList<object>, string, int)[]
        {
            ("Patient", "diff-conflict-2", ResourceJsonNode.Parse("""{"resourceType":"Patient","id":"diff-conflict-2"}"""), [], "PUT", 0),
        };

        // First call for each repository succeeds and persists a resource whose surrogate ID equals
        // (TransactionId + entryIndex 0).
        await _harness.LegacyRepository.BatchWriteAsync(legacyTx, legacyOperations, CancellationToken.None);
        await _harness.NewRepository.BatchWriteAsync(newTx, newOperations, CancellationToken.None);

        // Second call reuses the SAME TransactionId and the SAME entryIndex (0) for the SAME
        // resource -- the computed surrogate ID collides exactly with the one just persisted.
        var legacyStaleOperations = new (string, string, ResourceJsonNode, IReadOnlyList<object>, string, int)[]
        {
            ("Patient", "diff-conflict-2", ResourceJsonNode.Parse("""{"resourceType":"Patient","id":"diff-conflict-2"}"""), [], "PUT", 0),
        };
        var newStaleOperations = new (string, string, ResourceJsonNode, IReadOnlyList<object>, string, int)[]
        {
            ("Patient", "diff-conflict-2", ResourceJsonNode.Parse("""{"resourceType":"Patient","id":"diff-conflict-2"}"""), [], "PUT", 0),
        };

        var legacyException = await Should.ThrowAsync<ResourceVersionConflictException>(() =>
            _harness.LegacyRepository.BatchWriteAsync(legacyTx, legacyStaleOperations, CancellationToken.None));
        var newException = await Should.ThrowAsync<ResourceVersionConflictException>(() =>
            _harness.NewRepository.BatchWriteAsync(newTx, newStaleOperations, CancellationToken.None));

        legacyException.StatusCode.ShouldBe(409);
        newException.StatusCode.ShouldBe(409);
        legacyException.ResourceType.ShouldBe(newException.ResourceType);
        legacyException.ResourceId.ShouldBe(newException.ResourceId);
        NormalizeDigits(legacyException.Message).ShouldBe(NormalizeDigits(newException.Message));
    }

    /// <summary>
    /// Replaces every run of digits with a single '#' placeholder -- used to compare
    /// <see cref="ResourceVersionConflictException"/> messages by template rather than by literal
    /// value, since the embedded SurrogateId digits are genuinely, independently allocated per
    /// database (see the comment above).
    /// </summary>
    private static string NormalizeDigits(string message) =>
        System.Text.RegularExpressions.Regex.Replace(message, "[0-9]+", "#");
}
