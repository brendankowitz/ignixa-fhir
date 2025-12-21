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
    int SearchParamId);

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
