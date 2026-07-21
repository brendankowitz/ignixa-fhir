using System.Collections.Frozen;
using Ignixa.Abstractions;
using Ignixa.Search.Definition;
using Ignixa.Search.Indexing;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Benchmarks;

internal sealed class BenchmarkSearchParameterDefinitionManager : ISearchParameterDefinitionManager
{
    private const string StableHash = "benchmark";

    private readonly SearchParameterInfo[] _allSearchParameters;
    private readonly FrozenDictionary<string, SearchParameterInfo[]> _searchParametersByResourceType;
    private readonly FrozenDictionary<string, FrozenDictionary<string, SearchParameterInfo>> _searchParameterLookup;
    private readonly FrozenDictionary<Uri, SearchParameterInfo> _searchParameterByDefinitionUri;

    public BenchmarkSearchParameterDefinitionManager()
    {
        var allSearchParameters = new List<SearchParameterInfo>();
        var byResourceType = new Dictionary<string, List<SearchParameterInfo>>(StringComparer.OrdinalIgnoreCase);
        var byDefinitionUri = new Dictionary<Uri, SearchParameterInfo>();

        Register(CreateParameter("Patient", "name", SearchParamType.String), "Patient");
        Register(CreateParameter("Patient", "identifier", SearchParamType.Token), "Patient");
        Register(CreateParameter("Observation", "subject", SearchParamType.Reference, ["Patient"]), "Observation");
        Register(CreateParameter("Observation", "patient", SearchParamType.Reference, ["Patient"]), "Observation");
        Register(CreateParameter("Group", "member", SearchParamType.Reference, ["Patient"]), "Group");
        Register(CreateParameter("Group", "_tag", SearchParamType.Token), "Group");
        Register(CreateParameter("Observation", "code", SearchParamType.Token), "Observation");

        SearchParameterInfo componentCode = CreateParameter(
            "Observation",
            "component-code",
            SearchParamType.Token);

        SearchParameterInfo componentValueQuantity = CreateParameter(
            "Observation",
            "component-value-quantity",
            SearchParamType.Quantity);

        SearchParameterInfo composite = CreateParameter(
            "Observation",
            "code-value-quantity",
            SearchParamType.Composite,
            components:
            [
                new SearchParameterComponentInfo(componentCode.Url)
                {
                    ResolvedSearchParameter = componentCode
                },
                new SearchParameterComponentInfo(componentValueQuantity.Url)
                {
                    ResolvedSearchParameter = componentValueQuantity
                }
            ]);

        Register(composite, "Observation");

        _allSearchParameters = allSearchParameters.ToArray();

        _searchParametersByResourceType = byResourceType.ToFrozenDictionary(
            pair => pair.Key,
            pair => pair.Value.ToArray(),
            StringComparer.OrdinalIgnoreCase);

        _searchParameterLookup = _searchParametersByResourceType.ToFrozenDictionary(
            pair => pair.Key,
            pair => pair.Value.ToFrozenDictionary(parameter => parameter.Code, StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);

        _searchParameterByDefinitionUri = byDefinitionUri.ToFrozenDictionary();

        SearchParameterHashMap = _searchParametersByResourceType.Keys.ToFrozenDictionary(
            resourceType => resourceType,
            _ => StableHash,
            StringComparer.OrdinalIgnoreCase);

        void Register(SearchParameterInfo parameter, string resourceType)
        {
            allSearchParameters.Add(parameter);

            if (!byResourceType.TryGetValue(resourceType, out List<SearchParameterInfo>? searchParameters))
            {
                searchParameters = [];
                byResourceType.Add(resourceType, searchParameters);
            }

            searchParameters.Add(parameter);
            byDefinitionUri.Add(parameter.Url, parameter);
        }
    }

    public IEnumerable<SearchParameterInfo> AllSearchParameters => _allSearchParameters;

    public IReadOnlyDictionary<string, string> SearchParameterHashMap { get; }

    public IEnumerable<SearchParameterInfo> GetSearchParameters(string resourceType)
    {
        return _searchParametersByResourceType.TryGetValue(resourceType, out SearchParameterInfo[]? searchParameters)
            ? searchParameters
            : Array.Empty<SearchParameterInfo>();
    }

    public bool TryGetSearchParameters(string resourceType, out IEnumerable<SearchParameterInfo> searchParameters)
    {
        bool found = _searchParametersByResourceType.TryGetValue(resourceType, out SearchParameterInfo[]? values);
        searchParameters = values ?? Array.Empty<SearchParameterInfo>();
        return found;
    }

    public bool TryGetSearchParameter(string resourceType, string code, out SearchParameterInfo searchParameter)
    {
        if (_searchParameterLookup.TryGetValue(resourceType, out FrozenDictionary<string, SearchParameterInfo>? byCode) &&
            byCode.TryGetValue(code, out SearchParameterInfo? value))
        {
            searchParameter = value;
            return true;
        }

        searchParameter = null!;
        return false;
    }

    public SearchParameterInfo GetSearchParameter(string resourceType, string code)
    {
        return TryGetSearchParameter(resourceType, code, out SearchParameterInfo searchParameter)
            ? searchParameter
            : throw new SearchParameterNotSupportedException(resourceType, code);
    }

    public bool TryGetSearchParameter(Uri definitionUri, out SearchParameterInfo value)
    {
        return _searchParameterByDefinitionUri.TryGetValue(definitionUri, out value!);
    }

    public SearchParameterInfo GetSearchParameter(Uri definitionUri)
    {
        return TryGetSearchParameter(definitionUri, out SearchParameterInfo searchParameter)
            ? searchParameter
            : throw new SearchParameterNotSupportedException(definitionUri);
    }

    public void UpdateSearchParameterHashMap(Dictionary<string, string> updatedSearchParamHashMap)
    {
        throw MutationNotSupported();
    }

    public string GetSearchParameterHashForResourceType(string resourceType)
    {
        return SearchParameterHashMap.TryGetValue(resourceType, out string? hash)
            ? hash
            : StableHash;
    }

    public void AddNewSearchParameters(IReadOnlyCollection<IElement> searchParameters, bool calculateHash = true)
    {
        throw MutationNotSupported();
    }

    public void DeleteSearchParameter(string url, bool calculateHash = true)
    {
        throw MutationNotSupported();
    }

    private static SearchParameterInfo CreateParameter(
        string resourceType,
        string code,
        SearchParamType type,
        IReadOnlyList<string>? targetResourceTypes = null,
        IReadOnlyList<SearchParameterComponentInfo>? components = null)
    {
        return new SearchParameterInfo(
            name: code,
            code: code,
            searchParamType: type,
            url: new Uri($"http://example.org/SearchParameter/{resourceType}-{code}"),
            components: components,
            targetResourceTypes: targetResourceTypes,
            baseResourceTypes: [resourceType]);
    }

    private static NotSupportedException MutationNotSupported()
    {
        return new NotSupportedException("Search parser benchmarks use immutable search parameter definitions.");
    }
}
