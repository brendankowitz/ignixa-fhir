// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Ignixa.FhirPath.Evaluation.Functions;

/// <summary>
/// Builds, caches and runs the regular expressions behind <c>matches()</c>, <c>matchesFull()</c> and
/// <c>replaceMatches()</c>, whose patterns come from expression text the server does not control.
/// </summary>
/// <remarks>
/// <para>
/// A pattern is compiled with <see cref="RegexOptions.NonBacktracking"/> first, which matches in time
/// linear in the input, so catastrophic backtracking cannot occur. That engine refuses some patterns with
/// <see cref="NotSupportedException"/>: lookarounds, backreferences, atomic groups, conditionals and
/// <c>\G</c>, and any pattern whose automaton would exceed the engine's size limit (for example an
/// alternation of a few thousand branches). Only those patterns fall back to the backtracking engine.
/// </para>
/// <para>
/// Both engines run under <see cref="MatchTimeout"/>. Linear time is not small time - a large bounded
/// repetition over a long input can still take seconds - so the timeout is the bound in both cases. A
/// timeout is a <see cref="FhirPathResourceLimitExceededException"/>, never "no match". The linear engine does
/// not check the timeout while it computes capture groups, which only <c>replaceMatches()</c> asks for when its
/// substitution contains a reference: measured on a 1,000,000-character input, <c>replaceMatches('(.*a){80}', '$1')</c>
/// ran 21 s under a 1 s timeout, the cost growing with input length times repetition count. That case is bounded
/// by <see cref="FhirPathEvaluationLimits.MaxRegexCaptureInputLength"/>, which <c>replaceMatches()</c> checks
/// before matching.
/// </para>
/// <para>
/// Regex work is charged to the evaluation by the clock, at <see cref="EvaluationBudget.NanosecondsPerStep"/>:
/// building a regex, matching, and replacing. Each call's match timeout is the lesser of <see cref="MatchTimeout"/>
/// and the time the evaluation's remaining steps stand for (rounded down to one of a few fixed fractions of
/// <see cref="MatchTimeout"/>, so the cache holds a handful of timeouts per pattern), so the regex time of one
/// evaluation is bounded by its step budget rather than by the number of calls. A pattern longer than
/// <see cref="MaxPatternLength"/> is refused before it is built: construction cost grows with the pattern, which a
/// FHIRPath expression can compute at runtime.
/// </para>
/// <para>
/// Both engines are built with <see cref="RegexOptions.CultureInvariant"/>: case-insensitive matching
/// (the <c>i</c> flag or an inline <c>(?i)</c>) otherwise uses the host culture captured at construction,
/// and under tr-TR <c>'I'.matches('(?i)i')</c> is false.
/// </para>
/// <para>
/// An invalid pattern is an evaluation error, not an empty result: a <c>where(x.matches(bad)).empty()</c>
/// would otherwise be true for every input.
/// </para>
/// </remarks>
internal static class FhirPathRegex
{
    /// <summary>
    /// The longest pattern, in characters, <c>matches()</c>, <c>matchesFull()</c> and <c>replaceMatches()</c> accept.
    /// </summary>
    internal const int MaxPatternLength = 4096;

    private const int CacheCapacity = 256;

    /// <summary>
    /// The fractions of <see cref="MatchTimeout"/> a call's timeout is rounded down to. The smallest is also the
    /// floor: an evaluation with less time left than that still gets it, so a call can overrun the budget by at most
    /// that much before the next charge stops the evaluation.
    /// </summary>
    private static readonly int[] TimeoutDivisors = [1, 4, 16, 64];

    /// <summary>
    /// The production match timeout. Fixed for now; a per-evaluation budget, when one exists, would pass its
    /// remaining time where <see cref="MatchTimeout"/> is read.
    /// </summary>
    private static readonly TimeSpan DefaultMatchTimeout = TimeSpan.FromSeconds(1);

