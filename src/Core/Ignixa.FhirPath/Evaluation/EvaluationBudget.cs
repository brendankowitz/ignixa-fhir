// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Expressions;
using Ignixa.FhirPath.Types;

namespace Ignixa.FhirPath.Evaluation;

/// <summary>
/// Meters one evaluation against its <see cref="FhirPathEvaluationLimits"/> and cancellation token.
/// </summary>
/// <remarks>
/// <para>
/// One instance per <see cref="FhirPathEvaluator.Evaluate"/> call, shared by every <c>with</c>-derived copy of
/// that call's context the same way <see cref="EvaluationContext.ReferenceIndexCache"/> is, so the count is for
/// the whole evaluation rather than for whichever copy happened to do the work.
/// </para>
/// <para>
/// Not thread-safe, and does not need to be: an evaluation runs on one thread at a time, and every call starts
/// its own meter. The hot path is <see cref="Step"/>, called once per expression node: an add and a compare, with
/// the token and the clock read only when the count crosses a multiple of <see cref="CheckInterval"/>.
/// </para>
/// </remarks>
internal sealed class EvaluationBudget
{
    /// <summary>
    /// Steps between reads of the cancellation token and the clock. A power of two.
    /// </summary>
    internal const int CheckInterval = 1024;

    /// <summary>
    /// String characters charged as one step: building text costs far less per unit than evaluating a node, but
    /// it is what holds memory, so it must still drain the budget.
    /// </summary>
    internal const int CharactersPerStep = 16;

    /// <summary>
    /// The time one step stands for, used to charge work metered by the clock rather than counted: a library call
    /// whose cost its input does not predict is charged its elapsed time at this rate. Measured warm in a Release
    /// build, 10,000,000 steps of nested <c>select()</c> took 2.74 s, about 274 ns a step; timed work is charged
    /// slightly faster than that so it never drains the budget more slowly than counted work.
    /// </summary>
    internal const long NanosecondsPerStep = 250;

    private const int CheckShift = 10;

    private readonly CancellationToken _cancellationToken;
    private readonly long _deadline;
    private long _steps;
    private Dictionary<Expression, List<IElement>>? _invariantResults;
    private MeteredUnitConverter? _units;

    /// <summary>
    /// Starts metering an evaluation.
    /// </summary>
    /// <exception cref="OperationCanceledException">The token is already cancelled.</exception>
    public EvaluationBudget(FhirPathEvaluationLimits limits, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(limits);
        cancellationToken.ThrowIfCancellationRequested();

        Limits = limits;
        _cancellationToken = cancellationToken;
        _deadline = limits.Timeout is { } timeout
            ? Stopwatch.GetTimestamp() + (long)(timeout.TotalSeconds * Stopwatch.Frequency)
            : long.MaxValue;
    }

    /// <summary>The limits being enforced.</summary>
    public FhirPathEvaluationLimits Limits { get; }

    /// <summary>The steps charged so far.</summary>
    public long StepsTaken => _steps;

    /// <summary>
    /// The unit converter for this evaluation: UCUM work it does is charged here, and repeated conversions are
    /// answered from a per-evaluation memo.
    /// </summary>
    public IQuantityUnitConverter Units => _units ??= new MeteredUnitConverter(this, QuantityUnitConverter.Instance);

    /// <summary>
    /// Charges one step.
    /// </summary>
    /// <exception cref="FhirPathResourceLimitExceededException">The step budget or the deadline is exhausted.</exception>
    /// <exception cref="OperationCanceledException">The evaluation was cancelled.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Step() => Charge(1);

    /// <summary>
    /// Charges <paramref name="steps"/> steps at once, for work proportional to a size known up front.
    /// </summary>
    /// <exception cref="FhirPathResourceLimitExceededException">The step budget or the deadline is exhausted.</exception>
    /// <exception cref="OperationCanceledException">The evaluation was cancelled.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Charge(long steps)
    {
        var before = _steps;
        var after = before + steps;
        _steps = after;

        if (after > Limits.MaxSteps)
        {
            ThrowStepsExceeded();
        }

        // Crossing a multiple of CheckInterval, which a bulk charge may skip past rather than land on.
        if ((before >> CheckShift) != (after >> CheckShift))
        {
            CheckCancellationAndDeadline();
        }
    }

    /// <summary>
    /// Charges for building a string of <paramref name="length"/> characters, before it is built.
    /// </summary>
    /// <param name="length">The length the result will have. A <see cref="long"/> so a caller can compute it without overflow.</param>
    /// <param name="site">The operator or function building it, for the message.</param>
    /// <exception cref="FhirPathResourceLimitExceededException">The string would be too long, or the step budget is exhausted.</exception>
    public void EnsureStringLength(long length, string site)
    {
        if (length > Limits.MaxStringLength)
        {
            throw new FhirPathResourceLimitExceededException(string.Create(
                CultureInfo.InvariantCulture,
                $"{site} would build a string of {length} characters, more than the evaluation's limit of {Limits.MaxStringLength}."));
        }

        Charge(length / CharactersPerStep);
    }

