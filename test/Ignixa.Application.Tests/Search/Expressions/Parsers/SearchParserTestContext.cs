// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Ignixa.Abstractions;
using Ignixa.Search.Definition;
using Ignixa.Search.Expressions.Parsers;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Specification.Generated;
using Ignixa.Specification.ValueSets.Normative;
using NSubstitute;

namespace Ignixa.Application.Tests.Search.Expressions.Parsers;

internal sealed class SearchParserTestContext
{
    private readonly Dictionary<string, List<SearchParameterInfo>> _searchParametersByResourceType = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SearchParameterInfo> _searchParametersByUrl = new(StringComparer.Ordinal);

    public SearchParserTestContext()
    {
        SchemaProvider = new R4CoreSchemaProvider();
        DefinitionManager = Substitute.For<ISearchParameterDefinitionManager>();
        ValueParser = new SearchParameterExpressionParser(new ReferenceSearchValueParser(SchemaProvider, NullFhirBaseUriProvider.Instance), SchemaProvider);
        Parser = new ExpressionParser(() => DefinitionManager, ValueParser, SchemaProvider);

        DefinitionManager.GetSearchParameters(Arg.Any<string>()).Returns(callInfo => GetSearchParameters(callInfo.ArgAt<string>(0)));
        DefinitionManager.GetSearchParameter(Arg.Any<string>(), Arg.Any<string>()).Returns(callInfo => GetSearchParameter(callInfo.ArgAt<string>(0), callInfo.ArgAt<string>(1))!);
        DefinitionManager.TryGetSearchParameter(Arg.Any<Uri>(), out Arg.Any<SearchParameterInfo>()).Returns(callInfo =>
        {
            if (_searchParametersByUrl.TryGetValue(callInfo.ArgAt<Uri>(0).OriginalString, out SearchParameterInfo? parameter))
            {
                callInfo[1] = parameter;
                return true;
            }

            callInfo[1] = null!;
            return false;
        });
        DefinitionManager.AllSearchParameters.Returns(_ => _searchParametersByResourceType.Values.SelectMany(parameters => parameters).ToArray());
        DefinitionManager.SearchParameterHashMap.Returns(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    }

    public R4CoreSchemaProvider SchemaProvider { get; }

    public ISearchParameterDefinitionManager DefinitionManager { get; }

    public ISearchParameterExpressionParser ValueParser { get; }

    public IExpressionParser Parser { get; }

    public SearchParameterInfo Add(
        string resourceType,
        string code,
        SearchParamType type,
        string[]? targets = null,
        SearchParameterComponentInfo[]? components = null,
        Uri? url = null)
    {
        var parameter = new SearchParameterInfo(
            name: code,
            code: code,
            searchParamType: type,
            components: components!,
            targetResourceTypes: targets!,
            baseResourceTypes: new[] { resourceType },
            url: url ?? new Uri($"http://ignixa.test/SearchParameter/{resourceType}-{code}"));

        Register(resourceType, parameter);

        DefinitionManager.GetSearchParameters(resourceType).Returns(_ => GetSearchParameters(resourceType));
        DefinitionManager.GetSearchParameter(resourceType, code).Returns(parameter);

        return parameter;
    }

    public void AddCommon(SearchParameterInfo parameter, params string[] resourceTypes)
    {
        foreach (var resourceType in resourceTypes)
        {
            Register(resourceType, parameter);
            DefinitionManager.GetSearchParameters(resourceType).Returns(_ => GetSearchParameters(resourceType));
            DefinitionManager.GetSearchParameter(resourceType, parameter.Code).Returns(parameter);
        }
    }

    public SearchParameterInfo AddIdentifierDerivative(SearchParameterInfo searchParameter)
    {
        SearchParameterInfo derived = ReferenceIdentifierSearchParameterFactory.Create(searchParameter);
        _searchParametersByUrl.Add(derived.Url!.OriginalString, derived);
        return derived;
    }

    private IEnumerable<SearchParameterInfo> GetSearchParameters(string resourceType)
    {
        return _searchParametersByResourceType.TryGetValue(resourceType, out var searchParameters)
            ? searchParameters.ToArray()
            : Array.Empty<SearchParameterInfo>();
    }

    private SearchParameterInfo? GetSearchParameter(string resourceType, string code)
    {
        return _searchParametersByResourceType.TryGetValue(resourceType, out var searchParameters)
            ? searchParameters.FirstOrDefault(parameter => string.Equals(parameter.Code, code, StringComparison.OrdinalIgnoreCase))
            : null;
    }

    private void Register(string resourceType, SearchParameterInfo parameter)
    {
        if (!_searchParametersByResourceType.TryGetValue(resourceType, out var searchParameters))
        {
            searchParameters = new List<SearchParameterInfo>();
            _searchParametersByResourceType[resourceType] = searchParameters;
        }

        if (!searchParameters.Contains(parameter))
        {
            searchParameters.Add(parameter);
        }

        _searchParametersByUrl.TryAdd(parameter.Url!.OriginalString, parameter);
    }
}
