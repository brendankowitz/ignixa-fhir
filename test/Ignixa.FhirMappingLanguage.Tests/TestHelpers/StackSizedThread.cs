/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Runs test work on a thread with a chosen stack size.
 */

using System.Runtime.ExceptionServices;

namespace Ignixa.FhirMappingLanguage.Tests.TestHelpers;

/// <summary>
/// Runs work on a dedicated thread with an explicit stack size, so stack-depth tests do not depend on
/// whichever thread the test runner provides.
/// </summary>
internal static class StackSizedThread
{
    /// <summary>
    /// Runs <paramref name="work"/> on a new thread with <paramref name="maxStackSize"/> bytes of stack and
    /// rethrows any exception it raised on the calling thread.
    /// </summary>
    public static T Run<T>(Func<T> work, int maxStackSize)
    {
        T? result = default;
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(
            () =>
            {
                try
                {
                    result = work();
                }
                catch (Exception ex)
                {
                    // Re-thrown on the calling thread below; an exception escaping a raw thread kills the host.
                    failure = ExceptionDispatchInfo.Capture(ex);
                }
            },
            maxStackSize);
        thread.Start();
        thread.Join();
        failure?.Throw();
        return result!;
    }
}
