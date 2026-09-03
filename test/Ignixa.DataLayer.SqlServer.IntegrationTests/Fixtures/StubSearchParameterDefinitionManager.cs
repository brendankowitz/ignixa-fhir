using Ignixa.Abstractions;
using Ignixa.Search.Definition;
using Ignixa.Search.Models;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;

/// <summary>
/// Answers only <see cref="ISearchParameterDefinitionManager.TryGetSearchParameter(Uri, out SearchParameterInfo)"/>,
/// from a caller-supplied map. That is the single member
/// <c>SqlServerSearchIndexReferenceDataCache.SyncSearchParametersToDatabaseAsync</c> consults (for
/// <see cref="SearchParameterInfo.OverridesUrl"/> aliasing); every other member throws rather than
/// returning an empty default, so a test that accidentally depends on one fails loudly instead of
/// silently passing against a stub.
/// </summary>
public sealed class StubSearchParameterDefinitionManager(IReadOnlyDictionary<string, SearchParameterInfo> parametersByUrl)
    : ISearchParameterDefinitionManager
{
    public bool TryGetSearchParameter(Uri definitionUri, out SearchParameterInfo value)
        => parametersByUrl.TryGetValue(definitionUri.ToString(), out value!);

    public IEnumerable<SearchParameterInfo> AllSearchParameters => parametersByUrl.Values;

    public IReadOnlyDictionary<string, string> SearchParameterHashMap => throw NotStubbed();

    public IEnumerable<SearchParameterInfo> GetSearchParameters(string resourceType) => throw NotStubbed();

    public bool TryGetSearchParameters(string resourceType, out IEnumerable<SearchParameterInfo> searchParameters)
        => throw NotStubbed();

    public bool TryGetSearchParameter(string resourceType, string code, out SearchParameterInfo searchParameter)
        => throw NotStubbed();

    public SearchParameterInfo GetSearchParameter(string resourceType, string code) => throw NotStubbed();

    public SearchParameterInfo GetSearchParameter(Uri definitionUri) => throw NotStubbed();

    public void UpdateSearchParameterHashMap(Dictionary<string, string> updatedSearchParamHashMap) => throw NotStubbed();

    public string GetSearchParameterHashForResourceType(string resourceType) => throw NotStubbed();

    public void AddNewSearchParameters(IReadOnlyCollection<IElement> searchParameters, bool calculateHash = true)
        => throw NotStubbed();

    public void DeleteSearchParameter(string url, bool calculateHash = true) => throw NotStubbed();

    private static NotSupportedException NotStubbed()
        => new("This stub only answers TryGetSearchParameter(Uri, out SearchParameterInfo).");
}
