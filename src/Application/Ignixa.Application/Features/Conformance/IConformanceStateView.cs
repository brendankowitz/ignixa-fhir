using Ignixa.Conformance.Events.Models;

namespace Ignixa.Application.Features.Conformance;

public interface IConformanceStateView
{
    bool IsInitialized { get; }
    IReadOnlyDictionary<(string ResourceType, string Code), ActiveSearchParameter> AllSearchParameters { get; }
    ActiveSearchParameter? FindExtractedByCanonical(string canonical);
    bool HasStagedSearchParameterReplacement(ActiveSearchParameter parameter);
    bool TryGetSearchParameterStorageCanonical(string canonical, out string storageCanonical);
}
