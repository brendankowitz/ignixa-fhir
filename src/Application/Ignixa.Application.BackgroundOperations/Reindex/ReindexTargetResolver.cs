using Ignixa.Application.Features.Conformance;
using Ignixa.Application.Features.Search;
using Ignixa.Conformance.Events.Abstractions;
using Ignixa.Conformance.Events.Models;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Constants;
using Ignixa.Domain.Models;
using Ignixa.Serialization;

namespace Ignixa.Application.BackgroundOperations.Reindex;

/// <summary>
/// Resolves what a new reindex job would cover. The projection is read only under the activation lock,
/// after catching up with the event store, so the plan is consistent with the event it targets.
/// </summary>
public sealed class ReindexTargetResolver(
    ITenantConfigurationStore tenantConfigurationStore,
    IFhirVersionContext fhirVersionContext,
    ConformanceState conformanceState,
    ISourceEventStore eventStore)
{
    public async Task<ReindexTargetPlan> ResolveAsync(CancellationToken cancellationToken)
    {
        var tenants = (await tenantConfigurationStore.GetAllTenantsAsync(cancellationToken))
            .Where(tenant =>
                tenant.IsActive &&
                tenant.TenantId != SystemConstants.SystemPartitionId)
            .OrderBy(tenant => tenant.TenantId)
            .ToArray();

        var concreteResourceTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var domainResourceTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tenant in tenants)
        {
            var version = FhirSpecificationExtensions.FromVersionString(tenant.FhirVersion);
            var schema = fhirVersionContext.GetSchemaProvider(version, tenant.TenantId);
            foreach (var resourceType in schema.ResourceTypeNames)
            {
                var definition = schema.GetTypeDefinition(resourceType);
                if (definition?.Info.IsAbstract != false)
                {
                    continue;
                }

                concreteResourceTypes.Add(resourceType);
                if (definition.Children.Any(child =>
                    child.Info.Name.Equals("text", StringComparison.OrdinalIgnoreCase)))
                {
                    domainResourceTypes.Add(resourceType);
                }
            }
        }

        using (await conformanceState.AcquireActivationLockAsync(cancellationToken))
        {
            await conformanceState.CatchUpWhileActivationLockHeldAsync(eventStore, cancellationToken);
            var resolution = Resolve(
                conformanceState.AllSearchParameters.Values.ToArray(),
                concreteResourceTypes,
                new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Resource"] = concreteResourceTypes,
                    ["DomainResource"] = domainResourceTypes
                });
            return new ReindexTargetPlan(
                tenants.Select(tenant => tenant.TenantId).ToArray(),
                conformanceState.LastProcessedEventId,
                resolution);
        }
    }

    public static ReindexTargetResolution Resolve(
        IEnumerable<ActiveSearchParameter> parameters,
        IReadOnlyCollection<string> concreteResourceTypes,
        IReadOnlyDictionary<string, IReadOnlyCollection<string>>? abstractResourceTypeExpansions = null)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(concreteResourceTypes);

        var concrete = concreteResourceTypes
            .Order(StringComparer.Ordinal)
            .ToArray();
        var targets = parameters
            .Where(parameter => parameter.Status == SearchParameterStatus.Pending)
            .Select(parameter =>
            {
                IEnumerable<string> affected = concrete.Contains(
                    parameter.ResourceType,
                    StringComparer.OrdinalIgnoreCase)
                    ? [parameter.ResourceType]
                    : abstractResourceTypeExpansions?.TryGetValue(
                        parameter.ResourceType,
                        out var expansion) == true
                        ? expansion
                        : parameter.ResourceType.Equals("Resource", StringComparison.OrdinalIgnoreCase)
                            ? concrete
                            : [];
                var affectedTypes = affected
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Order(StringComparer.Ordinal)
                    .ToArray();
                return new ReindexParameterDefinition(
                        parameter.Canonical,
                        parameter.Code,
                        parameter.ResourceType,
                        parameter.SearchParamId,
                        parameter.ActivationEventId,
                        affectedTypes,
                        parameter.OverridesCanonical);
            })
            .ToArray();

        var resourceTypes = targets.SelectMany(target => target.AffectedResourceTypes)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal)
            .ToArray();

        return new ReindexTargetResolution(targets, resourceTypes);
    }
}
