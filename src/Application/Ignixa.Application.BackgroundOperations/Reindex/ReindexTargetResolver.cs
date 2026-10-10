using Ignixa.Abstractions;
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
/// <remarks>
/// A Pending parameter recorded for one FHIR version is resolved against that version's schema and sends the
/// job only to that version's tenants; one recorded without a version applies to every tenant, as it always
/// did, and is resolved against the union of their schemas.
/// </remarks>
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

        var schemas = tenants
            .GroupBy(tenant => FhirSpecificationExtensions.FromVersionString(tenant.FhirVersion))
            .ToDictionary(group => group.Key, group => DescribeSchema(group.Key, group));

        using (await conformanceState.AcquireActivationLockAsync(cancellationToken))
        {
            await conformanceState.CatchUpWhileActivationLockHeldAsync(eventStore, cancellationToken);
            var parameters = conformanceState.AllSearchParameters.Values.ToArray();
            var targets = new List<ReindexParameterDefinition>();
            foreach (var (version, schema) in schemas)
            {
                targets.AddRange(Resolve(
                    parameters.Where(parameter => parameter.FhirVersion is not null && parameter.AppliesTo(version)),
                    schema.ConcreteResourceTypes,
                    schema.Expansions).Targets);
            }

            targets.AddRange(Resolve(
                parameters.Where(parameter => parameter.FhirVersion is null),
                schemas.Values.SelectMany(schema => schema.ConcreteResourceTypes).ToHashSet(StringComparer.OrdinalIgnoreCase),
                Union(schemas.Values.Select(schema => schema.Expansions))).Targets);

            var targetedTenants = targets.Count == 0
                ? tenants
                : tenants.Where(tenant => targets.Any(target => target.AppliesToTenant(tenant.FhirVersion))).ToArray();
            var resourceTypes = targets.SelectMany(target => target.AffectedResourceTypes)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.Ordinal)
                .ToArray();
            return new ReindexTargetPlan(
                targetedTenants.Select(tenant => tenant.TenantId).ToArray(),
                conformanceState.LastProcessedEventId,
                new ReindexTargetResolution(targets, resourceTypes));
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
                        parameter.OverridesCanonical,
                        parameter.FhirVersion);
            })
            .ToArray();

        var resourceTypes = targets.SelectMany(target => target.AffectedResourceTypes)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal)
            .ToArray();

        return new ReindexTargetResolution(targets, resourceTypes);
    }

    // The concrete types of one version, across its tenants' schemas (a tenant's packages may add types).
    private VersionSchema DescribeSchema(FhirVersion version, IEnumerable<TenantConfiguration> tenants)
    {
        var concreteResourceTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var domainResourceTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tenant in tenants)
        {
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

        return new VersionSchema(
            concreteResourceTypes,
            new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["Resource"] = concreteResourceTypes,
                ["DomainResource"] = domainResourceTypes
            });
    }

    private static Dictionary<string, IReadOnlyCollection<string>> Union(
        IEnumerable<IReadOnlyDictionary<string, IReadOnlyCollection<string>>> expansions)
    {
        var union = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (abstractType, concreteTypes) in expansions.SelectMany(expansion => expansion))
        {
            if (!union.TryGetValue(abstractType, out var set))
            {
                set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                union[abstractType] = set;
            }

            set.UnionWith(concreteTypes);
        }

        return union.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyCollection<string>)pair.Value,
            StringComparer.OrdinalIgnoreCase);
    }

    private sealed record VersionSchema(
        IReadOnlyCollection<string> ConcreteResourceTypes,
        IReadOnlyDictionary<string, IReadOnlyCollection<string>> Expansions);
}
