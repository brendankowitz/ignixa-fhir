// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.RegularExpressions;
using Ignixa.Abstractions;
using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.Contracts;

namespace Ignixa.CliOnFhir.Patients;

/// <summary>
/// The shape checks on the free-string values summaries copy that are bound to codes or dates: a status or a gender (a FHIR
/// <c>code</c>), a quantity comparator and a birth date. A value outside its shape could carry prose, so it is replaced by its
/// <see cref="FhirIds.Digest"/> and reported once per type as <see cref="InvalidCode"/>, without echoing it; the resource holds
/// it as written. Coded data (<c>system|code</c>, a coding display, a unit) is copied as written and not checked here.
/// For one call; not thread-safe.
/// </summary>
internal sealed partial class ValueShapes(FhirVersion version)
{
    public const string InvalidCode = "invalid-value";

    private static readonly HashSet<string> Genders = new(["male", "female", "other", "unknown"], StringComparer.Ordinal);

    private readonly SortedDictionary<string, SortedSet<string>> _invalid = new(StringComparer.Ordinal);

    /// <summary>A FHIR <c>code</c>: at most 64 characters, words of non-whitespace, non-control characters separated by single spaces.</summary>
    public string? Code(string resourceType, string element, string? value) => Check(resourceType, element, value, value is not null && value.Length <= 64 && CodeShape().IsMatch(value));

    /// <summary>One of the administrative genders, the same in every supported version.</summary>
    public string? Gender(string resourceType, string? value) => Check(resourceType, "gender", value, value is not null && Genders.Contains(value));

    /// <summary>A quantity comparator: <c>&lt;</c>, <c>&lt;=</c>, <c>&gt;=</c>, <c>&gt;</c>, and in R5 also <c>ad</c>.</summary>
    public string? Comparator(string resourceType, string? value) =>
        Check(resourceType, "comparator", value, value is "<" or "<=" or ">=" or ">" || (value == "ad" && version >= FhirVersion.R5));

    /// <summary>A FHIR <c>date</c> (year, year-month or full date).</summary>
    public string? Date(string resourceType, string element, string? value) => Check(resourceType, element, value, value is not null && DateShape().IsMatch(value));

    /// <summary>One <see cref="InvalidCode"/> warning per type with values that failed a check, naming the elements, in ordinal type order.</summary>
    public IEnumerable<IssueItem> Issues() => _invalid.Select(pair => new IssueItem(
        "warning",
        InvalidCode,
        $"Some {pair.Key} {string.Join(", ", pair.Value)} values are not shaped as their FHIR type (a code, a comparator or a date), so summaries show each as a digest ('~' and 16 hex digits); the resources hold them as written.",
        Location: null));

    private string? Check(string resourceType, string element, string? value, bool valid)
    {
        if (value is null || valid)
        {
            return value;
        }

        if (!_invalid.TryGetValue(resourceType, out SortedSet<string>? elements))
        {
            elements = new SortedSet<string>(StringComparer.Ordinal);
            _invalid.Add(resourceType, elements);
        }

        elements.Add(element);
        return FhirIds.Digest(value);
    }

    [GeneratedRegex("^[^\\s\\p{C}]+( [^\\s\\p{C}]+)*$")]
    private static partial Regex CodeShape();

    [GeneratedRegex("^[0-9]{4}(-(0[1-9]|1[0-2])(-(0[1-9]|[12][0-9]|3[01]))?)?$")]
    private static partial Regex DateShape();
}
