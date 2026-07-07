// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Validation.Abstractions;

namespace Ignixa.Validation.Checks;

/// <summary>
/// Enforces FHIR slicing on a sliced element (Full tier). Buckets each element of the sliced array
/// to a slice by evaluating that slice's discriminators, then enforces per-slice cardinality and the
/// closed / openAtEnd rules. The FHIRPath engine is used only for the per-element discriminator
/// navigation; assignment, accounting, and diagnostics are imperative so messages can name the slice
/// and the offending element index.
/// </summary>
/// <remarks>
/// Supports <c>value</c>, <c>pattern</c> (scalar equivalence), <c>exists</c>, and <c>type</c>
/// discriminators. <c>profile</c> discriminators require <c>conformsTo()</c> and are deferred: a
/// slicing that uses one (or whose slices could not be resolved to determinate match values) is
/// skipped with an informational issue rather than risk falsely rejecting a valid resource.
/// </remarks>
public sealed class SlicingCheck : IValidationCheck
{
    private const string ClosedRule = "CLOSED";
    private const string OpenAtEndRule = "OPENATEND";

    private readonly string _slicedName;
    private readonly SlicingMetadata _metadata;
    private readonly bool _isClosed;
    private readonly bool _isOpenAtEnd;
    private readonly bool _deferred;

