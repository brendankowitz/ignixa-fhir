/*
 * Copyright (c) 2025, Ignixa Contributors
 */

namespace Ignixa.FhirPath.Evaluation.Functions;

/// <summary>
/// The guard thresholds <see cref="CollectionFunctions.Repeat"/> checks against, and a test-only seam
/// (#435) to substitute smaller ones.
/// </summary>
/// <remarks>
/// <para>
/// Production code always runs against <see cref="MaxIterations"/> and <see cref="MaxComparisons"/> at
/// their real values, documented on <see cref="CollectionFunctions.Repeat"/> itself. Only
/// <c>Ignixa.FhirPath.Tests</c> (this assembly grants it <c>InternalsVisibleTo</c>) can see this type at
/// all, and the only supported way to change either value is <see cref="Scope"/>, which always restores
/// the production value afterward - even if the guarded call throws, which it is expected to, since
/// proving a guard trips is the entire point of lowering it.
/// </para>
/// <para>
/// <b>Why this exists (#435):</b> before this seam, the tests proving <c>Repeat</c>'s guards actually
/// trip had no way to drive them at anything but the real 10,000/comparison-budget scale, so proving the
/// iteration guard fires cost its own real-world wall-clock time - 33 seconds, by construction - on every
/// CI run, on every target framework, forever. A small cap proves the identical throw-and-log-tier
/// behaviour in milliseconds; this type is what lets a test substitute one.
/// </para>
/// </remarks>
internal static class RepeatGuardLimits
{
    /// <summary>
    /// The real production iteration cap. See <see cref="CollectionFunctions.Repeat"/>'s remarks for why
    /// 10,000 is a data-headroom figure, chosen against a measured control case, not an arbitrary round number.
    /// </summary>
    internal static int MaxIterations = 10_000;

    /// <summary>
    /// The real production comparison-count budget. See <see cref="CollectionFunctions.Repeat"/>'s remarks
    /// for the measurement this value is based on and why it is a cost figure rather than a data-headroom one.
    /// </summary>
    internal static long MaxComparisons = 15_000_000;

    /// <summary>
    /// Temporarily substitutes either or both thresholds for the scope's lifetime, restoring the
    /// production values on <see cref="Dispose"/> unconditionally - including when the code under test
    /// throws, which is the expected outcome for every current caller of this seam. Leaving a parameter
    /// <see langword="null"/> keeps that threshold at its current (normally production) value, so a test
    /// that wants to isolate one guard can override only that one without also having to restate the
    /// other's production value.
    /// </summary>
    /// <remarks>
    /// Not thread-safe by design: <see cref="MaxIterations"/> and <see cref="MaxComparisons"/> are process-wide
    /// statics, so a test using this scope must not run concurrently with another test that calls
    /// <c>repeat()</c> expecting production-scale limits. xUnit's default parallelization runs test classes
    /// concurrently but methods within one class sequentially, so this is safe as long as every caller of
    /// this seam lives in the same test class as any large-scale <c>repeat()</c> test it could otherwise race.
    /// </remarks>
    internal readonly struct Scope : IDisposable
    {
        private readonly int _restoreIterations;
        private readonly long _restoreComparisons;

        internal Scope(int? maxIterations = null, long? maxComparisons = null)
        {
            _restoreIterations = MaxIterations;
            _restoreComparisons = MaxComparisons;

            if (maxIterations.HasValue)
                MaxIterations = maxIterations.Value;

            if (maxComparisons.HasValue)
                MaxComparisons = maxComparisons.Value;
        }

        public void Dispose()
        {
            MaxIterations = _restoreIterations;
            MaxComparisons = _restoreComparisons;
        }
    }
}
