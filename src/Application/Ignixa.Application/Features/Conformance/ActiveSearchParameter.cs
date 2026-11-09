using Ignixa.Abstractions;
using Ignixa.Conformance.Events.Events;
using Ignixa.Conformance.Events.Models;
using Ignixa.Search.Models;
using Ignixa.Serialization;
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

    /// <summary>
    /// The FHIR version this definition was activated for, or null for a definition recorded before versions
    /// were carried, which applies to every version.
    /// </summary>
    public string? FhirVersion { get; init; }

    public string? OverridesCanonical { get; init; }
    public IReadOnlyList<string>? TargetResourceTypes { get; init; }
    public IReadOnlyList<SearchParameterComponentData>? Components { get; init; }
    public string? Name { get; init; }
    public string? Description { get; init; }
    public VectorSearchConfig? VectorConfig { get; init; }

    /// <summary>
    /// True when the package's <c>vector-search-config</c> extension failed to parse
    /// (<see cref="VectorConfig"/> is then null even though the extension was present). Carried
    /// through so <see cref="Ignixa.Application.Features.Search.CompositeSearchParameterDefinitionManager"/>
    /// can mark the resulting Core <c>SearchParameterInfo.IsSupported</c> false, consistent with the
    /// IElement/runtime-registration path's handling of the same failure.
    /// </summary>
    public bool HasInvalidVectorConfig { get; init; }

    public long ActivationEventId { get; init; }
    public long? DeactivationEventId { get; set; }
    public long? PreviousActivationEventId { get; init; }
    public bool IsAvailable { get; set; } = true;
    public SearchParameterStatus Status { get; set; }
    public string? ReindexJobId { get; set; }

    /// <summary>
    /// The event that made this definition Pending: its activation, or the transition that committed it from
    /// Staged. A failed reindex returns it to Pending without moving this, so automatic reindexing does not
    /// retry a finished job's own targets.
    /// </summary>
    public long PendingSinceEventId { get; set; }

    /// <summary>
    /// Whether this definition belongs in the projection of a tenant on <paramref name="fhirVersion"/>.
    /// </summary>
    public bool AppliesTo(FhirVersion fhirVersion) =>
        FhirVersion is null || FhirSpecificationExtensions.FromVersionString(FhirVersion) == fhirVersion;

    /// <summary>
    /// Whether this definition and one recorded for <paramref name="fhirVersion"/> can own the same code:
    /// an unversioned definition applies everywhere, otherwise the versions must match.
    /// </summary>
    public bool SharesVersionWith(string? fhirVersion) =>
        FhirVersion is null ||
        fhirVersion is null ||
        FhirSpecificationExtensions.FromVersionString(FhirVersion) == FhirSpecificationExtensions.FromVersionString(fhirVersion);

    internal ActiveSearchParameter Clone() =>
        CreateClone(
            ActivationEventId,
            DeactivationEventId,
            IsAvailable,
            Status,
            ReindexJobId);

    internal ActiveSearchParameter CloneForRestoration(long eventId) =>
        CreateClone(
            eventId,
            deactivationEventId: null,
            isAvailable: true,
            SearchParameterStatus.Staged,
            reindexJobId: null);

    private ActiveSearchParameter CreateClone(
        long activationEventId,
        long? deactivationEventId,
        bool isAvailable,
        SearchParameterStatus status,
        string? reindexJobId) =>
        new()
        {
            SearchParamId = SearchParamId,
            Canonical = Canonical,
            Code = Code,
            ResourceType = ResourceType,
            Expression = Expression,
            ParamType = ParamType,
            SourcePackage = SourcePackage,
            FhirVersion = FhirVersion,
            OverridesCanonical = OverridesCanonical,
            TargetResourceTypes = TargetResourceTypes?.ToArray(),
            Components = Components?.ToArray(),
            Name = Name,
            Description = Description,
            VectorConfig = VectorConfig,
            HasInvalidVectorConfig = HasInvalidVectorConfig,
            ActivationEventId = activationEventId,
            DeactivationEventId = deactivationEventId,
            PreviousActivationEventId = PreviousActivationEventId,
            IsAvailable = isAvailable,
            Status = status,
            ReindexJobId = reindexJobId,
            PendingSinceEventId = PendingSinceEventId
        };
}