    /// <summary>
    /// Charges the time elapsed since <paramref name="startTimestamp"/> at <see cref="NanosecondsPerStep"/>, at
    /// least one step.
    /// </summary>
    /// <param name="startTimestamp">A <see cref="Stopwatch.GetTimestamp"/> reading taken before the work.</param>
    /// <exception cref="FhirPathResourceLimitExceededException">The step budget or the deadline is exhausted.</exception>
    public void ChargeElapsed(long startTimestamp)
    {
        var elapsed = Stopwatch.GetElapsedTime(startTimestamp);
        Charge(Math.Max(1, (long)(elapsed.Ticks * (1_000_000_000d / TimeSpan.TicksPerSecond) / NanosecondsPerStep)));
    }

    /// <summary>
    /// Charges for scanning or transforming <paramref name="length"/> characters of existing text - case mapping,
    /// searching, splitting, decoding - at the same rate as building it.
    /// </summary>
    /// <exception cref="FhirPathResourceLimitExceededException">The step budget is exhausted.</exception>
    public void ChargeText(long length) => Charge(length / CharactersPerStep);

    /// <summary>
    /// Returns the value of a subexpression <see cref="LoopInvariance"/> has judged fixed for this evaluation,
    /// computing it on first use.
    /// </summary>
    /// <remarks>
    /// Held per evaluation, so nothing outlives the <see cref="FhirPathEvaluator.Evaluate"/> call that computed it. A
    /// reuse is charged a step per item: most consumers of a collection (sort, aggregates, type tests, paging) walk it
    /// without evaluating a node per item, so the reuse is charged as if the collection were built again.
    /// </remarks>
    public List<IElement> GetOrAddInvariant(Expression expression, Func<List<IElement>> compute)
    {
        _invariantResults ??= new Dictionary<Expression, List<IElement>>(ReferenceEqualityComparer.Instance);

        if (_invariantResults.TryGetValue(expression, out var cached))
        {
            Charge(cached.Count);
            return cached;
        }

        var computed = compute();
        _invariantResults[expression] = computed;
        return computed;
    }

    /// <summary>
    /// Checks the size of a collection the evaluator is building.
    /// </summary>
    /// <param name="count">The size the collection has reached, or will reach.</param>
    /// <param name="site">The operator or function building it, for the message.</param>
    /// <exception cref="FhirPathResourceLimitExceededException">The collection is too large.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void EnsureCollectionSize(long count, string site)
    {
        if (count > Limits.MaxCollectionSize)
        {
            ThrowCollectionTooLarge(count, site);
        }
    }

    /// <summary>
    /// Yields <paramref name="source"/> unchanged, charging a step per item and stopping it at
    /// <see cref="FhirPathEvaluationLimits.MaxCollectionSize"/> items, for operators whose output can be larger
    /// than their input without evaluating a node per item.
    /// </summary>
    public IEnumerable<IElement> Counted(IEnumerable<IElement> source, string site)
    {
        long count = 0;
        foreach (var item in source)
        {
            Step();
            EnsureCollectionSize(++count, site);
            yield return item;
        }
    }

    /// <summary>
    /// Yields <paramref name="source"/> unchanged, charging a step per item, for functions that walk their input
    /// without evaluating a node per item.
    /// </summary>
    public IEnumerable<IElement> PerItem(IEnumerable<IElement> source)
    {
        foreach (var item in source)
        {
            Step();
            yield return item;
        }
    }

    private void CheckCancellationAndDeadline()
    {
        _cancellationToken.ThrowIfCancellationRequested();

        if (_deadline != long.MaxValue && Stopwatch.GetTimestamp() > _deadline)
        {
            throw new FhirPathResourceLimitExceededException(string.Create(
                CultureInfo.InvariantCulture,
                $"FHIRPath evaluation exceeded its time limit of {Limits.Timeout!.Value.TotalMilliseconds} ms."));
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ThrowStepsExceeded() =>
        throw new FhirPathResourceLimitExceededException(string.Create(
            CultureInfo.InvariantCulture,
            $"FHIRPath evaluation exceeded its step budget of {Limits.MaxSteps} steps."));

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ThrowCollectionTooLarge(long count, string site) =>
        throw new FhirPathResourceLimitExceededException(string.Create(
            CultureInfo.InvariantCulture,
            $"{site} built a collection of {count} items, more than the evaluation's limit of {Limits.MaxCollectionSize}."));
}
