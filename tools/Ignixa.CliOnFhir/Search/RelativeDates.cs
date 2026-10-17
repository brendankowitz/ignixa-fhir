// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using System.Text.RegularExpressions;
using Ignixa.CliOnFhir.Clock;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.Search.Definition;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.CliOnFhir.Search;

/// <summary>
/// Relative values of date search parameters, resolved against asOf before the query is parsed: <c>now</c> and <c>today</c>, each
/// optionally less an amount of hours (<c>h</c>, <c>now</c> only), days (<c>d</c>), weeks (<c>wk</c>), months (<c>mo</c>) or years
/// (<c>y</c>), after any FHIR prefix: <c>date=genow-24h</c>, <c>date=getoday-2d</c>, <c>birthdate=lenow-40y</c>. <c>now</c> is the
/// asOf instant, written in UTC; <c>today</c> is the asOf calendar day in the asOf's own offset (UTC unless <c>--as-of</c> gave
/// another), written as a date, which the search reads as that day in UTC. Months and years are calendar arithmetic: a day the
/// month lacks becomes its last (2025-03-31 less a month is 2025-02-28), as FHIRPath adds calendar durations. Only a value of a
/// known date parameter (with its chain) is read; any other value, and every value of a parameter with a modifier, is the query's.
/// </summary>
internal static partial class RelativeDates
{
    /// <summary>The information issue that states the rewrite.</summary>
    public const string Code = "relative-date-resolved";

    /// <summary>The query with every relative date resolved, what was resolved, and the note; the request itself when nothing was.</summary>
    /// <param name="asOf">The instant to resolve against, asked for only when the query has a relative date.</param>
    /// <exception cref="ToolRequestException"><c>invalid-parameter</c>: a value that starts like a relative date but is not one.</exception>
    public static (SearchRequest Request, IReadOnlyList<ResolvedParameter> Resolved, IssueItem? Note) Resolve(
        ISearchParameterDefinitionManager definitions, SearchRequest request, Func<AsOf> asOf)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(asOf);

        if (string.IsNullOrEmpty(request.Query) || !Anchor().IsMatch(request.Query))
        {
            return (request, [], null);
        }

        AsOf? anchor = null;
        List<ResolvedParameter> resolved = [];
        List<string> segments = [];
        foreach (string segment in request.Query.Split('&'))
        {
            int equals = segment.IndexOf('=', StringComparison.Ordinal);
            if (equals <= 0 || !IsDateParameter(definitions, request.ResourceType, segment[..equals]))
            {
                segments.Add(segment);
                continue;
            }

            string name = segment[..equals];
            string value = segment[(equals + 1)..];
            string[] values = value.Split(',');
            if (!values.Any(IsRelative))
            {
                segments.Add(segment);
                continue;
            }

            anchor ??= asOf();
            string rewritten = string.Join(',', values.Select(item => IsRelative(item) ? ResolveValue(name, item, anchor) : item));
            resolved.Add(new ResolvedParameter(name, value, rewritten));
            segments.Add($"{name}={rewritten}");
        }

