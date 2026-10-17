// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Output;

namespace Ignixa.CliOnFhir.Commands;

/// <summary>
/// The last line of a verb's timeout. The runners stop at their timeout by cancellation, which they check between steps; one step,
/// a single call of the FHIRPath evaluator, cannot be interrupted. So the CLI runs the work off the main thread and, when it has not
/// finished <see cref="Grace"/> after its timeout, prints the verb's timeout error itself, names the temporary output files, previous
/// targets renamed aside and directories the run may leave (it cannot clean up after a step that will not return), and ends the
/// process with a non-zero exit code. An MCP host cannot
/// end its process for one call; there the runners' cancellation is all there is until the Core evaluator can be cancelled.
/// </summary>
internal static class Watchdog
{
    /// <summary>How long past its timeout a run may go on before the process is ended.</summary>
    public static readonly TimeSpan Grace = TimeSpan.FromSeconds(5);

    /// <summary>Runs <paramref name="run"/>, printing its envelope, or the timeout error when it overruns; returns the exit code.</summary>
    /// <param name="timeoutCode">The verb's timeout error code, such as <c>view-timeout</c>.</param>
    /// <param name="timeoutSeconds">The run's timeout as requested; out-of-range values are the runner's to refuse, and are clamped here.</param>
    public static Task<int> RunAsync<TFacts>(
        string timeoutCode, int? timeoutSeconds, int defaultSeconds, int maxSeconds, Func<CancellationToken, Task<ToolEnvelope<TFacts?>>> run, bool pretty, CancellationToken cancellationToken)
        where TFacts : class =>
        RunAsync(timeoutCode, timeoutSeconds, defaultSeconds, maxSeconds, run, pretty, Grace, Environment.Exit, cancellationToken);

    /// <param name="grace">How long past the timeout to wait; a test passes a short one.</param>
    /// <param name="exit">Ends the process; a test records the code instead.</param>
    internal static async Task<int> RunAsync<TFacts>(
        string timeoutCode,
        int? timeoutSeconds,
        int defaultSeconds,
        int maxSeconds,
        Func<CancellationToken, Task<ToolEnvelope<TFacts?>>> run,
        bool pretty,
        TimeSpan grace,
        Action<int> exit,
        CancellationToken cancellationToken)
        where TFacts : class
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(exit);

        int seconds = Math.Clamp(timeoutSeconds ?? defaultSeconds, 1, maxSeconds);
        Task<ToolEnvelope<TFacts?>> work = Task.Factory.StartNew(() => run(cancellationToken), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();
        if (await Task.WhenAny(work, Task.Delay(TimeSpan.FromSeconds(seconds) + grace, CancellationToken.None)) == work)
        {
            return EnvelopeOutput.Write(await work, pretty);
        }

        EnvelopeOutput.Write(TimedOut<TFacts>(timeoutCode, seconds, grace), pretty);
        await Console.Out.FlushAsync(CancellationToken.None);
        exit(1);
        return 1;
    }

    private static ToolEnvelope<TFacts?> TimedOut<TFacts>(string timeoutCode, int seconds, TimeSpan grace)
        where TFacts : class
    {
        List<IssueItem> issues =
        [
            new IssueItem("error", timeoutCode, $"The run did not finish within {seconds} seconds, and one step of it, an evaluation that cannot be interrupted, was still going {grace.TotalSeconds} seconds later, so the process was ended. Output files are renamed into place only once every resource is done, so none was unless the run had reached that point; one it renamed is complete, and a previous file it was replacing is named below. Narrow the scope, or simplify the expression or map that is slow.", Location: null),
        ];
        issues.AddRange(OpenOutputFiles.TempPaths.Order(StringComparer.Ordinal).Select(path =>
            new IssueItem("information", "out-temp-left", $"The temporary file '{path}' (where it was as of the check) may have been left: the run was ended while it was writing it. Delete it when the directory is safe to use.", Location: null)));
        issues.AddRange(OpenOutputFiles.BackedUp.Order().Select(backup =>
            new IssueItem("information", "out-backup-left", $"The previous '{backup.Target}' may have been left at '{backup.Backup}' (where it was as of the check): the run was ended while it was replacing it. Rename it back if '{backup.Target}' is missing, or delete it.", Location: null)));
        issues.AddRange(OpenOutputFiles.CreatedDirectories.Order(StringComparer.Ordinal).Select(directory =>
            new IssueItem("information", "out-directory-left", $"The directory '{directory}', which this run created for its output, may have been left: the run was ended before it could remove it. Delete it if it is empty and not wanted.", Location: null)));
        return new ToolEnvelope<TFacts?>(null, [], [], [], issues, new FreshnessInfo(IndexBuiltAt: null, StaleFiles: null));
    }
}
