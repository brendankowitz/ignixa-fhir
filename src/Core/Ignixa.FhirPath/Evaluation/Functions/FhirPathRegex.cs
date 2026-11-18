// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

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
/// timeout is a <see cref="FhirPathResourceLimitExceededException"/>, never "no match". One gap remains:
/// the linear engine does not check the timeout while it computes capture groups, which only
/// <c>replaceMatches()</c> asks for when its substitution names a group. Measured on a 1,000,000-character
/// input, <c>replaceMatches('(.*a){80}', '$1')</c> ran 21 s under a 1 s timeout; the cost grows with input
/// length times repetition count. Bounding that is left to a per-evaluation budget.
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
    private const int CacheCapacity = 256;

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
    public static Regex Get(string pattern, RegexOptions options, string functionName)
    {
        var timeout = MatchTimeout;
        var key = Key('P', options, timeout, pattern);
        return _cache.TryGetValue(key, out var hit) ? hit : AddPattern(key, pattern, options, functionName, timeout);
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
    public static Regex GetAnchored(string pattern, RegexOptions options, string functionName)
    {
        var timeout = MatchTimeout;
        var key = Key('A', options, timeout, pattern);
        return _cache.TryGetValue(key, out var hit) ? hit : AddAnchored(key, pattern, options, functionName, timeout);
    }

    /// <param name="pattern">The pattern as the caller wrote it, for the error message.</param>
    /// <exception cref="FhirPathResourceLimitExceededException">The match exceeded the regex's timeout.</exception>
    public static bool IsMatch(Regex regex, string pattern, string input, string functionName)
    {
        try
        {
            return regex.IsMatch(input);
        }
        catch (RegexMatchTimeoutException ex)
        {
            throw Timeout(ex, pattern, functionName);
        }
    }

    /// <param name="pattern">The pattern as the caller wrote it, for the error message.</param>
    /// <exception cref="FhirPathResourceLimitExceededException">The match exceeded the regex's timeout.</exception>
    public static string Replace(Regex regex, string pattern, string input, string substitution, string functionName)
    {
        try
        {
            return regex.Replace(input, substitution);
        }
        catch (RegexMatchTimeoutException ex)
        {
            throw Timeout(ex, pattern, functionName);
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
                $"Invalid regular expression pattern '{pattern}' in {functionName}(): {ex.Message}", ex);
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
                $"Regular expression pattern '{pattern}' in {functionName}() exceeded its match timeout of {ex.MatchTimeout.TotalMilliseconds} ms."),
            ex);

    /// <summary>
    /// Test-only: substitutes <see cref="MatchTimeout"/> for the scope's lifetime, within the calling execution
    /// context only, so a test can prove a timeout surfaces without spending the production second.
    /// </summary>
    /// <remarks>
    /// Same shape and caveats as <see cref="RepeatGuardLimits.Scope"/>: an <see cref="AsyncLocal{T}"/> override,
    /// restored on dispose, kept out of production code by <c>Ignixa.RepoGuards.Tests.FhirPathRegexTimeoutSeamGuardTests</c>.
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
