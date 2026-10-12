using System.Data.Common;
using Ignixa.Abstractions;
using Ignixa.Application.Features.Conformance;
using Ignixa.Application.Features.Search;
using Ignixa.Domain.Abstractions;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Application.BackgroundOperations.Import.Activities;

/// <summary>
/// Writes one import batch under the conformance barrier. The first attempt uses the indexes extracted
/// with <c>definitionsHandle</c>; a barrier rejection refreshes definitions, re-extracts every resource
/// against <c>currentDefinitions()</c>, and retries once on a fresh transaction.
/// </summary>
internal static class ImportBatchWriter
{
    public static Task<int> WriteAsync(
        IFhirRepository repository,
        IReadOnlyList<(string resourceType, string resourceId, ResourceJsonNode resource, IReadOnlyList<object> searchIndexes, string httpMethod, int entryIndex)> operations,
        DefinitionsHandle definitionsHandle,
        Func<DefinitionsHandle> currentDefinitions,
        ConformanceBarrierRetryPolicy barrierRetryPolicy,
        CancellationToken cancellationToken)
    {
        var attempt = 0;
        return barrierRetryPolicy.ExecuteAsync(
            async ct =>
            {
                attempt++;
                var attemptHandle = attempt == 1 ? definitionsHandle : currentDefinitions();
                var attemptOperations = attempt == 1
                    ? operations
                    : operations.Select(operation =>
                    {
                        IReadOnlyList<object> indexes = attemptHandle.Indexer.Extract(
                            (IElement)operation.resource.ToElement(attemptHandle.SchemaProvider)).ToArray();
                        return (
                            operation.resourceType,
                            operation.resourceId,
                            operation.resource,
                            searchIndexes: indexes,
                            operation.httpMethod,
                            operation.entryIndex);
                    }).ToList();

                // A file can outgrow a reserved range, and blank lines are not resource offsets.
                var transactionId = await repository.GetNextTransactionIdAsync(attemptHandle.DefinitionsEventId, ct);
                var keys = await repository.BatchWriteAsync(transactionId, attemptOperations, ct);
                try
                {
                    await repository.CommitTransactionAsync(transactionId, CancellationToken.None);
                }
                catch (Exception ex) when (ex is IOException or DbException or TimeoutException or InvalidOperationException or OperationCanceledException)
                {
                    throw new InvalidOperationException(
                        $"Import commit outcome is indeterminate for transaction {transactionId.Value} ({attemptOperations.Count} resources). {ex.Message}",
                        ex);
                }

                return keys.Count;
            },
            cancellationToken);
    }
}
