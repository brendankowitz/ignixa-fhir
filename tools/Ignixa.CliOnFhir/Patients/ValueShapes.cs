// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.RegularExpressions;
using Ignixa.Abstractions;
using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Terminology;
using Ignixa.CliOnFhir.Terminology.Tools;

namespace Ignixa.CliOnFhir.Patients;

/// <summary>
/// The shape checks on the free-string values summaries copy that are bound to codes or dates: a status or a gender (a FHIR
/// <c>code</c>), a quantity comparator and a birth date. A value outside its shape could carry prose, so it is replaced by its
/// <see cref="FhirIds.Digest"/> and reported once per type as <see cref="InvalidCode"/>, without echoing it; the resource holds
/// it as written. With terminology loaded (<paramref name="membership"/>), a status, gender or comparator that passes its shape
/// but is not in its element's required-binding ValueSet is also named by digest, with an <see cref="InvalidCode"/> issue naming
/// the element and the ValueSet, never the value; one whose membership the loaded terminology cannot decide is shown as
/// written and counted in a <see cref="NotCheckedCode"/> note. Without terminology only the shape is checked, which
/// <see cref="Membership"/> says. Coded data (<c>system|code</c>, a coding display, a unit) is copied as written and not
/// checked here. For one call; not thread-safe.
/// </summary>
internal sealed partial class ValueShapes(FhirVersion version, BindingMembership? membership = null)
{
    public const string InvalidCode = "invalid-value";

    public const string NotCheckedCode = StoreTerminologyService.NotCheckedRule;

    /// <summary><see cref="Membership"/> when terminology was loaded and statuses, genders and comparators were checked against their bindings.</summary>
    public const string MembershipChecked = "checked";

    /// <summary><see cref="Membership"/> when no terminology was loaded, so only their shape was checked.</summary>
    public const string MembershipNotChecked = "not-checked";

    private static readonly HashSet<string> Genders = new(["male", "female", "other", "unknown"], StringComparer.Ordinal);

    private readonly SortedDictionary<string, SortedSet<string>> _invalid = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, string> _outside = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, string> _undecided = new(StringComparer.Ordinal);

    /// <summary>Whether statuses, genders and comparators were checked against their required bindings, or only their shape.</summary>
    public string Membership => membership is null ? MembershipNotChecked : MembershipChecked;

    /// <summary>
    /// A FHIR <c>code</c>: at most 64 characters, words of non-whitespace, non-control characters separated by single spaces;
    /// with terminology, a <c>status</c> also in its required binding (<see cref="ClinicalRules.StatusElement"/>).
    /// </summary>
    public string? Code(string resourceType, string element, string? value) =>
        Member(resourceType, element, resourceType, element == "status" ? ClinicalRules.StatusElement.GetValueOrDefault(resourceType, element) : element, Check(resourceType, element, value, value is not null && value.Length <= 64 && CodeShape().IsMatch(value)), value);

    /// <summary>One of the administrative genders, the same in every supported version; with terminology, also in Patient.gender's binding.</summary>
    public string? Gender(string resourceType, string? value) =>
        Member(resourceType, "gender", resourceType, "gender", Check(resourceType, "gender", value, value is not null && Genders.Contains(value)), value);

    /// <summary>A quantity comparator: <c>&lt;</c>, <c>&lt;=</c>, <c>&gt;=</c>, <c>&gt;</c>, and in R5 also <c>ad</c>; with terminology, also in Quantity.comparator's binding.</summary>
    public string? Comparator(string resourceType, string? value) =>
        Member(resourceType, "comparator", "Quantity", "comparator", Check(resourceType, "comparator", value, value is "<" or "<=" or ">=" or ">" || (value == "ad" && version >= FhirVersion.R5)), value);

    /// <summary>A FHIR <c>date</c> (year, year-month or full date).</summary>
    public string? Date(string resourceType, string element, string? value) => Check(resourceType, element, value, value is not null && DateShape().IsMatch(value));

    /// <summary>One <see cref="InvalidCode"/> warning per type with values that failed a check, naming the elements, in ordinal type order.</summary>
    public IEnumerable<IssueItem> Issues()
    {
        IEnumerable<IssueItem> shapes = _invalid.Select(pair => new IssueItem(
            "warning",
            InvalidCode,
            $"Some {pair.Key} {string.Join(", ", pair.Value)} values are not shaped as their FHIR type (a code, a comparator or a date), so summaries show each as a digest ('~' and 16 hex digits); the resources hold them as written.",
            Location: null));
        IEnumerable<IssueItem> outside = _outside.Select(pair => new IssueItem(
            "warning",
            InvalidCode,
            $"Some {pair.Key} values are not in the required ValueSet {pair.Value}, so summaries show each as a digest ('~' and 16 hex digits); the resources hold them as written.",
            Location: null));
        IEnumerable<IssueItem> undecided = _undecided.Select(pair => new IssueItem(
            "information",
            NotCheckedCode,
            $"Whether the {pair.Key} values are in the required ValueSet {pair.Value} could not be decided with the loaded terminology, so they are shown as written and only their shape was checked.",
            Location: null));
        return [.. shapes, .. outside, .. undecided];
    }

    /// <summary>
    /// <paramref name="shown"/> (the shape-checked value) checked against the required binding of
    /// <paramref name="definitionType"/>.<paramref name="bindingElement"/>: replaced by the digest of <paramref name="value"/>
    /// when it is not a member, as written otherwise.
    /// </summary>
    private string? Member(string resourceType, string element, string definitionType, string bindingElement, string? shown, string? value)
    {
        if (membership is null || value is null || shown != value || membership.Check(definitionType, bindingElement, value) is not { } answer)
        {
            return shown;
        }

        string name = $"{resourceType} {element}";
        string valueSet = TerminologyNames.Canonical(TerminologyStore.SplitCanonical(answer.ValueSet).Url, TerminologyStore.SplitCanonical(answer.ValueSet).Version);
        switch (answer.Result)
        {
            case TermValidate.Invalid:
                _outside.TryAdd(name, valueSet);
                return FhirIds.Digest(value);
            case TermValidate.Valid:
                return value;
            default:
                _undecided.TryAdd(name, valueSet);
                return value;
        }
    }

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