        return anchor is null
            ? (request, [], null)
            : (request with { Query = string.Join('&', segments) }, resolved, Note(resolved.Count, anchor));
    }

    private static IssueItem Note(int count, AsOf asOf)
    {
        string day = asOf.Moment.Offset == TimeSpan.Zero
            ? "today as the asOf day in UTC"
            : $"today as the asOf day in its offset {asOf.Moment.ToString("zzz", CultureInfo.InvariantCulture)} (a date the search reads in UTC)";
        return new IssueItem(
            "information",
            Code,
            $"{count} {(count == 1 ? "parameter" : "parameters")} with relative dates were resolved against asOf {asOf.Instant} ({asOf.Source}) before the search ran: now as that instant in UTC, {day}. facts.resolvedParameters gives each value as written and as searched; facts.query is the query searched.",
            Location: null);
    }

    // A value written as a relative date, after its FHIR prefix. One that starts like one but is not is refused, not searched as text.
    private static bool IsRelative(string value) => Anchored().IsMatch(value);

    private static string ResolveValue(string name, string value, AsOf asOf)
    {
        Match match = Relative().Match(value);
        if (!match.Success)
        {
            throw Invalid(name, value, "write now or today, optionally less a whole number of h (now only), d, wk, mo or y: now-24h, today-2d, now-3mo, now-1y");
        }

        string prefix = match.Groups["prefix"].Value;
        bool now = match.Groups["anchor"].Value == "now";
        int amount = match.Groups["amount"].Success ? int.Parse(match.Groups["amount"].Value, CultureInfo.InvariantCulture) : 0;
        string unit = match.Groups["unit"].Value;
        if (!now && unit == "h")
        {
            throw Invalid(name, value, "hours count back from now, not from today: write now-24h, or today-1d for the day before");
        }

        try
        {
            return now
                ? prefix + FhirInstant.Format(Back(asOf.Moment, amount, unit).ToUniversalTime())
                : prefix + Back(DateOnly.FromDateTime(asOf.Moment.DateTime), amount, unit).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw Invalid(name, value, "it reaches back before the year 1");
        }
    }

    private static DateTimeOffset Back(DateTimeOffset moment, int amount, string unit) => unit switch
    {
        "h" => moment.AddHours(-amount),
        "d" => moment.AddDays(-amount),
        "wk" => moment.AddDays(-7.0 * amount),
        "mo" => moment.AddMonths(-amount),
        "y" => moment.AddYears(-amount),
        _ => moment,
    };

    private static DateOnly Back(DateOnly day, int amount, string unit) => unit switch
    {
        "d" => day.AddDays(-amount),
        "wk" => day.AddDays(-7 * amount),
        "mo" => day.AddMonths(-amount),
        "y" => day.AddYears(-amount),
        _ => day,
    };

    private static ToolRequestException Invalid(string name, string value, string remedy) =>
        ToolRequestException.Error("invalid-parameter", $"{name}={QueryEcho.Of(value)} is not a relative date the tool reads: {remedy}.");

    // A known date parameter of the type, or the last link of a chain whose type is given (subject:Patient.birthdate) or, when it is
    // not, a date parameter of some type; a modifier (:missing, :above) leaves the value to the query.
    private static bool IsDateParameter(ISearchParameterDefinitionManager definitions, string resourceType, string name)
    {
        string[] links = name.Split('.');
        if (name.StartsWith("_has:", StringComparison.Ordinal) || links.Any(link => !Link().IsMatch(link)) || links[^1].Contains(':', StringComparison.Ordinal))
        {
            return false;
        }

        string code = links[^1];
        if (links.Length == 1)
        {
            return IsDate(definitions, resourceType, code);
        }

        string previous = links[^2];
        int colon = previous.IndexOf(':', StringComparison.Ordinal);
        return colon >= 0
            ? IsDate(definitions, previous[(colon + 1)..], code)
            : definitions.AllSearchParameters.Any(parameter => parameter.Code == code && parameter.Type == SearchParamType.Date);
    }

    private static bool IsDate(ISearchParameterDefinitionManager definitions, string resourceType, string code) =>
        definitions.TryGetSearchParameter(resourceType, code, out SearchParameterInfo parameter) && parameter.Type == SearchParamType.Date;

    // Whether the query names now or today at all, so a query without either costs one scan.
    [GeneratedRegex(@"(now|today)", RegexOptions.CultureInvariant)]
    private static partial Regex Anchor();

    [GeneratedRegex(@"^(eq|ne|gt|lt|ge|le|sa|eb|ap)?(now|today)", RegexOptions.CultureInvariant)]
    private static partial Regex Anchored();

    [GeneratedRegex(@"^(?<prefix>eq|ne|gt|lt|ge|le|sa|eb|ap)?(?<anchor>now|today)(-(?<amount>\d{1,6})(?<unit>h|d|wk|mo|y))?$", RegexOptions.CultureInvariant)]
    private static partial Regex Relative();

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_\-]*(:[A-Za-z][A-Za-z0-9\-]*)?$", RegexOptions.CultureInvariant)]
    private static partial Regex Link();
}
