#nullable enable

using Ignixa.Search.Definition;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Expressions.Parsers;

/// <summary>
/// The reference parameters an <c>_include=*</c> or <c>_revinclude=*</c> follows, shared by both expression
/// parsers: only those searchable on the referencing type, so a parameter that is pending reindex or being
/// redefined on that type is not followed.
/// </summary>
internal static class WildcardIncludeParameters
{
    /// <param name="allResourceTypes">Every resource type: a reverse or iterative wildcard is seeded by resources
    /// of any type, so any type's references can be followed.</param>
    public static IEnumerable<WildcardIncludeParameter> Resolve(
        ISearchParameterDefinitionManager definitions,
        IEnumerable<string> allResourceTypes,
        string[] resourceTypes,
        string sourceResourceType,
        bool isReversed,
        bool iterate)
    {
        IEnumerable<string> sourceResourceTypes = sourceResourceType != "*" ? [sourceResourceType]
            : isReversed || iterate ? allResourceTypes
            : resourceTypes;
        return sourceResourceTypes
            .SelectMany(resourceType => definitions.TryGetSearchParameters(resourceType, out var parameters)
                ? parameters
                    .Where(parameter =>
                        parameter.Type == SearchParamType.Reference &&
                        parameter.IsSearchable &&
                        !parameter.IsHiddenByTransition &&
                        parameter.Url is not null)
                    .Select(parameter => new WildcardIncludeParameter(resourceType, parameter))
                : [])
            .DistinctBy(include => (include.ResourceType, include.Parameter.Url));
    }
}
