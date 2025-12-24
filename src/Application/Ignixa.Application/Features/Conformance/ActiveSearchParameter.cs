using Ignixa.Conformance.Events.Events;
using Ignixa.Conformance.Events.Models;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Application.Features.Conformance;

public class ActiveSearchParameter
{
    public required int SearchParamId { get; init; }
    public required string Canonical { get; init; }
    public required string Code { get; init; }
    public required string ResourceType { get; init; }
    public required string Expression { get; init; }
    public required SearchParamType ParamType { get; init; }
    public required string SourcePackage { get; init; }
    public string? OverridesCanonical { get; init; }
    public IReadOnlyList<string>? TargetResourceTypes { get; init; }
    public IReadOnlyList<SearchParameterComponentData>? Components { get; init; }
    public string? Name { get; init; }
    public string? Description { get; init; }

    public SearchParameterStatus Status { get; set; }
    public string? ReindexJobId { get; set; }
}