    /// <summary>
    /// Initializes a new instance of the <see cref="SlicingCheck"/> class.
    /// </summary>
    /// <param name="slicedName">The element name owning the sliced array (e.g. <c>extension</c>).</param>
    /// <param name="metadata">The slicing metadata (discriminators, rules, ordered, slices).</param>
    public SlicingCheck(string slicedName, SlicingMetadata metadata)
    {
        _slicedName = slicedName ?? throw new ArgumentNullException(nameof(slicedName));
        _metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));

        var rules = metadata.Rules?.ToUpperInvariant().Replace("-", string.Empty, StringComparison.Ordinal) ?? "OPEN";
        _isClosed = rules == ClosedRule;
        _isOpenAtEnd = rules == OpenAtEndRule;
        _deferred = ComputeDeferred(metadata);
    }

    /// <summary>Gets the sliced element name this check enforces.</summary>
    public string SlicedName => _slicedName;

    /// <summary>Gets a value indicating whether this slicing was deferred (profile/indeterminate).</summary>
    public bool IsDeferred => _deferred;

    /// <inheritdoc/>
    public ValidationResult Validate(IElement element, ValidationSettings settings, ValidationState state)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(settings);

        // Slicing is a Full-tier check. ValidationSchema already gates profile checks to Full, but
        // guard defensively for direct callers.
        if (settings.Depth < ValidationDepth.Full)
        {
            return ValidationResult.Success();
        }

        var location = element.Location ?? _slicedName;

        if (_deferred)
        {
            return new ValidationResult(isValid: true, issues: new[]
            {
                new ValidationIssue(
                    IssueSeverity.Information,
                    "slicing-deferred",
                    $"{location}.{_slicedName}",
                    $"Slicing on '{_slicedName}' was not enforced: it uses a profile discriminator or a slice whose discriminator value could not be resolved (conformsTo() is not yet supported).")
            });
        }

        var candidates = element.Children(_slicedName);
        var context = BuildContext(state);

        var assignments = AssignSlices(candidates, context, out var issues);
        AccountCardinality(assignments, candidates.Count, location, issues);

        return issues.Count == 0
            ? ValidationResult.Success()
            : new ValidationResult(isValid: !issues.Any(i => i.Severity is IssueSeverity.Error or IssueSeverity.Fatal), issues: issues);
    }

    /// <summary>
    /// Buckets each candidate to the first slice whose discriminators all match (first match wins),
    /// reporting unmatched candidates according to the closed / openAtEnd rules and ordering.
    /// </summary>
    private Dictionary<string, List<int>> AssignSlices(
        IReadOnlyList<IElement> candidates,
        EvaluationContext context,
        out List<ValidationIssue> issues)
    {
        issues = new List<ValidationIssue>();
        var buckets = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        foreach (var slice in _metadata.Slices)
        {
            buckets[slice.Name] = new List<int>();
        }

        var lastMatchedSliceIndex = -1;
        var lastMatchedCandidate = -1;
        var unmatched = new List<int>();

        for (var i = 0; i < candidates.Count; i++)
        {
            var matchedSlice = -1;
            for (var s = 0; s < _metadata.Slices.Count; s++)
            {
                if (SliceMatches(candidates[i], _metadata.Slices[s], context))
                {
                    matchedSlice = s;
                    break;
                }
            }

            if (matchedSlice < 0)
            {
                unmatched.Add(i);
                continue;
            }

            if (_metadata.Ordered && matchedSlice < lastMatchedSliceIndex)
            {
                issues.Add(new ValidationIssue(
                    IssueSeverity.Error,
                    "slicing-out-of-order",
                    CandidateLocation(candidates[i], i),
                    $"'{_slicedName}[{i}]' matches slice '{_metadata.Slices[matchedSlice].Name}' out of order in an ordered slicing."));
            }

            buckets[_metadata.Slices[matchedSlice].Name].Add(i);
            lastMatchedSliceIndex = Math.Max(lastMatchedSliceIndex, matchedSlice);
            lastMatchedCandidate = i;
        }

        ReportUnmatched(candidates, unmatched, lastMatchedCandidate, issues);
        return buckets;
    }

    private void ReportUnmatched(
        IReadOnlyList<IElement> candidates,
        List<int> unmatched,
        int lastMatchedCandidate,
        List<ValidationIssue> issues)
    {
        foreach (var i in unmatched)
        {
            if (_isClosed)
            {
                issues.Add(new ValidationIssue(
                    IssueSeverity.Error,
                    "slicing-unmatched",
                    CandidateLocation(candidates[i], i),
                    $"'{_slicedName}[{i}]' does not match any slice, which is not allowed in a closed slicing."));
            }
            else if (_isOpenAtEnd && i < lastMatchedCandidate)
            {
                issues.Add(new ValidationIssue(
                    IssueSeverity.Error,
                    "slicing-unmatched",
                    CandidateLocation(candidates[i], i),
                    $"'{_slicedName}[{i}]' is additional content appearing before a matched slice, which is not allowed in an openAtEnd slicing."));
            }
        }
    }

    /// <summary>Enforces per-slice min/max cardinality over the assigned buckets.</summary>
    private void AccountCardinality(
        Dictionary<string, List<int>> assignments,
        int candidateCount,
        string location,
        List<ValidationIssue> issues)
    {
        foreach (var slice in _metadata.Slices)
        {
            var members = assignments[slice.Name];
            if (members.Count < slice.Min)
            {
                issues.Add(new ValidationIssue(
                    IssueSeverity.Error,
                    "slicing-cardinality",
                    $"{location}.{_slicedName}",
                    $"Slice '{slice.Name}' on '{_slicedName}' requires at least {slice.Min} occurrence(s), but found {members.Count}."));
            }

            if (slice.Max is { } max && members.Count > max)
            {
                var offendingIndex = members[max];
                issues.Add(new ValidationIssue(
                    IssueSeverity.Error,
                    "slicing-cardinality",
                    $"{location}.{_slicedName}[{offendingIndex}]",
                    $"Slice '{slice.Name}' on '{_slicedName}' allows at most {max} occurrence(s), but found {members.Count}."));
            }
        }
    }

    private bool SliceMatches(IElement candidate, SliceDefinition slice, EvaluationContext context)
    {
        if (slice.Match.Count == 0)
        {
            return false;
        }

        foreach (var match in slice.Match)
        {
            if (!DiscriminatorMatches(candidate, match, context))
            {
                return false;
            }
        }

        return true;
    }

    private static bool DiscriminatorMatches(IElement candidate, SliceDiscriminatorValue match, EvaluationContext context)
    {
        var targets = SelectPath(candidate, match.Path, context);

        return match.Type switch
        {
            DiscriminatorType.Exists => targets.Count > 0,
            DiscriminatorType.Type => match.ExpectedValue is { } typeCode
                && targets.Any(t => string.Equals(t.InstanceType, typeCode, StringComparison.Ordinal)),
            _ => match.ExpectedValue is { } expected
                && targets.Any(t => string.Equals(ScalarString(t), expected, StringComparison.Ordinal)),
        };
    }

    private static IReadOnlyList<IElement> SelectPath(IElement candidate, string path, EvaluationContext context)
    {
        if (string.IsNullOrEmpty(path) || path == "$this")
        {
            return new[] { candidate };
        }

        try
        {
            return candidate.Select(path, context).ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Array.Empty<IElement>();
        }
    }

    private static string? ScalarString(IElement element) => element.Value?.ToString();

    private static EvaluationContext BuildContext(ValidationState state)
    {
        var scope = state.Scope;
        if (scope.Resource is null)
        {
            return new FhirEvaluationContext();
        }

        return new FhirEvaluationContext
        {
            Resource = scope.Resource,
            RootResource = scope.RootResource,
            ElementResolver = scope.Resolver,
        };
    }

    private string CandidateLocation(IElement candidate, int index)
        => string.IsNullOrEmpty(candidate.Location) ? $"{_slicedName}[{index}]" : candidate.Location;

    /// <summary>
    /// A slicing is deferred (skip-with-info, never enforced) when any discriminator is a
    /// <c>profile</c> discriminator, or when any slice lacks a determinate match value — either
    /// case means slice assignment cannot be trusted, so enforcing closed/cardinality rules would
    /// risk falsely rejecting a valid resource.
    /// </summary>
    private static bool ComputeDeferred(SlicingMetadata metadata)
    {
        if (metadata.Discriminators.Any(d => d.Type == DiscriminatorType.Profile))
        {
            return true;
        }

        foreach (var slice in metadata.Slices)
        {
            if (!slice.Match.Any(IsDeterminate))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsDeterminate(SliceDiscriminatorValue match) => match.Type switch
    {
        DiscriminatorType.Exists => true,
        DiscriminatorType.Profile => false,
        _ => match.ExpectedValue is not null,
    };
}
