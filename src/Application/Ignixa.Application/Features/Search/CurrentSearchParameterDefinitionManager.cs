using Ignixa.Abstractions;
using Ignixa.Search.Definition;
using Ignixa.Search.Models;

namespace Ignixa.Application.Features.Search;

/// <summary>
/// Stable manager reference that forwards every operation to the currently published definition snapshot.
/// </summary>
internal sealed class CurrentSearchParameterDefinitionManager(
    Func<ISearchParameterDefinitionManager> getCurrent) : ISearchParameterDefinitionManager
{
    private ISearchParameterDefinitionManager Current => getCurrent();

    public IEnumerable<SearchParameterInfo> AllSearchParameters => Current.AllSearchParameters;
    public IReadOnlyDictionary<string, string> SearchParameterHashMap => Current.SearchParameterHashMap;
    public IEnumerable<SearchParameterInfo> GetAllKnownSearchParameters() => Current.GetAllKnownSearchParameters();
    public IEnumerable<SearchParameterInfo> GetAllKnownSearchParameters(string resourceType) =>
        Current.GetAllKnownSearchParameters(resourceType);
    public IEnumerable<SearchParameterInfo> GetSearchParameters(string resourceType) =>
        Current.GetSearchParameters(resourceType);
    public bool TryGetSearchParameters(string resourceType, out IEnumerable<SearchParameterInfo> searchParameters) =>
        Current.TryGetSearchParameters(resourceType, out searchParameters);
    public bool TryGetSearchParameter(string resourceType, string code, out SearchParameterInfo searchParameter) =>
        Current.TryGetSearchParameter(resourceType, code, out searchParameter);
    public SearchParameterInfo GetSearchParameter(string resourceType, string code) =>
        Current.GetSearchParameter(resourceType, code);
    public bool TryGetSearchParameter(Uri definitionUri, out SearchParameterInfo value) =>
        Current.TryGetSearchParameter(definitionUri, out value);
    public SearchParameterInfo GetSearchParameter(Uri definitionUri) => Current.GetSearchParameter(definitionUri);
    public bool TryGetSearchParameterRootUrl(Uri definitionUri, out Uri rootUri) =>
        Current.TryGetSearchParameterRootUrl(definitionUri, out rootUri);
    public void UpdateSearchParameterHashMap(Dictionary<string, string> updatedSearchParamHashMap) =>
        Current.UpdateSearchParameterHashMap(updatedSearchParamHashMap);
    public string GetSearchParameterHashForResourceType(string resourceType) =>
        Current.GetSearchParameterHashForResourceType(resourceType);
    public void AddNewSearchParameters(IReadOnlyCollection<IElement> searchParameters, bool calculateHash = true) =>
        Current.AddNewSearchParameters(searchParameters, calculateHash);
    public void DeleteSearchParameter(string url, bool calculateHash = true) =>
        Current.DeleteSearchParameter(url, calculateHash);
}