    private static readonly AsyncLocal<TimeSpan?> _matchTimeoutOverride = new();

    /// <summary>
    /// Constructed regexes keyed by kind, options, timeout and pattern. Pattern text is caller-influenced,
    /// hence bounded. A <see cref="Regex"/> is immutable and safe to share across threads. Invalid patterns
    /// throw from the factory and are not cached.
    /// </summary>
    private static readonly BoundedExpressionCache<Regex> _cache = new(CacheCapacity);

    /// <summary>The match timeout in force for the calling execution context.</summary>
    internal static TimeSpan MatchTimeout => _matchTimeoutOverride.Value ?? DefaultMatchTimeout;

    /// <summary>
    /// Returns a cached regex for <paramref name="pattern"/>, building it on a miss.
    /// </summary>
    /// <exception cref="FhirPathEvaluationException">The pattern is not a valid regular expression.</exception>
    /// <exception cref="FhirPathResourceLimitExceededException">
    /// The pattern is longer than <see cref="MaxPatternLength"/>, or building it exhausted the budget.
    /// </exception>
    public static Regex Get(string pattern, RegexOptions options, string functionName, EvaluationBudget budget)
    {
        EnsurePatternLength(pattern, functionName);
        var timeout = TimeoutFor(budget);
        var key = Key('P', options, timeout, pattern);
        var start = Stopwatch.GetTimestamp();
        var regex = _cache.TryGetValue(key, out var hit) ? hit : AddPattern(key, pattern, options, functionName, timeout);
        budget.ChargeElapsed(start);
        return regex;
    }

    /// <summary>
    /// Returns a cached regex that matches <paramref name="pattern"/> against the whole input, as
    /// <c>matchesFull()</c> requires.
    /// </summary>
    /// <remarks>
    /// The raw pattern is validated first: wrapping can turn an invalid pattern (<c>a)|(b</c>) into a valid
    /// one, and the error should quote the text the caller wrote. Once the raw pattern is valid, the only way
    /// wrapping it can fail is a trailing <c>#</c> comment (the <c>x</c> flag or an inline <c>(?x)</c>) running
    /// to end of line and swallowing the closing; a newline ends the comment and is ignorable whitespace in
    /// comment mode, so that form is used instead.
    /// </remarks>
    /// <exception cref="FhirPathEvaluationException">The pattern is not a valid regular expression.</exception>
    /// <exception cref="FhirPathResourceLimitExceededException">
    /// The pattern is longer than <see cref="MaxPatternLength"/>, or building it exhausted the budget.
    /// </exception>
    public static Regex GetAnchored(string pattern, RegexOptions options, string functionName, EvaluationBudget budget)
    {
        EnsurePatternLength(pattern, functionName);
        var timeout = TimeoutFor(budget);
        var key = Key('A', options, timeout, pattern);
        var start = Stopwatch.GetTimestamp();
        var regex = _cache.TryGetValue(key, out var hit) ? hit : AddAnchored(key, pattern, options, functionName, timeout);
        budget.ChargeElapsed(start);
        return regex;
    }

    /// <param name="regex">A regex from <see cref="Get"/> or <see cref="GetAnchored"/>.</param>
    /// <param name="pattern">The pattern as the caller wrote it, for the error message.</param>
    /// <param name="input">The text to match.</param>
    /// <param name="functionName">The calling function, for the error message.</param>
    /// <param name="budget">Charged the time the match takes.</param>
    /// <exception cref="FhirPathResourceLimitExceededException">The match exceeded the regex's timeout or the budget.</exception>
    public static bool IsMatch(Regex regex, string pattern, string input, string functionName, EvaluationBudget budget)
    {
        var start = Stopwatch.GetTimestamp();
        try
        {
            return regex.IsMatch(input);
        }
        catch (RegexMatchTimeoutException ex)
        {
            throw Timeout(ex, pattern, functionName);
        }
        finally
        {
            ChargeUnlessExhausted(budget, start);
        }
    }

