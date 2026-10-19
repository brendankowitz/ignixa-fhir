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
/// another). In UTC it is written as a date; in another offset as that day's bounds, instants in UTC (<c>getoday</c> is <c>ge</c>
/// its first instant, <c>letoday</c> <c>lt</c> the next day's; <c>today</c> or <c>eqtoday</c> alone, a value within the day:
/// <c>sa</c> the second before it and <c>eb</c> the next day's start), since a FHIR date is read as a UTC day; that bare form
/// under <c>_has</c> or a chain, <c>ne</c>, <c>ap</c>, and <c>today</c> without a prefix in an OR list or a composite, cannot be
/// stated so and are refused.
/// Months and years are calendar arithmetic: a day the month lacks becomes its last (2025-03-31 less a month is 2025-02-28), as
/// FHIRPath adds calendar durations. Only a value of a known date parameter is read: of the type, at the end of a chain, at the
/// end of a <c>_has</c> chain (<c>_has:Observation:patient:date</c>), and the date components of a composite
/// (<c>code-value-date=http://loinc.org|8867-4$genow-7d</c>); any other value, and every value of a parameter with a modifier, is
/// the query's.
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
            if (equals <= 0 || Shape(definitions, request.ResourceType, segment[..equals]) is not { } shape)
            {
                segments.Add(segment);
                continue;
            }

            string name = segment[..equals];
            string value = segment[(equals + 1)..];
            string[] values = value.Split(',');
            if (!values.Any(item => shape.Pieces(item).Any(piece => piece.Date && IsRelative(piece.Text))))
            {
                segments.Add(segment);
                continue;
            }

            anchor ??= asOf();
            AsOf at = anchor;
            string rewritten = values.Length == 1 && shape.Components is null && TodayRange(name, values[0], at) is { } day
                ? Within(name, values[0], day, at)
                : string.Join(',', values.Select(item => shape.Rewrite(item, piece => ResolveValue(name, piece, at, listed: values.Length > 1 || shape.Components is not null))));
            resolved.Add(new ResolvedParameter(name, value, rewritten));
            segments.Add($"{name}={rewritten}");
        }

        return anchor is null
            ? (request, [], null)
            : (request with { Query = string.Join('&', segments) }, resolved, Note(resolved.Count, anchor));
    }

    /// <summary>The most resolved parameters facts list; the rest are counted in <c>omitted</c>.</summary>
    public const int MaxListed = 10;

    /// <summary>
    /// <paramref name="resolved"/> as facts list it, bounded whatever the query: at most <see cref="MaxListed"/>, each name and value
    /// as an issue repeats a value (<see cref="QueryEcho.Value"/>: a long one keeps its start, length and digest); and the omitted
    /// items that say what was left out, the original query's cut echo among them.
    /// </summary>
    public static (IReadOnlyList<ResolvedParameter> Shown, IReadOnlyList<OmittedItem> Omitted) ForFacts(string originalQuery, IReadOnlyList<ResolvedParameter> resolved)
    {
        ArgumentNullException.ThrowIfNull(originalQuery);
        ArgumentNullException.ThrowIfNull(resolved);

        List<OmittedItem> omitted = [];
        if (QueryEcho.IsCut(originalQuery))
        {
            omitted.Add(QueryEcho.Cut(originalQuery, "facts.originalQuery"));
        }

        if (resolved.Count > MaxListed)
        {
            omitted.Add(new OmittedItem($"{resolved.Count - MaxListed} more resolvedParameters (facts.query is the query searched)", "limit"));
        }

        return ([.. resolved.Take(MaxListed).Select(parameter => new ResolvedParameter(QueryEcho.Value(parameter.Parameter), QueryEcho.Value(parameter.Original), QueryEcho.Value(parameter.Resolved)))], omitted);
    }

    private static IssueItem Note(int count, AsOf asOf)
    {
        string day = asOf.Moment.Offset == TimeSpan.Zero
            ? "today as the asOf day in UTC"
            : $"today as the asOf day in its offset {asOf.Moment.ToString("zzz", CultureInfo.InvariantCulture)}, written as that day's bounds in UTC";
        return new IssueItem(
            "information",
            Code,
            $"{(count == 1 ? "1 parameter with relative dates was" : $"{count} parameters with relative dates were")} resolved against asOf {asOf.Instant} ({asOf.Source}): now as that instant in UTC, {day}. facts.resolvedParameters gives each value as written and as resolved; facts.query is the resolved query.",
            Location: null);
    }

    // A value written as a relative date, after its FHIR prefix. One that starts like one but is not is refused, not searched as text.
    private static bool IsRelative(string value) => Anchored().IsMatch(value);

    // One relative value resolved. listed: the value is one of an OR list or a composite's component, so a today that needs both
    // bounds (no prefix, or eq) cannot become two parameters.
    private static string ResolveValue(string name, string value, AsOf asOf, bool listed)
    {
        (string prefix, bool now, int amount, string unit) = Read(name, value);
        if (now)
        {
            return prefix + FhirInstant.Format(Back(asOf.Moment, amount, unit, name, value).ToUniversalTime());
        }

        DateOnly day = Back(DateOnly.FromDateTime(asOf.Moment.DateTime), amount, unit, name, value);
        if (asOf.Moment.Offset == TimeSpan.Zero)
        {
            return prefix + day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        (string start, string end) = Bounds(day, asOf.Moment.Offset);
        return prefix switch
        {
            "ge" => "ge" + start,
            "lt" => "lt" + start,
            "gt" => "ge" + end,
            "le" => "lt" + end,
            "eb" => "eb" + start,
            "sa" => "sa" + LastSecond(day, asOf.Moment.Offset),
            _ => throw Invalid(
                name,
                value,
                $"in asOf's offset {asOf.Moment.ToString("zzz", CultureInfo.InvariantCulture)} that day is ge{start} to lt{end} in UTC, which {(listed ? "one value of a list or a composite" : "this prefix")} cannot state: "
                + $"write {name}=sa{SecondBefore(day, asOf.Moment.Offset)}&{name}=eb{end} (within the day) or {name}=ge{start}&{name}=lt{end} (overlapping it), use ge, gt, le or lt, or give an asOf in UTC"),
        };
    }

    // A today alone in its parameter, without a prefix or with eq, in an offset other than UTC: the day it names; null otherwise.
    private static DateOnly? TodayRange(string name, string value, AsOf asOf)
    {
        if (asOf.Moment.Offset == TimeSpan.Zero || !IsRelative(value))
        {
            return null;
        }

        (string prefix, bool now, int amount, string unit) = Read(name, value);
        return now || prefix is not ("" or "eq") ? null : Back(DateOnly.FromDateTime(asOf.Moment.DateTime), amount, unit, name, value);
    }

    // A bare or eq today in an offset, as what a FHIR date means: a value within the day, from its first instant to the next day's
    // (sa the second before the day, eb the next day's start), two clauses on the parameter. Under _has or a chain the two clauses
    // would hold independently, each perhaps of another resource, so it is refused there.
    private static string Within(string name, string value, DateOnly day, AsOf asOf)
    {
        (string start, string end) = Bounds(day, asOf.Moment.Offset);
        if (name.StartsWith("_has:", StringComparison.Ordinal) || name.Contains('.', StringComparison.Ordinal))
        {
            throw Invalid(
                name,
                value,
                $"in asOf's offset {asOf.Moment.ToString("zzz", CultureInfo.InvariantCulture)} that day is {start} to {end} in UTC, which under _has or a chain would be two independent clauses, each perhaps met by another resource: "
                + "give an asOf in UTC, or explicit ge and lt bounds");
        }

        return $"sa{SecondBefore(day, asOf.Moment.Offset)}&{name}=eb{end}";
    }

    // The second before day's first instant in offset, as a UTC instant: a value that starts after it starts within or after the day.
    private static string SecondBefore(DateOnly day, TimeSpan offset) =>
        FhirInstant.Format(new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), offset).AddSeconds(-1).ToUniversalTime());

    // The first instant of day in offset, and of the next day, as UTC instants.
    private static (string Start, string End) Bounds(DateOnly day, TimeSpan offset)
    {
        DateTimeOffset start = new(day.ToDateTime(TimeOnly.MinValue), offset);
        return (FhirInstant.Format(start.ToUniversalTime()), FhirInstant.Format(start.AddDays(1).ToUniversalTime()));
    }

    // The day's last second, as a UTC instant: a value that starts after it starts after the day.
    private static string LastSecond(DateOnly day, TimeSpan offset) =>
        FhirInstant.Format(new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), offset).AddDays(1).AddSeconds(-1).ToUniversalTime());

    private static (string Prefix, bool Now, int Amount, string Unit) Read(string name, string value)
    {
        Match match = Relative().Match(value);
        if (!match.Success)
        {
            throw Invalid(name, value, "write now or today, optionally less a whole number of h (now only), d, wk, mo or y: now-24h, today-2d, now-3mo, now-1y");
        }

        bool now = match.Groups["anchor"].Value == "now";
        string unit = match.Groups["unit"].Value;
        if (!now && unit == "h")
        {
            throw Invalid(name, value, "hours count back from now, not from today: write now-24h, or today-1d for the day before");
        }

        int amount = match.Groups["amount"].Success ? int.Parse(match.Groups["amount"].Value, CultureInfo.InvariantCulture) : 0;
        return (match.Groups["prefix"].Value, now, amount, unit);
    }

    private static DateTimeOffset Back(DateTimeOffset moment, int amount, string unit, string name, string value)
    {
        try
        {
            return Back(moment, amount, unit);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw Invalid(name, value, "it reaches back before the year 1");
        }
    }

    private static DateOnly Back(DateOnly day, int amount, string unit, string name, string value)
    {
        try
        {
            return Back(day, amount, unit);
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

    // How a parameter's values hold dates: a known date parameter of the type, or the last link of a chain whose type is given
    // (subject:Patient.birthdate) or, when it is not, a date parameter of some type; the terminal parameter of a _has chain, on the
    // type it names; a composite of the type (or of a typed chain's last link) with a date component. Null when its values hold
    // none; a modifier (:missing, :above) leaves the value to the query.
    private static DateShape? Shape(ISearchParameterDefinitionManager definitions, string resourceType, string name)
    {
        if (name.StartsWith("_has:", StringComparison.Ordinal))
        {
            // _has:Type:reference:name, where name is a parameter of Type (or another _has).
            string[] parts = name.Split(':', 4);
            return parts.Length == 4 && Link().IsMatch(parts[1]) && Link().IsMatch(parts[2]) ? Shape(definitions, parts[1], parts[3]) : null;
        }

        string[] links = name.Split('.');
        if (links.Any(link => !Link().IsMatch(link)) || links[^1].Contains(':', StringComparison.Ordinal))
        {
            return null;
        }

        string code = links[^1];
        if (links.Length == 1)
        {
            return Of(definitions, resourceType, code);
        }

        string previous = links[^2];
        int colon = previous.IndexOf(':', StringComparison.Ordinal);
        return colon >= 0
            ? Of(definitions, previous[(colon + 1)..], code)
            : definitions.AllSearchParameters.Any(parameter => parameter.Code == code && parameter.Type == SearchParamType.Date) ? DateShape.Date : null;
    }

    private static DateShape? Of(ISearchParameterDefinitionManager definitions, string resourceType, string code)
    {
        if (!definitions.TryGetSearchParameter(resourceType, code, out SearchParameterInfo parameter))
        {
            return null;
        }

        if (parameter.Type == SearchParamType.Date)
        {
            return DateShape.Date;
        }

        if (parameter.Type != SearchParamType.Composite || parameter.Component is not { Count: > 0 } components)
        {
            return null;
        }

        bool[] dates = [.. components.Select(component => IsDate(definitions, component))];
        return dates.Any(date => date) ? new DateShape(dates) : null;
    }

    private static bool IsDate(ISearchParameterDefinitionManager definitions, SearchParameterComponentInfo component)
    {
        SearchParameterInfo? resolved = component.ResolvedSearchParameter;
        if (resolved is null && component.DefinitionUrl is { } url && definitions.TryGetSearchParameter(url, out SearchParameterInfo found))
        {
            resolved = found;
        }

        return resolved?.Type == SearchParamType.Date;
    }

    // Which parts of a value are dates: the whole value, or (a composite) the components Components marks, split on an unescaped '$'.
    private sealed record DateShape(IReadOnlyList<bool>? Components)
    {
        public static DateShape Date { get; } = new((IReadOnlyList<bool>?)null);

        public IEnumerable<(string Text, bool Date)> Pieces(string value) => Components is null
            ? [(value, true)]
            : ComponentSeparator().Split(value).Select((piece, index) => (piece, index < Components.Count && Components[index]));

        public string Rewrite(string value, Func<string, string> resolve) =>
            string.Join('$', Pieces(value).Select(piece => piece.Date && IsRelative(piece.Text) ? resolve(piece.Text) : piece.Text));
    }

    // Whether the query names now or today at all, so a query without either costs one scan.
    [GeneratedRegex(@"(now|today)", RegexOptions.CultureInvariant)]
    private static partial Regex Anchor();

    [GeneratedRegex(@"^(eq|ne|gt|lt|ge|le|sa|eb|ap)?(now|today)", RegexOptions.CultureInvariant)]
    private static partial Regex Anchored();

    [GeneratedRegex(@"^(?<prefix>eq|ne|gt|lt|ge|le|sa|eb|ap)?(?<anchor>now|today)(-(?<amount>\d{1,6})(?<unit>h|d|wk|mo|y))?$", RegexOptions.CultureInvariant)]
    private static partial Regex Relative();

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_\-]*(:[A-Za-z][A-Za-z0-9\-]*)?$", RegexOptions.CultureInvariant)]
    private static partial Regex Link();

    [GeneratedRegex(@"(?<!\\)\$", RegexOptions.CultureInvariant)]
    private static partial Regex ComponentSeparator();
}
