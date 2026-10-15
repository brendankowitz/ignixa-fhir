// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Ignixa.FhirPath.Analysis;
using Ignixa.FhirPath.Visitors;
using Ignixa.Search.Indexing;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Definition;

/// <summary>
/// Checks that each component of a composite search parameter selects elements of the kind its definition declares.
/// </summary>
/// <remarks>
/// The indexer types a component's values from the elements its expression selects, while a query binds the component
/// with the declared type, so a component whose expression selects something else (a <c>code</c> under a reference
/// component) indexes values no query can match: the search answers "nothing" for every value. Such a composite is
/// reported here so a backend can refuse it instead. A component whose elements have several types (a value[x])
/// is consistent when at least one of them indexes as the declared kind; the others simply never match.
/// </remarks>
internal static class CompositeComponentConsistency
{
    /// <summary>
    /// Why <paramref name="composite"/> cannot be searched consistently, or null when every component's expression selects
    /// elements the indexer turns into values of the component's declared type (or their types cannot be inferred, which
    /// is not evidence of a fault).
    /// </summary>
    /// <param name="composite">The composite search parameter, with its components resolved.</param>
    /// <param name="analyzer">Infers the element types a component expression selects.</param>
    /// <param name="converters">The (FHIR element type, search value type) pairs the indexer can convert.</param>
    public static string? Inconsistency(SearchParameterInfo composite, FhirPathAnalyzer analyzer, IReadOnlySet<(string FhirType, Type SearchValueType)> converters)
    {
        ArgumentNullException.ThrowIfNull(composite);
        ArgumentNullException.ThrowIfNull(analyzer);
        ArgumentNullException.ThrowIfNull(converters);

        for (int index = 0; index < composite.Component.Count; index++)
        {
            SearchParameterComponentInfo component = composite.Component[index];
            if (component.ResolvedSearchParameter is not { } declared || string.IsNullOrWhiteSpace(component.Expression))
            {
                continue;
            }

            foreach (string resourceType in composite.BaseResourceTypes)
            {
                if (Indexed(analyzer, converters, $"({composite.Expression}).{component.Expression}", resourceType, declared.Type) is { Count: > 0 } kinds
                    && !kinds.Contains(declared.Type))
                {
                    return $"component {index} ('{component.Expression}') is declared {declared.Type} by '{declared.Code}' but selects "
                        + $"{string.Join(" | ", kinds)} on {resourceType}, so its indexed values can never match a query";
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The kinds the indexer files the expression's elements as: the declared kind where a converter turns the element type
    /// into it, otherwise the kind inferred from the element type, as <c>ElementSearchIndexer</c> does. Null when an element
    /// type cannot be inferred.
    /// </summary>
    private static HashSet<SearchParamType>? Indexed(
        FhirPathAnalyzer analyzer,
        IReadOnlySet<(string FhirType, Type SearchValueType)> converters,
        string expression,
        string resourceType,
        SearchParamType declared)
    {
        FhirPathTypeSet types;
        try
        {
            types = analyzer.InferTypes(expression, resourceType);
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException or ArgumentException or NotSupportedException)
        {
            // An expression the analyzer cannot type is not evidence of an inconsistency.
            return null;
        }

        if (types.HasUnknown || types.Types.Count == 0)
        {
            return null;
        }

        Type declaredValueType = ElementSearchIndexer.GetSearchValueTypeForSearchParamType(declared);
        HashSet<SearchParamType?> kinds = [.. types.Types.Select(type => converters.Contains((type.TypeName, declaredValueType))
            ? declared
            : ElementSearchIndexer.InferSearchParamTypeFromFhirType(type.TypeName))];
        return kinds.Contains(null) ? null : [.. kinds.OfType<SearchParamType>()];
    }
}