    /// <summary>
    /// Replaces every match, after proving the result fits the evaluation's string limit.
    /// </summary>
    /// <param name="pattern">The pattern as the caller wrote it, for the error message.</param>
    /// <remarks>
    /// <para>
    /// The output length is bounded before anything is built, by walking the matches with
    /// <see cref="Regex.EnumerateMatches(ReadOnlySpan{char})"/> - which finds match bounds only, never captures,
    /// and is under the same timeout - and summing an upper bound on each expansion
    /// (<see cref="RegexSubstitution"/>). A pattern that matches the empty string at every position multiplies
    /// the input by the substitution, the regex analogue of <c>replace('', s)</c>, and this is what stops it.
    /// Each match is also a step, so many matches drain the budget and observe cancellation.
    /// </para>
    /// <para>
    /// A group can only be as long as the match on the linear engine, which has no lookarounds; the backtracking
    /// engine can capture inside a lookbehind or lookahead outside the match, so there a group is bounded by the
    /// input instead.
    /// </para>
    /// </remarks>
    /// <exception cref="FhirPathResourceLimitExceededException">
    /// The match exceeded the regex's timeout, or the result would exceed the evaluation's string or step limit.
    /// </exception>
    public static string Replace(Regex regex, string pattern, string input, string substitution, string functionName, EvaluationBudget budget)
    {
        ArgumentNullException.ThrowIfNull(regex);
        ArgumentNullException.ThrowIfNull(budget);

        var shape = RegexSubstitution.Parse(substitution);
        var linear = (regex.Options & RegexOptions.NonBacktracking) != 0;
        var start = Stopwatch.GetTimestamp();

        try
        {
            long length = input.Length;
            foreach (var match in regex.EnumerateMatches(input))
            {
                budget.Step();
                var groupBound = linear ? match.Length : input.Length;
                length += shape.MaxExpansionLength(match.Index, match.Length, input.Length, groupBound) - match.Length;
            }

            budget.EnsureStringLength(length, functionName + "()");

            return regex.Replace(input, substitution);
        }
        catch (RegexMatchTimeoutException ex)
        {
            throw Timeout(ex, pattern, functionName);
        }
        finally
        {
            ChargeUnlessExhausted(budget, start);
        }
    }

    /// <summary>
    /// Reports whether a substitution contains any reference, which is what makes the linear engine compute captures -
    /// the one phase of matching its timeout does not cover.
    /// </summary>
    public static bool NeedsCaptures(string substitution) => RegexSubstitution.Parse(substitution).NeedsCaptures;

    private static void EnsurePatternLength(string pattern, string functionName)
    {
        if (pattern.Length > MaxPatternLength)
        {
            throw new FhirPathResourceLimitExceededException(string.Create(
                CultureInfo.InvariantCulture,
                $"{functionName}() was given a pattern of {pattern.Length} characters, more than the limit of {MaxPatternLength}."));
        }
    }

    /// <summary>
    /// The match timeout for a call: <see cref="MatchTimeout"/>, or the largest of its fixed fractions that fits in the
    /// time the budget's remaining steps stand for.
    /// </summary>
    private static TimeSpan TimeoutFor(EvaluationBudget budget)
    {
        var cap = MatchTimeout;
        var remainingSteps = Math.Max(0, budget.Limits.MaxSteps - budget.StepsTaken);
        var remaining = TimeSpan.FromTicks(
            (long)Math.Min(TimeSpan.MaxValue.Ticks, remainingSteps * (double)EvaluationBudget.NanosecondsPerStep / 100d));

        foreach (var divisor in TimeoutDivisors)
        {
            var candidate = cap / divisor;
            if (candidate <= remaining)
            {
                return candidate;
            }
        }

        return cap / TimeoutDivisors[^1];
    }

