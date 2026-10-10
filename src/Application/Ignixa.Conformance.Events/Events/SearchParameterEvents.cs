using Ignixa.Conformance.Events.Models;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Conformance.Events.Events;

/// <param name="FhirVersion">
/// The FHIR version ("4.0", "5.0", ...) whose tenants this definition applies to. Null on events appended
/// before versions were recorded; such a definition applies to every version, which is how it was always
/// projected.
/// </param>
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
    string? Description,
    string? FhirVersion = null);

public record SearchParameterComponentData(
    string DefinitionUrl,
    string? Expression);

public record SearchParameterReindexStarted(
    string Canonical,
    string Code,
    string ResourceType,
    string JobId,
    IReadOnlyList<string> AffectedResourceTypes,
    long? ActivationEventId = null);

public record SearchParameterReindexCompleted(
    string Canonical,
    string Code,
    string ResourceType,
    string JobId,
    long ResourcesIndexed,
    TimeSpan Duration,
    long? ActivationEventId = null);

public record SearchParameterReindexFailed(
    string Canonical,
    string Code,
    string ResourceType,
    string JobId,
    string ErrorMessage,
    long? ActivationEventId = null);

public record SearchParameterTransitionCommitted(
    int SearchParamId,
    IReadOnlyList<long> ActivationEventIds,
    IReadOnlyList<long> DeactivationEventIds);

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
