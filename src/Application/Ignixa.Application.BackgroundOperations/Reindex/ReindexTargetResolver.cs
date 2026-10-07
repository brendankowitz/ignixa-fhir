using Ignixa.Application.Features.Conformance;
using Ignixa.Conformance.Events.Models;

namespace Ignixa.Application.BackgroundOperations.Reindex;

public static class ReindexTargetResolver
{
    public static ReindexTargetResolution Resolve(
        IEnumerable<ActiveSearchParameter> parameters,
        IReadOnlyCollection<string> concreteResourceTypes,
        IReadOnlyCollection<string>? targetResourceTypes,
        IReadOnlyDictionary<string, IReadOnlyCollection<string>>? abstractResourceTypeExpansions = null)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(concreteResourceTypes);

        var concrete = concreteResourceTypes
            .Order(StringComparer.Ordinal)
            .ToArray();
        var requested = targetResourceTypes?
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
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
                var scheduledTypes = requested is null
                    ? affectedTypes
                    : affectedTypes.Where(requested.Contains).ToArray();
                return new ReindexTarget(
                        parameter.Canonical,
                        parameter.Code,
                        parameter.ResourceType,
                        parameter.SearchParamId,
                        parameter.ActivationEventId,
                        affectedTypes)
                    {
                        ScheduledResourceTypes = scheduledTypes
                    };
            })
            .ToArray();

        var resourceTypes = requested is null
            ? targets.SelectMany(target => target.ScheduledResourceTypes)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.Ordinal)
                .ToArray()
            : requested.Order(StringComparer.Ordinal).ToArray();

        return new ReindexTargetResolution(targets, resourceTypes);
    }
}
