// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Definition;

public static class ReferenceIdentifierSearchParameterFactory
{
    private const string IdentifierSuffix = ":identifier";
    private const string IdentifierFragment = "#identifier";

    public static Uri DeriveUrl(SearchParameterInfo searchParameter)
    {
        ArgumentNullException.ThrowIfNull(searchParameter);

        return new Uri($"{searchParameter.Url}{IdentifierFragment}", UriKind.RelativeOrAbsolute);
    }

    public static string DeriveCode(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        return $"{code}{IdentifierSuffix}";
    }

    public static SearchParameterInfo Create(SearchParameterInfo searchParameter)
    {
        ArgumentNullException.ThrowIfNull(searchParameter);

        if (searchParameter.Type != SearchParamType.Reference)
        {
            throw new ArgumentException("Only reference search parameters have an identifier derivative.", nameof(searchParameter));
        }

        string derivedCode = DeriveCode(searchParameter.Code);
        return new SearchParameterInfo(
            name: derivedCode,
            code: derivedCode,
            searchParamType: SearchParamType.Token,
            url: DeriveUrl(searchParameter),
            expression: searchParameter.Expression,
            targetResourceTypes: [],
            baseResourceTypes: searchParameter.BaseResourceTypes)
        {
            IsDerived = true,
            IsSearchable = true,
            IsSupported = true,
        };
    }

    public static bool TryResolve(
        ISearchParameterDefinitionManager definitionManager,
        SearchParameterInfo searchParameter,
        out SearchParameterInfo derivedSearchParameter)
    {
        ArgumentNullException.ThrowIfNull(definitionManager);
        ArgumentNullException.ThrowIfNull(searchParameter);

        return definitionManager.TryGetSearchParameter(DeriveUrl(searchParameter), out derivedSearchParameter);
    }
}
