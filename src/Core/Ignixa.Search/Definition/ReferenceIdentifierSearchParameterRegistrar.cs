// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Definition;

internal static class ReferenceIdentifierSearchParameterRegistrar
{
    public static void Register(
        ConcurrentDictionary<Uri, SearchParameterInfo> urlLookup,
        ConcurrentDictionary<string, ConcurrentDictionary<string, SearchParameterInfo>> typeLookup)
    {
        SearchParameterInfo[] referenceParameters = typeLookup.Values
            .SelectMany(parameters => parameters.Values)
            .Where(parameter => parameter.Type == SearchParamType.Reference)
            .Where(parameter => parameter.Url is not null)
            .DistinctBy(parameter => parameter.Url, SearchParameterUriComparer.Instance)
            .ToArray();

        foreach (SearchParameterInfo referenceParameter in referenceParameters)
        {
            Uri derivedUrl = ReferenceIdentifierSearchParameterFactory.DeriveUrl(referenceParameter);
            if (urlLookup.TryGetValue(derivedUrl, out SearchParameterInfo existing))
            {
                if (!existing.IsDerived)
                {
                    throw new InvalidOperationException(
                        $"A search parameter is already registered with derived URL '{derivedUrl}'.");
                }

                continue;
            }

            SearchParameterInfo derivedParameter = ReferenceIdentifierSearchParameterFactory.Create(referenceParameter);
            urlLookup.TryAdd(derivedParameter.Url, derivedParameter);
        }

        foreach (ConcurrentDictionary<string, SearchParameterInfo> parameters in typeLookup.Values)
        {
            SearchParameterInfo[] referencesForType = parameters.Values
                .Where(parameter => parameter.Type == SearchParamType.Reference)
                .ToArray();

            foreach (SearchParameterInfo referenceParameter in referencesForType)
            {
                Uri derivedUrl = ReferenceIdentifierSearchParameterFactory.DeriveUrl(referenceParameter);
                SearchParameterInfo derivedParameter = urlLookup[derivedUrl];
                if (parameters.TryGetValue(derivedParameter.Code, out SearchParameterInfo existing))
                {
                    if (!existing.IsDerived)
                    {
                        throw new InvalidOperationException(
                            $"A search parameter is already registered with derived code '{derivedParameter.Code}'.");
                    }

                    continue;
                }

                parameters.TryAdd(derivedParameter.Code, derivedParameter);
            }
        }
    }

    public static void Unregister(
        SearchParameterInfo searchParameter,
        ConcurrentDictionary<Uri, SearchParameterInfo> urlLookup,
        ConcurrentDictionary<string, ConcurrentDictionary<string, SearchParameterInfo>> typeLookup)
    {
        if (searchParameter.Type != SearchParamType.Reference)
        {
            return;
        }

        Uri derivedUrl = ReferenceIdentifierSearchParameterFactory.DeriveUrl(searchParameter);
        string derivedCode = ReferenceIdentifierSearchParameterFactory.DeriveCode(searchParameter.Code);
        urlLookup.TryRemove(derivedUrl, out _);

        foreach (ConcurrentDictionary<string, SearchParameterInfo> parameters in typeLookup.Values)
        {
            if (parameters.TryGetValue(derivedCode, out SearchParameterInfo parameter) &&
                parameter.IsDerived &&
                SearchParameterUriComparer.Instance.Equals(parameter.Url, derivedUrl))
            {
                parameters.TryRemove(derivedCode, out _);
            }
        }
    }
}
