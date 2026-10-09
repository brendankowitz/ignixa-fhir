using Ignixa.Application.Features.Conformance;
using Ignixa.Domain.Models;
using Ignixa.Conformance.Events.Models;

namespace Ignixa.Application.BackgroundOperations.Reindex;

public static class ReindexTargetResolver
{
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
                        affectedTypes);
            })
            .ToArray();

        var resourceTypes = targets.SelectMany(target => target.AffectedResourceTypes)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal)
            .ToArray();

        return new ReindexTargetResolution(targets, resourceTypes);
    }
}
