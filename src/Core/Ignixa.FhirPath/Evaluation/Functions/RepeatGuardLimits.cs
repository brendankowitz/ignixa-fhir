/*
 * Copyright (c) 2025, Ignixa Contributors
 */

namespace Ignixa.FhirPath.Evaluation.Functions;

/// <summary>
/// The guard thresholds <see cref="CollectionFunctions.Repeat"/> checks against, and a test-only seam
/// (#435) to substitute smaller ones for the duration of one call.
/// </summary>
/// <remarks>
/// <para>
/// Both thresholds are read-only properties over a <see langword="private"/> <see cref="AsyncLocal{T}"/>
/// override that falls back to the production constant, so the only way to change either is
/// <see cref="Scope"/>. That narrows the seam; it does not close it. <see cref="Scope"/>'s constructor is
/// <see langword="internal"/> and <c>InternalsVisibleTo</c> on this assembly names four assemblies, not
/// one - <c>Ignixa.SqlOnFhir</c> and <c>Ignixa.Search</c> as well as <c>Ignixa.FhirPath.Tests</c> and
/// <c>Ignixa.RepoGuards.Tests</c> - so both <em>production</em> assemblies can construct one. Removing
/// the settable properties renamed the mutator rather than removing it, and "production code always runs
/// against the real values" is a convention here, not a property of the type.
/// </para>
/// <para>
/// What removing them did buy is unconditional and worth stating exactly: every mutation is now
/// <em>scoped and self-restoring</em>. No assembly can lower a threshold and leave it lowered, so the
/// worst a misuse can do is confine itself to one <c>using</c> block. The remaining convention - that no
/// production code opens such a block at all - is backed by
/// <c>Ignixa.RepoGuards.Tests.RepeatGuardLimitsSeamGuardTests</c>, which fails if any file under
/// <c>src/</c> other than this one names <see cref="Scope"/> outside a doc comment. That is the only
/// enforcement <c>InternalsVisibleTo</c> granularity permits, and it is a failing test rather than a
/// comment.
/// </para>
/// <para>
/// <b>Why <see cref="AsyncLocal{T}"/> and not a plain static (#435 review):</b> a process-wide static
/// bleeds across concurrently-running tests. <c>Ignixa.FhirPath.Tests</c> has no <c>xunit.runner.json</c>
/// and no <c>[assembly: CollectionBehavior]</c>, so xUnit's default applies and test <em>classes</em> run
/// in parallel; several classes other than this seam's callers evaluate <c>repeat()</c> at scales that need
/// the production budget. A static field version of this seam was demonstrated to fail such a class -
/// a scope-holding class in one test class reduced <see cref="MaxComparisons"/> to 50 while another class
/// evaluating <c>repeat(item)</c> over a 363-item tree threw the 50-comparison budget. An
/// <see cref="AsyncLocal{T}"/> override is confined to the execution context that set it, so a scope
/// cannot be observed by a test it did not enclose, and no test class has to be remembered and added to a
/// shared xUnit collection for that to stay true.
/// </para>
/// <para>
/// <b>Why this seam exists (#435):</b> before it, the tests proving <c>Repeat</c>'s guards actually trip had
/// no way to drive them at anything but the real 10,000/comparison-budget scale, so proving the iteration
/// guard fires cost its own real-world wall-clock time - 33 seconds, by construction - on every CI run, on
/// every target framework, forever. A small cap proves the identical throw-and-log-tier behaviour in
/// milliseconds; this type is what lets a test substitute one.
/// </para>
/// <para>
/// <b>Cost.</b> An <see cref="AsyncLocal{T}"/> read is not free the way a static field read is, and
/// <c>ContainsElement</c> consults <see cref="MaxComparisons"/> up to fifteen million times per call.
/// <see cref="CollectionFunctions.Repeat"/> therefore hoists both properties into locals once at entry -
/// which is sound because that method is eager, so a threshold cannot legitimately change mid-call.
/// </para>
/// </remarks>
internal static class RepeatGuardLimits
{
    /// <summary>
    /// The real production iteration cap. See <see cref="CollectionFunctions.Repeat"/>'s remarks for why
    /// 10,000 is a data-headroom figure, chosen against a measured control case, not an arbitrary round number.
    /// </summary>
    private const int ProductionMaxIterations = 10_000;

