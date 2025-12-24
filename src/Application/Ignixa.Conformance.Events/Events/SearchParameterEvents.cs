using Ignixa.Conformance.Events.Models;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Conformance.Events.Events;

public record SearchParameterActivated(
    string Canonical,
    string Code,
    string ResourceType,
    string Expression,
    SearchParamType ParamType,
    string SourcePackage,
    OverrideInfo? Overrides,
    int SearchParamId,
    IReadOnlyList<string>? TargetResourceTypes,
    IReadOnlyList<SearchParameterComponentData>? Components,
    string? Name,
    string? Description);

public record SearchParameterComponentData(
    string DefinitionUrl,
    string? Expression);

public record SearchParameterReindexStarted(
    string Canonical,
    string Code,
    string ResourceType,
    string JobId,
    IReadOnlyList<string> AffectedResourceTypes);

public record SearchParameterReindexCompleted(
    string Canonical,
    string Code,
    string ResourceType,
    string JobId,
    long ResourcesIndexed,
    TimeSpan Duration);

public record SearchParameterReindexFailed(
    string Canonical,
    string Code,
    string ResourceType,
    string JobId,
    string ErrorMessage);

public record SearchParameterDeactivated(
    string Canonical,
    string Code,
    string ResourceType,
    string Reason);

public record SearchParameterDeleted(
    string Canonical,
    string Code,
    string ResourceType,
    string Reason);
