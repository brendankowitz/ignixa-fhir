// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Reading;

namespace Ignixa.CliOnFhir.DeId;

/// <summary>
/// What a run did with each resource, counted per type and per method, with an issue for each resource not written, up to
/// <see cref="MaxFailureIssues"/>; the rest are counted in <c>omitted</c>. Not thread-safe.
/// </summary>
internal sealed class DeIdTally
{
    /// <summary>The most resources not written that are each reported by an issue.</summary>
    public const int MaxFailureIssues = 20;

    private readonly Dictionary<string, (int Written, int Failed)> _types = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, long> _operations = new(StringComparer.Ordinal);
    private readonly List<IssueItem> _failures = [];
    private readonly List<EvidenceItem> _cited = [];

    public int Written { get; private set; }

    public int Failed { get; private set; }

    public int ReferencesResolved { get; private set; }

    /// <summary>Elements changed per method, as <c>Ignixa.DeId</c> counted them on the resources written.</summary>
    public IReadOnlyDictionary<string, long> Operations => _operations;

    /// <summary>One issue per resource not written, the first <see cref="MaxFailureIssues"/>.</summary>
    public IReadOnlyList<IssueItem> FailureIssues => _failures;

    /// <summary>The resources <see cref="FailureIssues"/> are about.</summary>
    public IReadOnlyList<EvidenceItem> Cited => _cited;

    /// <summary>Resources not written that no issue reports.</summary>
    public int Uncited => Failed - _failures.Count;

    public (int Written, int Failed) Of(string resourceType) => _types.GetValueOrDefault(resourceType);

    public void Wrote(CatalogEntry entry, IReadOnlyDictionary<string, int> operations, int referencesResolved)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(operations);

        (int written, int failed) = Of(entry.ResourceType);
        _types[entry.ResourceType] = (written + 1, failed);
        Written++;
        ReferencesResolved += referencesResolved;
        foreach ((string method, int count) in operations)
        {
            string name = DeIdConfiguration.MethodName(method);
            _operations[name] = _operations.GetValueOrDefault(name) + count;
        }
    }

    /// <summary>Counts <paramref name="entry"/> as not written, for the reason <paramref name="issue"/> gives.</summary>
    public void Fail(CatalogEntry entry, IssueItem issue)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(issue);

        (int written, int failed) = Of(entry.ResourceType);
        _types[entry.ResourceType] = (written, failed + 1);
        Failed++;
        if (_failures.Count < MaxFailureIssues)
        {
            _failures.Add(issue);
            _cited.Add(new EvidenceItem(ResourceReader.Describe(entry), entry.FilePath, entry.Line, entry.Offset));
        }
    }
}
