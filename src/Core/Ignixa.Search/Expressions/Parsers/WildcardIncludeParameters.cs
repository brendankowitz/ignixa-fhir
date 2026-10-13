#nullable enable

using Ignixa.Search.Definition;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Expressions.Parsers;

/// <summary>
/// The reference parameters an <c>_include=*</c> or <c>_revinclude=*</c> follows, shared by both expression
/// parsers: only searchable ones, so a parameter that is pending reindex or being redefined is not followed.
/// </summary>
internal static class WildcardIncludeParameters
{
    public static IEnumerable<SearchParameterInfo> Resolve(
        ISearchParameterDefinitionManager definitions,
        string[] resourceTypes,
        string sourceResourceType,
        bool isReversed)
    {
        IEnumerable<SearchParameterInfo> candidates = sourceResourceType switch
        {
            "*" when isReversed => definitions.AllSearchParameters,
            "*" => resourceTypes.SelectMany(definitions.GetSearchParameters),
            _ => definitions.GetSearchParameters(sourceResourceType),
        };

        return candidates
            .Where(parameter =>
                parameter.Type == SearchParamType.Reference &&
                parameter.IsSearchable &&
                !parameter.IsHiddenByTransition &&
                parameter.Url is not null)
            .DistinctBy(parameter => parameter.Url);
    }
}