    /// <summary>
    /// Charges the elapsed time of a regex call, from a <c>finally</c>: a timed-out call is charged too, but a charge
    /// must not replace an exception already propagating, so a budget that is already exhausted is left to the
    /// exception in flight or to the next step.
    /// </summary>
    private static void ChargeUnlessExhausted(EvaluationBudget budget, long start)
    {
        if (budget.StepsTaken < budget.Limits.MaxSteps)
        {
            budget.ChargeElapsed(start);
        }
    }

    private static string Key(char kind, RegexOptions options, TimeSpan timeout, string pattern) =>
        string.Create(CultureInfo.InvariantCulture, $"{kind}{(int)options}:{timeout.Ticks}:{pattern}");

    private static Regex AddPattern(string key, string pattern, RegexOptions options, string functionName, TimeSpan timeout) =>
        _cache.GetOrAdd(key, _ => Compile(pattern, pattern, options, functionName, timeout));

    private static Regex AddAnchored(string key, string pattern, RegexOptions options, string functionName, TimeSpan timeout) =>
        _cache.GetOrAdd(key, _ =>
        {
            Compile(pattern, pattern, options, functionName, timeout);
            try
            {
                return Build("^(?:" + pattern + ")$", options, timeout);
            }
            catch (ArgumentException)
            {
                return Compile("^(?:" + pattern + "\n)$", pattern, options, functionName, timeout);
            }
        });

    /// <param name="regexText">The text compiled.</param>
    /// <param name="pattern">The pattern as the caller wrote it, for the error message.</param>
    private static Regex Compile(string regexText, string pattern, RegexOptions options, string functionName, TimeSpan timeout)
    {
        try
        {
            return Build(regexText, options, timeout);
        }
        catch (ArgumentException ex)
        {
            throw new FhirPathEvaluationException(
                $"Invalid regular expression pattern '{LogSafeText.Sanitize(pattern)}' in {functionName}(): {LogSafeText.Sanitize(ex.Message)}", ex);
        }
    }

    private static Regex Build(string regexText, RegexOptions options, TimeSpan timeout)
    {
        options |= RegexOptions.CultureInvariant;
        try
        {
            return new Regex(regexText, options | RegexOptions.NonBacktracking, timeout);
        }
        catch (NotSupportedException)
        {
            // A construct only the backtracking engine has, or an automaton beyond the linear engine's size limit.
            return new Regex(regexText, options, timeout);
        }
    }

    private static FhirPathResourceLimitExceededException Timeout(RegexMatchTimeoutException ex, string pattern, string functionName) =>
        new(string.Create(
                CultureInfo.InvariantCulture,
                $"Regular expression pattern '{LogSafeText.Sanitize(pattern)}' in {functionName}() exceeded its match timeout of {ex.MatchTimeout.TotalMilliseconds} ms, the lesser of the per-call limit and the evaluation's remaining budget."),
            ex);

    /// <summary>
    /// Test-only: substitutes <see cref="MatchTimeout"/> for the scope's lifetime, within the calling execution
    /// context only, so a test can prove a timeout surfaces without spending the production second.
    /// </summary>
    /// <remarks>
    /// An <see cref="AsyncLocal{T}"/> override, restored on dispose, kept out of production code by
    /// <c>Ignixa.RepoGuards.Tests.FhirPathRegexTimeoutSeamGuardTests</c>. Restoring in the disposing flow only, it
    /// leaks into work started inside the scope; harmless for the synchronous tests that use it.
    /// The timeout is part of the cache key, so a scoped timeout never reaches a regex cached under another.
    /// </remarks>
    internal sealed class TimeoutScope : IDisposable
    {
        private readonly TimeSpan? _restore;

        internal TimeoutScope(TimeSpan matchTimeout)
        {
            _restore = _matchTimeoutOverride.Value;
            _matchTimeoutOverride.Value = matchTimeout;
        }

        public void Dispose() => _matchTimeoutOverride.Value = _restore;
    }
}
