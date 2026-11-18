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
    /// exhaustion or stack exhaustion - rather than that one step failed. Such an exception must propagate:
    /// recording it as an issue or an empty result would report a partial answer as a complete one.
    /// </summary>
    /// <remarks>
    /// An <see cref="AggregateException"/> is searched too, because sync-over-async caller code
    /// (<c>.Result</c> / <c>.Wait()</c>) wraps a cancelled or failed task's exception in one.
    /// </remarks>
    public static bool IsCritical(Exception ex) =>
        ex is OperationCanceledException or OutOfMemoryException or InsufficientExecutionStackException
        || (ex is AggregateException aggregate && aggregate.Flatten().InnerExceptions.Any(IsCritical));
}
