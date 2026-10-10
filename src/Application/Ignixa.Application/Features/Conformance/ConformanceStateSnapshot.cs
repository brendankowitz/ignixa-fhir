using Ignixa.Conformance.Events.Models;

namespace Ignixa.Application.Features.Conformance;

/// <summary>
/// Detached, immutable-by-convention projection used to build conformance consumers off-lock.
/// </summary>
public sealed class ConformanceStateSnapshot : IConformanceStateView
{
    private readonly IReadOnlyDictionary<(string ResourceType, string Code), ActiveSearchParameter> _searchParameters;
    private readonly IReadOnlyList<ActiveSearchParameter> _searchParameterActivations;
    private readonly IReadOnlyDictionary<string, string> _storageCanonicals;

    internal ConformanceStateSnapshot(
        IReadOnlyDictionary<(string ResourceType, string Code), ActiveSearchParameter> searchParameters,
        IReadOnlyList<ActiveSearchParameter> searchParameterActivations,
        IReadOnlyDictionary<string, string> storageCanonicals,
        bool isInitialized)
    {
        var clones = searchParameterActivations.ToDictionary(parameter => parameter, parameter => parameter.Clone());
        _searchParameterActivations = searchParameterActivations.Select(parameter => clones[parameter]).ToArray();
        _searchParameters = searchParameters.ToDictionary(pair => pair.Key, pair => clones[pair.Value]);
        _storageCanonicals = storageCanonicals.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        IsInitialized = isInitialized;
    }

    public bool IsInitialized { get; }

    public IReadOnlyDictionary<(string ResourceType, string Code), ActiveSearchParameter> AllSearchParameters =>
        _searchParameters;

    public ActiveSearchParameter? FindExtractedByCanonical(string canonical) =>
        _searchParameters.Values.LastOrDefault(parameter => parameter.Canonical == canonical);

    public bool TryGetSearchParameterStorageCanonical(string canonical, out string storageCanonical) =>
        _storageCanonicals.TryGetValue(canonical, out storageCanonical!);
}
