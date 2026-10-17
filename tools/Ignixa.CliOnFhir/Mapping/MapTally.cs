// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Reading;

namespace Ignixa.CliOnFhir.Mapping;

/// <summary>
/// What a run did with each resource: the first transformed ones kept for the preview, one issue per resource it failed on up to
/// <see cref="MaxFailureIssues"/> (the rest counted in <c>omitted</c>), and the resources skipped per type. Not thread-safe.
/// </summary>
/// <param name="keep">How many transformed resources to keep for the preview.</param>
internal sealed class MapTally(int keep)
{
    /// <summary>The most resources a run failed on that are each reported by an issue.</summary>
    public const int MaxFailureIssues = 20;

    private readonly List<(CatalogEntry Source, JsonObject Result)> _kept = [];
    private readonly List<IssueItem> _failures = [];
    private readonly List<EvidenceItem> _cited = [];
    private readonly Dictionary<string, int> _skipped = new(StringComparer.Ordinal);

    public int Transformed { get; private set; }

    /// <summary>Transformed resources that hold nothing but their <c>resourceType</c>.</summary>
    public int Empty { get; private set; }

    public int Failed { get; private set; }

    public int Skipped => _skipped.Values.Sum();

    /// <summary>The first transformed resources, with the resource each came from.</summary>
    public IReadOnlyList<(CatalogEntry Source, JsonObject Result)> Kept => _kept;

    /// <summary>One issue per resource the run failed on, the first <see cref="MaxFailureIssues"/>.</summary>
    public IReadOnlyList<IssueItem> FailureIssues => _failures;

    /// <summary>The resources <see cref="FailureIssues"/> are about.</summary>
    public IReadOnlyList<EvidenceItem> Cited => _cited;

    /// <summary>Resources failed on that no issue reports.</summary>
    public int Uncited => Failed - _failures.Count;

    /// <summary>Resources skipped, per type, in the order first met.</summary>
    public IReadOnlyDictionary<string, int> SkippedByType => _skipped;

    /// <summary>Counts a transformed resource, keeping it for the preview while there is room.</summary>
    public void Transform(CatalogEntry source, JsonObject result)
    {
        ArgumentNullException.ThrowIfNull(result);

        Transformed++;
        if (result.Count == 1)
        {
            Empty++;
        }

        if (_kept.Count < keep)
        {
            _kept.Add((source, result));
        }
    }

    public void Fail(CatalogEntry entry, IssueItem issue)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(issue);

        Failed++;
        if (_failures.Count < MaxFailureIssues)
        {
            _failures.Add(issue);
            _cited.Add(new EvidenceItem(ResourceReader.Describe(entry), entry.FilePath, entry.Line, entry.Offset));
        }
    }

    public void Skip(string resourceType) => _skipped[resourceType] = _skipped.GetValueOrDefault(resourceType) + 1;
}