    /// <summary>
    /// The real production comparison-count budget. See <see cref="CollectionFunctions.Repeat"/>'s remarks
    /// for the measurement this value is based on and why it is a cost figure rather than a data-headroom one.
    /// </summary>
    private const long ProductionMaxComparisons = 15_000_000;

    private static readonly AsyncLocal<int?> _maxIterationsOverride = new();
    private static readonly AsyncLocal<long?> _maxComparisonsOverride = new();

    /// <summary>
    /// The iteration cap in force for the calling execution context: <see cref="ProductionMaxIterations"/>
    /// unless an enclosing <see cref="Scope"/> lowered it.
    /// </summary>
    internal static int MaxIterations => _maxIterationsOverride.Value ?? ProductionMaxIterations;

    /// <summary>
    /// The comparison-count budget in force for the calling execution context:
    /// <see cref="ProductionMaxComparisons"/> unless an enclosing <see cref="Scope"/> lowered it.
    /// </summary>
    internal static long MaxComparisons => _maxComparisonsOverride.Value ?? ProductionMaxComparisons;

    /// <summary>
    /// Substitutes either or both thresholds for the scope's lifetime <em>within the calling execution
    /// context only</em>, restoring the enclosing values on <see cref="Dispose"/> unconditionally -
    /// including when the code under test throws, which is the expected outcome for every current caller
    /// of this seam. Leaving a parameter <see langword="null"/> keeps that threshold at whatever the
    /// enclosing context already had, so a test that wants to isolate one guard can override only that one
    /// without also having to restate the other's production value.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The override flows to work the scope's own execution context starts and is invisible to everything
    /// else, so - unlike the process-wide static this replaced - a scope held in one test class cannot be
    /// observed by a <c>repeat()</c> evaluated concurrently in another. Restoring the captured
    /// <em>previous</em> value rather than the production constant keeps nested scopes correct.
    /// </para>
    /// <para>
    /// <b>The flow is one-way, and it leaks outward.</b> <see cref="Dispose"/> restores the value in the
    /// disposing flow's execution context, not in the copies that already flowed to work started inside the
    /// scope, so a task started in the <c>using</c> keeps the lowered threshold after the scope ends.
    /// Measured with a <c>maxIterations: 11</c> scope: after <see cref="Dispose"/> the disposing flow read
    /// 10,000 while the escaped task still read 11. That is inherent to <see cref="AsyncLocal{T}"/> and
    /// harmless for both current callers, which are fully synchronous inside their <c>using</c>. An
    /// asynchronous one would see it, and would get a confusing pass rather than a failure.
    /// </para>
    /// <para>
    /// <b>Why a class and not a <see langword="readonly"/> <see langword="struct"/>.</b> As a struct,
    /// <c>new Scope()</c> bound to the struct's implicit parameterless constructor rather than the declared
    /// one - it omits no optional argument, so overload resolution preferred it - producing a zero-initialised
    /// value whose <c>_restore</c> fields were both <see langword="null"/> and whose <see cref="Dispose"/>
    /// therefore cleared the <em>enclosing</em> scope's override. Measured: inside a <c>maxIterations: 50</c>
    /// scope, disposing an empty inner scope left the threshold at 10,000 rather than 50. A struct cannot
    /// reject its own default instance; a class has no default instance to reject, and the allocation is
    /// irrelevant in a seam that runs a handful of times in tests.
    /// </para>
    /// </remarks>
    internal sealed class Scope : IDisposable
    {
        private readonly int? _restoreIterations;
        private readonly long? _restoreComparisons;

        internal Scope(int? maxIterations = null, long? maxComparisons = null)
        {
            _restoreIterations = _maxIterationsOverride.Value;
            _restoreComparisons = _maxComparisonsOverride.Value;

            if (maxIterations.HasValue)
                _maxIterationsOverride.Value = maxIterations.Value;

            if (maxComparisons.HasValue)
                _maxComparisonsOverride.Value = maxComparisons.Value;
        }

        public void Dispose()
        {
            _maxIterationsOverride.Value = _restoreIterations;
            _maxComparisonsOverride.Value = _restoreComparisons;
        }
    }
}
