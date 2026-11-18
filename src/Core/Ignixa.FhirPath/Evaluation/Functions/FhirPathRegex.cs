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
/// linear in the input, so catastrophic backtracking cannot occur. That engine rejects lookarounds,
/// backreferences, atomic groups, conditionals and <c>\G</c> with <see cref="NotSupportedException"/>;
/// only those patterns fall back to the backtracking engine, and that engine always runs under
/// <see cref="BacktrackingMatchTimeout"/>. A timeout is an evaluation error, never "no match".
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
    /// Match timeout for patterns the linear-time engine cannot run. Fixed for now; a per-evaluation
    /// budget, when one exists, would pass its remaining time to <see cref="Create"/> in place of this.
    /// </summary>
    internal static readonly TimeSpan BacktrackingMatchTimeout = TimeSpan.FromSeconds(1);

    private const int CacheCapacity = 256;

    /// <summary>
    /// Constructed regexes keyed by options and pattern. Pattern text is caller-influenced, hence bounded.
    /// A <see cref="Regex"/> is immutable and safe to share across threads. Invalid patterns throw from the
    /// factory and are not cached.
    /// </summary>
    private static readonly BoundedExpressionCache<Regex> _cache = new(CacheCapacity);

    /// <summary>
    /// Returns a cached regex for <paramref name="pattern"/>, building it on a miss.
    /// </summary>
    /// <exception cref="FhirPathEvaluationException">The pattern is not a valid regular expression.</exception>
    public static Regex Get(string pattern, RegexOptions options, string functionName)
    {
        var key = string.Create(CultureInfo.InvariantCulture, $"{(int)options}:{pattern}");
        return _cache.GetOrAdd(key, _ => Create(pattern, options, functionName, BacktrackingMatchTimeout));
    }

    /// <summary>
    /// Builds a regex without caching, preferring the linear-time engine.
    /// </summary>
    /// <param name="backtrackingTimeout">The match timeout applied if the pattern needs the backtracking engine.</param>
    /// <exception cref="FhirPathEvaluationException">The pattern is not a valid regular expression.</exception>
    internal static Regex Create(string pattern, RegexOptions options, string functionName, TimeSpan backtrackingTimeout)
    {
        try
        {
            return Build(pattern, options | RegexOptions.CultureInvariant, backtrackingTimeout);
        }
        catch (ArgumentException ex)
        {
            throw new FhirPathEvaluationException(
                $"Invalid regular expression pattern '{pattern}' in {functionName}(): {ex.Message}", ex);
        }
    }

    private static Regex Build(string pattern, RegexOptions options, TimeSpan backtrackingTimeout)
    {
        try
        {
            return new Regex(pattern, options | RegexOptions.NonBacktracking);
        }
        catch (NotSupportedException)
        {
            // The pattern needs a construct only the backtracking engine has; run it there under a timeout.
            return new Regex(pattern, options, backtrackingTimeout);
        }
    }

    /// <exception cref="FhirPathEvaluationException">The match exceeded the regex's timeout.</exception>
    public static bool IsMatch(Regex regex, string input, string functionName)
    {
        try
        {
            return regex.IsMatch(input);
        }
        catch (RegexMatchTimeoutException ex)
        {
            throw Timeout(ex, functionName);
        }
    }

    /// <exception cref="FhirPathEvaluationException">The match exceeded the regex's timeout.</exception>
    public static string Replace(Regex regex, string input, string substitution, string functionName)
    {
        try
        {
            return regex.Replace(input, substitution);
        }
        catch (RegexMatchTimeoutException ex)
        {
            throw Timeout(ex, functionName);
        }
    }

    private static FhirPathEvaluationException Timeout(RegexMatchTimeoutException ex, string functionName) =>
        new(string.Create(
                CultureInfo.InvariantCulture,
                $"Regular expression pattern '{ex.Pattern}' in {functionName}() exceeded its match timeout of {ex.MatchTimeout.TotalMilliseconds} ms."),
            ex);
}
