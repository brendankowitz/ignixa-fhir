// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.FhirPath.Analysis;
using Ignixa.FhirPath.Visitors;
using Ignixa.Search.Indexing;
using Ignixa.Search.Models;

namespace Ignixa.Search.Tests.Definition;

/// <summary>
/// The census oracle for <c>KnownInconsistentComposites</c>: checks that each component of a composite selects elements
/// the indexer files as the kind the component declares.
/// </summary>
/// <remarks>
/// The indexer types a component's values from the elements its expression selects (the declared kind where a converter
/// exists for the element type, otherwise the kind inferred from it), while a query binds the declared kind, so a
/// component whose expression selects something else indexes values no query can match. A component whose elements
/// have several types (a value[x]) is consistent when at least one of them indexes as the declared kind.
/// </remarks>
internal static class CompositeComponentConsistency
{
    /// <summary>
    /// The verdict for <paramref name="composite"/>: <see cref="CompositeConsistencyVerdict.Inconsistent"/> with the
    /// component at fault, <see cref="CompositeConsistencyVerdict.Unresolved"/> with the index of a component whose definition
    /// did not resolve, <see cref="CompositeConsistencyVerdict.Unknown"/> when the analyzer cannot type a component's
    /// expression (never to be read as consistent), or <see cref="CompositeConsistencyVerdict.Consistent"/>.
    /// </summary>
    public static (CompositeConsistencyVerdict Verdict, string? Detail) Check(
        SearchParameterInfo composite,
        FhirPathAnalyzer analyzer,
        IReadOnlySet<(string FhirType, Type SearchValueType)> converters)
    {
        ArgumentNullException.ThrowIfNull(composite);
        ArgumentNullException.ThrowIfNull(analyzer);
        ArgumentNullException.ThrowIfNull(converters);

        for (int index = 0; index < composite.Component.Count; index++)
        {
            SearchParameterComponentInfo component = composite.Component[index];
            if (component.ResolvedSearchParameter is not { } declared || string.IsNullOrWhiteSpace(component.Expression))
            {
                return (CompositeConsistencyVerdict.Unresolved, index.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            foreach (string resourceType in composite.BaseResourceTypes)
            {
                string expression = $"({composite.Expression}).{component.Expression}";
                FhirPathTypeSet types = analyzer.InferTypes(expression, resourceType);
                if (types.HasUnknown || types.Types.Count == 0)
                {
                    return (CompositeConsistencyVerdict.Unknown, $"'{expression}' on {resourceType} could not be typed ({types})");
                }

                // An element type with neither a converter to the declared kind nor an inferable kind is skipped by the
                // indexer, so it contributes nothing.
                Type declaredValueType = ElementSearchIndexer.GetSearchValueTypeForSearchParamType(declared.Type);
                List<string> kinds = [.. types.Types.Select(type => converters.Contains((type.TypeName, declaredValueType))
                    ? declared.Type.ToString()
                    : ElementSearchIndexer.InferSearchParamTypeFromFhirType(type.TypeName)?.ToString() ?? $"nothing ({type.TypeName})")];
                if (!kinds.Contains(declared.Type.ToString()))
                {
                    return (CompositeConsistencyVerdict.Inconsistent,
                        $"component {index} ('{component.Expression}') is declared {declared.Type} by '{declared.Code}' but indexes as {string.Join(" | ", kinds.Distinct())} on {resourceType}");
                }
            }
        }

        return (CompositeConsistencyVerdict.Consistent, null);
    }
}
