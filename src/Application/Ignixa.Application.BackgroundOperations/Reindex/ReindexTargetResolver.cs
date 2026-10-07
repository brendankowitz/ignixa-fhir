using Ignixa.Application.Features.Conformance;
using Ignixa.Conformance.Events.Models;

namespace Ignixa.Application.BackgroundOperations.Reindex;

public static class ReindexTargetResolver
{
    public static ReindexTargetResolution Resolve(
        IEnumerable<ActiveSearchParameter> parameters,
        IReadOnlyCollection<string> concreteResourceTypes,
        IReadOnlyCollection<string>? targetResourceTypes)
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
                IEnumerable<string> affected = parameter.TargetResourceTypes is { Count: > 0 }
                    ? parameter.TargetResourceTypes
                    : concrete.Contains(parameter.ResourceType, StringComparer.OrdinalIgnoreCase)
                        ? [parameter.ResourceType]
                        : concrete;
                if (requested is not null)
                {
                    affected = affected.Where(requested.Contains);
                }

                var affectedTypes = affected
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Order(StringComparer.Ordinal)
                    .ToArray();
                return affectedTypes.Length == 0
                    ? null
                    : new ReindexTarget(
                        parameter.Canonical,
                        parameter.Code,
                        parameter.ResourceType,
                        parameter.SearchParamId,
                        parameter.ActivationEventId,
                        affectedTypes);
            })
            .OfType<ReindexTarget>()
            .ToArray();

        var resourceTypes = requested is null
            ? targets.SelectMany(target => target.AffectedResourceTypes)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.Ordinal)
                .ToArray()
            : requested.Order(StringComparer.Ordinal).ToArray();

        return new ReindexTargetResolution(targets, resourceTypes);
    }
}
