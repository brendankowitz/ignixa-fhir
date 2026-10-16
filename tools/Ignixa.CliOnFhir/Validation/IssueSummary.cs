// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Catalog;

namespace Ignixa.CliOnFhir.Validation;

/// <summary>
/// Groups the issues of validated resources by severity, profile, rule, constraint, ValueSet and path, counting issues and
/// resources and keeping the first three resources of each group as samples; and counts verdicts and unconcluded checks.
/// Resources must be added in scope order, which makes the samples, and so the whole summary, deterministic. Not thread-safe.
/// </summary>
internal sealed class IssueSummary
{
    public const int SamplesPerGroup = 3;

    private readonly Dictionary<ClassifiedIssue, Tally> _groups = [];
    private readonly SortedDictionary<string, Tally> _notChecked = new(StringComparer.Ordinal);
    private int _valid;
    private int _invalid;
    private int _unknown;
    private int _unreadable;

    public void Add(ResourceValidation validation)
    {
        ArgumentNullException.ThrowIfNull(validation);

        switch (validation.Verdict)
        {
            case ResourceValidation.Valid: _valid++; break;
            case ResourceValidation.Invalid: _invalid++; break;
            case ResourceValidation.Unknown: _unknown++; break;
            default: _unreadable++; return;
        }

        string reference = FhirIds.Reference(validation.Entry);
        foreach (ClassifiedIssue issue in validation.Issues)
        {
            Count(_groups.TryGetValue(issue, out Tally? group) ? group : _groups[issue] = new Tally(), validation.Entry, reference);
            if (ValidationRules.NotChecked.Contains(issue.Rule))
            {
                Count(_notChecked.TryGetValue(issue.Rule, out Tally? notChecked) ? notChecked : _notChecked[issue.Rule] = new Tally(), validation.Entry, reference);
            }
        }
    }

    public VerdictCounts Verdicts => new(_valid, _invalid, _unknown, _unreadable);

    public IReadOnlyList<NotCheckedCount> NotChecked => [.. _notChecked.Select(pair => new NotCheckedCount(pair.Key, pair.Value.Count, pair.Value.Resources.Count))];

    /// <summary>Resources with at least one check of <paramref name="rule"/> that did not conclude.</summary>
    public int ResourcesNotChecked(string rule) => _notChecked.TryGetValue(rule, out Tally? tally) ? tally.Resources.Count : 0;

    /// <summary>Every group: fatal, error, warning, information; then most issues first; then rule, path, profile and message.</summary>
    public IReadOnlyList<(IssueGroup Group, IReadOnlyList<CatalogEntry> Samples)> Groups() =>
    [
        .. _groups
            .OrderBy(pair => SeverityRank(pair.Key.Severity))
            .ThenByDescending(pair => pair.Value.Count)
            .ThenBy(pair => pair.Key.Rule, StringComparer.Ordinal)
            .ThenBy(pair => pair.Key.Constraint ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(pair => pair.Key.Path, StringComparer.Ordinal)
            .ThenBy(pair => pair.Key.Profile, StringComparer.Ordinal)
            .ThenBy(pair => pair.Key.ValueSet ?? string.Empty, StringComparer.Ordinal)
            .Select(pair => (
                new IssueGroup(
                    pair.Key.Severity,
                    pair.Key.Profile,
                    pair.Key.Rule,
                    pair.Key.Constraint,
                    pair.Key.ValueSet,
                    pair.Key.Path,
                    RuleDescriptions.Describe(pair.Key.Rule, pair.Key.Constraint),
                    pair.Value.Count,
                    pair.Value.Resources.Count,
                    [.. pair.Value.Samples.Select(FhirIds.Reference)]),
                (IReadOnlyList<CatalogEntry>)pair.Value.Samples)),
    ];

    private static void Count(Tally tally, CatalogEntry entry, string reference)
    {
        tally.Count++;
        if (tally.Resources.Add(reference) && tally.Samples.Count < SamplesPerGroup)
        {
            tally.Samples.Add(entry);
        }
    }

    private static int SeverityRank(string severity) => severity switch
    {
        "fatal" => 0,
        "error" => 1,
        "warning" => 2,
        _ => 3,
    };

    private sealed class Tally
    {
        public int Count { get; set; }

        public HashSet<string> Resources { get; } = new(StringComparer.Ordinal);

        public List<CatalogEntry> Samples { get; } = [];
    }
}
