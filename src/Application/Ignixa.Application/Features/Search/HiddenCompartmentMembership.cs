using Ignixa.Application.Infrastructure;
using Ignixa.Search.Definition;
using Ignixa.Search.Models;
using Ignixa.Serialization;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Application.Features.Search;

/// <summary>
/// Describes, for compartment searches and <c>$everything</c>, the membership parameters the request's tenant
/// cannot search yet, so an incomplete result carries a warning (spec §4.2).
/// </summary>
public static class HiddenCompartmentMembership
{
    public static IReadOnlyList<IssueComponent> Describe(
        IFhirVersionContext versionContext,
        IFhirRequestContext context,
        CompartmentType compartmentType,
        IReadOnlySet<string>? resourceTypes)
    {
        var tenant = context.TenantConfiguration
            ?? throw new InvalidOperationException("Tenant configuration not available");
        var fhirVersion = FhirSpecificationExtensions.FromVersionString(tenant.FhirVersion);
        return CompartmentMembershipIssues.Describe(
            versionContext.GetCompartmentDefinitionManager(fhirVersion),
            versionContext.GetSearchParameterDefinitionManager(fhirVersion, context.TenantId),
            compartmentType,
            resourceTypes);
    }
}
