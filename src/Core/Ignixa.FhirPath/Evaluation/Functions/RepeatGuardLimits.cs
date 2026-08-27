/*
 * Copyright (c) 2025, Ignixa Contributors
 */

namespace Ignixa.FhirPath.Evaluation.Functions;

/// <remarks>
/// <para>
/// The seam exists so tests proving <c>Repeat</c>'s guards trip need not drive them at the real
/// 10,000-iteration scale, which costs 33 seconds of wall clock per CI run per target framework.
/// </para>
/// <para>
/// Both thresholds are read-only over a private <see cref="AsyncLocal{T}"/> override, so <see cref="Scope"/>
/// is the only mutator and every mutation is scoped and self-restoring. That narrows the seam without
/// closing it: <c>InternalsVisibleTo</c> names two production assemblies (<c>Ignixa.SqlOnFhir</c>,
/// <c>Ignixa.Search</c>) as well as the two test ones, so "production runs against the real values" is a
/// convention enforced by a failing test - <c>Ignixa.RepoGuards.Tests.RepeatGuardLimitsSeamGuardTests</c>
/// rejects any file under <c>src/</c> other than this one that names <see cref="Scope"/> outside a doc
/// comment.
/// </para>
/// <para>
/// <see cref="AsyncLocal{T}"/> rather than a static field because a static was demonstrated to bleed: xUnit
/// runs test classes in parallel by default, so a scope lowering <see cref="MaxComparisons"/> to 50 in one
/// class broke a concurrent class evaluating <c>repeat(item)</c> over a 363-item tree. The read is not free
/// and <c>ContainsElement</c> consults <see cref="MaxComparisons"/> up to fifteen million times per call,
/// so <see cref="CollectionFunctions.Repeat"/> hoists both into locals at entry - sound because it is
/// eager.
/// </para>
/// </remarks>
internal static class RepeatGuardLimits
{
    /// <summary>
    /// The production iteration cap. See <see cref="CollectionFunctions.Repeat"/>'s remarks for why 10,000
    /// is a data-headroom figure rather than an arbitrary round number.
    /// </summary>
    private const int ProductionMaxIterations = 10_000;

    /// <summary>
    /// The production comparison-count budget - a cost figure, not a data-headroom one. See
    /// <see cref="CollectionFunctions.Repeat"/>'s remarks for the difference and why it matters.
    /// </summary>
    private const long ProductionMaxComparisons = 15_000_000;

    private static readonly AsyncLocal<int?> _maxIterationsOverride = new();
    private static readonly AsyncLocal<long?> _maxComparisonsOverride = new();

    /// <summary>The iteration cap in force for the calling execution context.</summary>
    internal static int MaxIterations => _maxIterationsOverride.Value ?? ProductionMaxIterations;

    /// <summary>The comparison-count budget in force for the calling execution context.</summary>
    internal static long MaxComparisons => _maxComparisonsOverride.Value ?? ProductionMaxComparisons;

    /// <summary>
    /// Substitutes either or both thresholds for the scope's lifetime, within the calling execution context
    /// only, restoring the enclosing values on <see cref="Dispose"/> even when the code under test throws
    /// (the expected outcome for every current caller). A <see langword="null"/> parameter leaves that
    /// threshold at the enclosing value; restoring the captured previous value rather than the production
    /// constant keeps nested scopes correct.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The override leaks outward: <see cref="Dispose"/> restores it in the disposing flow only, not in the
    /// execution-context copies that already flowed to work started inside the scope (measured: 10,000 in
    /// the disposing flow, 11 in an escaped task). Inherent to <see cref="AsyncLocal{T}"/> and harmless for
    /// the current callers, which are synchronous inside their <c>using</c>; an asynchronous one would get
    /// a confusing pass.
    /// </para>
    /// <para>
    /// A class, not a <see langword="readonly"/> <see langword="struct"/>: as a struct, <c>new Scope()</c>
    /// bound to the implicit parameterless constructor, yielding null <c>_restore</c> fields whose
    /// <see cref="Dispose"/> cleared the <em>enclosing</em> scope's override.
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
