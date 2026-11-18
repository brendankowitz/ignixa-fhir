// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Numerics;

namespace Ignixa.FhirPath.Evaluation;

/// <summary>
/// The resource limits one FHIRPath evaluation runs under.
/// </summary>
/// <remarks>
/// <para>
/// A short expression can ask for exponential work independent of the resource it runs against -
/// <c>aggregate($total &amp; $total, 'aaaa')</c> doubles a string per item, nested <c>select()</c> multiplies
/// CPU with flat memory - so every evaluation carries these limits, and exceeding one stops evaluation with a
/// <see cref="FhirPathResourceLimitExceededException"/>. Cancellation is separate: it arrives through
/// <see cref="EvaluationContext.CancellationToken"/> and surfaces as <see cref="OperationCanceledException"/>,
/// because "the caller gave up" and "the expression asked for too much" call for different handling.
/// </para>
/// <para>
/// The limits apply to each call of <see cref="FhirPathEvaluator.Evaluate"/> (and so of
/// <see cref="TypedElementExtensions.Select"/>) separately, and cover all enumeration of the sequence it returns.
/// Most of <see cref="MaxSteps"/> is counted, not timed, but UCUM unit conversion and regular-expression work is
/// charged by its elapsed time, so an expression whose cost lies near the limit can fit on an idle machine and
/// exceed it on a loaded one (a search parameter that close to the limit may then be skipped while indexing). The
/// other limits are counted. <see cref="Timeout"/> is off by default and is for hosts that want a wall-clock bound
/// on top.
/// </para>
/// <para>
/// The defaults are sized to leave the shipped search-parameter and invariant corpus untouched while stopping
/// each known amplification within about three seconds of CPU and a few hundred megabytes; see the FHIRPath
/// limits page in the documentation for the measurements.
/// </para>
/// </remarks>
public sealed record FhirPathEvaluationLimits
{
    private readonly long _maxSteps = 10_000_000;
    private readonly int _maxCollectionSize = 1_000_000;
    private readonly int _maxStringLength = 16 * 1024 * 1024;
    private readonly int _maxRegexCaptureInputLength = 16 * 1024;
    private readonly TimeSpan? _timeout;
    private readonly int _maxRepeatIterations = 10_000;
    private readonly int _maxRepeatAllIterations = 100_000;
    private readonly long _maxRepeatComparisons = 15_000_000;

    /// <summary>
    /// The limits an evaluation runs under when the host sets none.
    /// </summary>
    public static FhirPathEvaluationLimits Default { get; } = new();

    /// <summary>
    /// The most units of work one evaluation may perform.
    /// </summary>
    /// <remarks>
    /// A step is charged for every expression node evaluated, every item a projection yields, every equality
    /// comparison a collection operation makes, and every 16 characters of string an operator builds; work whose
    /// cost its input does not predict - UCUM unit conversion, regular expressions - is charged its elapsed time
    /// at 250 nanoseconds a step. Measured warm in a Release build, a step is 250-450 nanoseconds of work, so the
    /// default bounds a runaway expression at about two to three seconds of CPU.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public long MaxSteps
    {
        get => _maxSteps;
        init => _maxSteps = Positive(value);
    }

    /// <summary>
    /// The most items a collection the evaluator builds may hold.
    /// </summary>
    /// <remarks>
    /// Checked where the evaluator creates collections larger than its inputs - <c>combine()</c>, <c>|</c>,
    /// <c>aggregate()</c>, <c>select()</c>, <c>repeat()</c>, <c>repeatAll()</c>, <c>split()</c>,
    /// <c>toChars()</c> and the pairing matrix behind <c>~</c> - not where it navigates the resource, whose size
    /// the host already bounds.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public int MaxCollectionSize
    {
        get => _maxCollectionSize;
        init => _maxCollectionSize = Positive(value);
    }

    /// <summary>
    /// The longest string, in UTF-16 code units, an operator may build.
    /// </summary>
    /// <remarks>
    /// Checked before the string is allocated, at every site whose output can be longer than its inputs:
    /// <c>&amp;</c>, <c>replace()</c>, <c>replaceMatches()</c>, <c>join()</c> and <c>encode()</c>. Strings read
    /// from the resource are not limited.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public int MaxStringLength
    {
        get => _maxStringLength;
        init => _maxStringLength = Positive(value);
    }

    /// <summary>
    /// The longest input <c>replaceMatches()</c> accepts when its substitution contains any reference: the match
    /// (<c>$0</c>, <c>$&amp;</c>), a group (<c>$1</c>, <c>${name}</c>, <c>$+</c>), or the text around the match
    /// (<c>$`</c>, <c>$'</c>, <c>$_</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The linear-time regex engine does not check its match timeout while it computes captures, and that phase
    /// costs roughly input length times the pattern's repetition count: <c>replaceMatches('(.*a){80}', '$1')</c>
    /// over a 1,000,000-character input ran 21 seconds under a one-second timeout. Neither the timeout nor the step
    /// budget can interrupt it, so the input is bounded before the call instead.
    /// </para>
    /// <para>
    /// Measured on the linear engine, the capture phase added 0.3 s at 8,192 characters, 0.6 s at 16,384, 1.4 s at
    /// 32,768 and 1.6 s at 65,536 (with a smaller repetition count), each on top of a match phase the one-second
    /// timeout already bounds; repetition counts high enough to cost more time out during matching. 16,384 keeps one
    /// call under about two seconds while leaving room for any realistic text. A substitution with no reference
    /// (a literal, or <c>$$</c>) computes no captures and is not limited by this.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public int MaxRegexCaptureInputLength
    {
        get => _maxRegexCaptureInputLength;
        init => _maxRegexCaptureInputLength = Positive(value);
    }

    /// <summary>
    /// The wall-clock time one evaluation may take, or <see langword="null"/> for no time limit.
    /// </summary>
    /// <remarks>
    /// Checked every 1,024 steps, so it bounds evaluation to within a few microseconds of work past the deadline.
    /// Exceeding it raises <see cref="FhirPathResourceLimitExceededException"/>, not
    /// <see cref="OperationCanceledException"/>: the deadline is the host's limit on the expression, and the
    /// token is for the caller abandoning the request.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is zero or negative.</exception>
    public TimeSpan? Timeout
    {
        get => _timeout;
        init => _timeout = value is { } timeout && timeout <= TimeSpan.Zero
            ? throw new ArgumentOutOfRangeException(nameof(value), timeout, "The timeout must be positive.")
            : value;
    }

    /// <summary>
    /// The most items <c>repeat()</c> may dequeue. See <c>CollectionFunctions.Repeat</c> for how it was sized.
    /// </summary>
    internal int MaxRepeatIterations
    {
        get => _maxRepeatIterations;
        init => _maxRepeatIterations = Positive(value);
    }

    /// <summary>
    /// The most items <c>repeatAll()</c> may dequeue. Larger than <see cref="MaxRepeatIterations"/> because
    /// <c>repeatAll()</c> does not deduplicate, so a finite tree it walks is dequeued once per path to each node.
    /// </summary>
    internal int MaxRepeatAllIterations
    {
        get => _maxRepeatAllIterations;
        init => _maxRepeatAllIterations = Positive(value);
    }

    /// <summary>
    /// The most equality comparisons <c>repeat()</c> may make. See <c>CollectionFunctions.Repeat</c>.
    /// </summary>
    internal long MaxRepeatComparisons
    {
        get => _maxRepeatComparisons;
        init => _maxRepeatComparisons = Positive(value);
    }

    private static T Positive<T>(T value)
        where T : INumberBase<T>
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
        return value;
    }
}
