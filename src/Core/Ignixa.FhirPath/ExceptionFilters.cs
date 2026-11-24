// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.FhirPath;

/// <summary>
/// Classifies exceptions for the few <c>catch (Exception)</c> sites that deliberately convert failures into a
/// result (an analysis issue, an unresolved reference).
/// </summary>
internal static class ExceptionFilters
{
    /// <summary>
    /// True when <paramref name="ex"/> says the operation as a whole cannot continue - cancellation, memory
    /// exhaustion, stack exhaustion, or an exhausted evaluation limit - rather than that one step failed. Such an
    /// exception must propagate: recording it as an issue or an empty result would report a partial answer as a
    /// complete one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="Evaluation.FhirPathResourceLimitExceededException"/> is here because the budget it reports
    /// belongs to the whole evaluation: a host <c>resolve()</c> callback that runs FHIRPath of its own and trips
    /// a limit has not failed to find a reference, and turning that into an empty <c>resolve()</c> would let an
    /// expression exhaust the budget and carry on with a wrong answer.
    /// </para>
    /// <para>
    /// An <see cref="AggregateException"/> is searched too, because sync-over-async caller code
    /// (<c>.Result</c> / <c>.Wait()</c>) wraps a cancelled or failed task's exception in one.
    /// </para>
    /// </remarks>
    public static bool IsCritical(Exception ex) =>
        ex is OperationCanceledException or OutOfMemoryException or InsufficientExecutionStackException
            or Evaluation.FhirPathResourceLimitExceededException
        || (ex is AggregateException aggregate && aggregate.Flatten().InnerExceptions.Any(IsCritical));
}
