// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using Ignixa.Abstractions;

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

    private const int CheckShift = 10;

    private readonly CancellationToken _cancellationToken;
    private readonly long _deadline;
    private long _steps;

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
