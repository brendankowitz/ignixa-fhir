// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using DeltaLake.Errors;

namespace Ignixa.SqlOnFhir.Fabric.Delta;

/// <summary>
/// Recognizes a MERGE that lost an optimistic-concurrency race and can be retried against the new snapshot.
/// </summary>
/// <remarks>
/// The bridge reports delta-rs commit conflicts only as text inside a <see cref="DeltaRuntimeException"/>, so this
/// matches the shared prefix of the delta-rs <c>CommitConflictError</c> messages for concurrent appends, deletes
/// and reads ("Commit failed: a concurrent transaction..."). That is fragile: a reworded upstream message turns
/// retryable conflicts into failures, which fails closed rather than writing wrongly. Metadata, protocol and
/// commit-attempt-limit errors do not match and are not retried; the conflict tests pin the current wording.
/// </remarks>
internal static class MergeConflict
{
    private const string ConflictMarker = "Commit failed: a concurrent transaction";

    public static bool IsRetryable(Exception exception) =>
        exception is DeltaRuntimeException && exception.Message.Contains(ConflictMarker, StringComparison.Ordinal);
}
