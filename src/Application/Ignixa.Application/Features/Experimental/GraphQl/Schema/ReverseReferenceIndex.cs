// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Application.Features.Experimental.GraphQl.Schema;

/// <summary>
/// For each resource type, the resource types that can reference it through a reference search parameter.
/// </summary>
/// <remarks>
/// Drives the instance-level reverse fields (<c>Patient.ObservationList</c>, <c>Patient.ObservationConnection</c>).
/// A reverse field's <c>_reference</c> argument names a search parameter on the source type, so a source type
/// with no reference parameter that can point at the target has no value that could ever match. Emitting a
/// field for every pair made the schema quadratic in the resource-type count: about 2.3 million HotChocolate
/// arguments and 560 MB of live heap for R4 alone.
/// </remarks>
public sealed class ReverseReferenceIndex
{
    // A reference parameter with no declared target, or targeting an abstract base, can point at any resource.
    private static readonly HashSet<string> AnyResourceTargets = new(StringComparer.Ordinal) { "Resource", "DomainResource" };

    private readonly Dictionary<string, List<string>> _sourcesByTarget;

    private ReverseReferenceIndex(Dictionary<string, List<string>> sourcesByTarget)
    {
        _sourcesByTarget = sourcesByTarget;
    }

    /// <summary>
    /// Builds the index over <paramref name="resourceTypes"/>. Source types are listed in
    /// <paramref name="resourceTypes"/> order so the generated schema's field order stays stable.
    /// </summary>
    public static ReverseReferenceIndex Build(
        IReadOnlyList<string> resourceTypes,
        Func<string, IEnumerable<SearchParameterInfo>> getSearchParameters)
    {
        ArgumentNullException.ThrowIfNull(resourceTypes);
        ArgumentNullException.ThrowIfNull(getSearchParameters);

        var sourcesByTarget = new Dictionary<string, List<string>>(resourceTypes.Count, StringComparer.Ordinal);
        foreach (var resourceType in resourceTypes)
            sourcesByTarget[resourceType] = [];

        foreach (var sourceType in resourceTypes)
        {
            foreach (var targetType in CollectTargets(getSearchParameters(sourceType), resourceTypes))
            {
                if (sourcesByTarget.TryGetValue(targetType, out var sources))
                    sources.Add(sourceType);
            }
        }

        return new ReverseReferenceIndex(sourcesByTarget);
    }

    /// <summary>
    /// The resource types that have at least one reference search parameter able to target
    /// <paramref name="targetType"/>.
    /// </summary>
    public IReadOnlyList<string> GetReferencingTypes(string targetType) =>
        _sourcesByTarget.TryGetValue(targetType, out var sources) ? sources : [];

    private static HashSet<string> CollectTargets(
        IEnumerable<SearchParameterInfo> searchParameters,
        IReadOnlyList<string> allResourceTypes)
    {
        var targets = new HashSet<string>(StringComparer.Ordinal);
        foreach (var parameter in searchParameters.Where(p => p.Type == SearchParamType.Reference))
        {
            if (TargetsAnyResource(parameter))
                return new HashSet<string>(allResourceTypes, StringComparer.Ordinal);

            targets.UnionWith(parameter.TargetResourceTypes);
        }

        return targets;
    }

    private static bool TargetsAnyResource(SearchParameterInfo parameter) =>
        parameter.TargetResourceTypes.Count == 0 || parameter.TargetResourceTypes.Any(AnyResourceTargets.Contains);
}
