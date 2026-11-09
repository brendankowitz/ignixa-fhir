#nullable enable

using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Definition;

/// <summary>
/// Describes the compartment membership parameters a search cannot use. Compartment and <c>$everything</c>
/// queries resolve membership through the searchable definitions, which drop a parameter that is pending
/// reindex or hidden by a transition; the results are then incomplete, and the caller must be told so the
/// way an ignored query parameter is reported.
/// </summary>
public static class CompartmentMembershipIssues
{
    /// <param name="compartments">The compartment definitions of the tenant's FHIR version.</param>
    /// <param name="definitions">
    /// The tenant's full definitions, hidden parameters included; a searchable view would not return them.
    /// </param>
    /// <param name="compartmentType">The compartment searched.</param>
    /// <param name="resourceTypes">The member types the search is limited to, or null for every member type.</param>
    public static IReadOnlyList<IssueComponent> Describe(
        ICompartmentDefinitionManager compartments,
        ISearchParameterDefinitionManager definitions,
        CompartmentType compartmentType,
        IReadOnlySet<string>? resourceTypes)
    {
        ArgumentNullException.ThrowIfNull(compartments);
        ArgumentNullException.ThrowIfNull(definitions);

        if (!compartments.TryGetResourceTypes(compartmentType, out var memberTypes))
        {
            return [];
        }

        var issues = new List<IssueComponent>();
        foreach (var resourceType in memberTypes.Order(StringComparer.Ordinal))
        {
            if (resourceTypes?.Contains(resourceType) == false ||
                !compartments.TryGetSearchParams(resourceType, compartmentType, out var codes))
            {
                continue;
            }

            foreach (var code in codes.Order(StringComparer.Ordinal))
            {
                if (definitions.TryGetSearchParameter(resourceType, code, out var parameter) &&
                    parameter.Type == SearchParamType.Reference &&
                    !parameter.IsSearchable)
                {
                    issues.Add(Describe(resourceType, parameter));
                }
            }
        }

        return issues;
    }

    private static IssueComponent Describe(string resourceType, SearchParameterInfo parameter) =>
        new(
            Severity: "warning",
            Code: "incomplete",
            Diagnostics: parameter.IsHiddenByTransition
                ? $"Results may be incomplete: compartment membership through '{resourceType}.{parameter.Code}' is being redefined and was ignored."
                : $"Results may be incomplete: compartment membership through '{resourceType}.{parameter.Code}' is pending reindex.");
}
