// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Globalization;
using EnsureThat;
using Ignixa.Specification;
using Ignixa.Specification.ValueSets.Normative;
using Ignixa.Search.Definition.BundleNavigators;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.Converters;
using Ignixa.Search.Models;
using Ignixa.Serialization;
using Ignixa.Abstractions;
using Ignixa.Search.Exceptions;
using Ignixa.Serialization.Models;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Search.Definition;

internal static class SearchParameterDefinitionBuilder
{
    internal static void Build(
        IReadOnlyCollection<IElement> searchParameters,
        ConcurrentDictionary<Uri, SearchParameterInfo> uriDictionary,
        ConcurrentDictionary<string, ConcurrentDictionary<string, SearchParameterInfo>> resourceTypeDictionary,
        IFhirSchemaProvider modelInfoProvider)
    {
        EnsureArg.IsNotNull(searchParameters, nameof(searchParameters));
        EnsureArg.IsNotNull(uriDictionary, nameof(uriDictionary));
        EnsureArg.IsNotNull(resourceTypeDictionary, nameof(resourceTypeDictionary));
        EnsureArg.IsNotNull(modelInfoProvider, nameof(modelInfoProvider));

        ILookup<string, SearchParameterInfo> searchParametersLookup = ValidateAndGetFlattenedList(
            searchParameters,
            uriDictionary,
            modelInfoProvider).ToLookup(
            entry => entry.ResourceType,
            entry => entry.SearchParameter);

        // Build the inheritance. For example, the _id search parameter is on Resource
        // and should be available to all resources that inherit Resource.
        foreach (string resourceType in modelInfoProvider.ResourceTypeNames)
            // Recursively build the search parameter definitions. For example,
            // Appointment inherits from DomainResource, which inherits from Resource
            // and therefore Appointment should include all search parameters DomainResource and Resource supports.
            BuildSearchParameterDefinition(searchParametersLookup, resourceType, resourceTypeDictionary, modelInfoProvider);
    }

    internal static bool ShouldExcludeEntry(string resourceType, string searchParameterName, IFhirSchemaProvider modelInfoProvider)
    {
        return resourceType == KnownResourceTypes.DomainResource && searchParameterName == "_text" ||
               resourceType == KnownResourceTypes.Resource && searchParameterName == "_content" ||
               resourceType == KnownResourceTypes.Resource && searchParameterName == "_query" ||
               resourceType == KnownResourceTypes.Resource && searchParameterName == "_list" ||
               resourceType == KnownResourceTypes.Resource && searchParameterName == "_in" ||
               ShouldExcludeEntryStu3(resourceType, searchParameterName, modelInfoProvider);
    }

    internal static bool ShouldExcludeEntryStu3(string resourceType, string searchParameterName, IFhirSchemaProvider modelInfoProvider)
    {
        return modelInfoProvider.Version == FhirVersion.Stu3 &&
               resourceType == "DataElement" && (searchParameterName == "objectClass" || searchParameterName == "objectClassProperty");
    }

    private static SearchParameterInfo GetOrCreateSearchParameterInfo(SearchParameterNavigator searchParameter, IDictionary<Uri, SearchParameterInfo> uriDictionary)
    {
        // Return SearchParameterInfo that has already been created for this Uri
        if (uriDictionary.TryGetValue(new Uri(searchParameter.Url, UriKind.RelativeOrAbsolute), out SearchParameterInfo spi)) return spi;

        return new SearchParameterInfo(searchParameter);
    }

    private static List<(string ResourceType, SearchParameterInfo SearchParameter)> ValidateAndGetFlattenedList(
        IReadOnlyCollection<IElement> searchParamCollection,
        IDictionary<Uri, SearchParameterInfo> uriDictionary,
        IFhirSchemaProvider modelInfoProvider)
    {
        var issues = new List<OperationOutcomeJsonNode.IssueComponent>();
        var searchParameters = searchParamCollection.Select((x, entryIndex) =>
        {
            try
            {
                return new SearchParameterNavigator(x);
            }
            catch (ArgumentException)
            {
                AddIssue(Resources.SearchParameterDefinitionInvalidResource, entryIndex);
                return null;
            }
        }).ToList();

        // Do the first pass to make sure all resources are SearchParameter.
        for (int entryIndex = 0; entryIndex < searchParameters.Count; entryIndex++)
        {
            SearchParameterNavigator searchParameter = searchParameters[entryIndex];

            if (searchParameter == null) continue;

            try
            {
                // Skip search parameters with null or empty URLs
                if (string.IsNullOrWhiteSpace(searchParameter.Url))
                {
                    AddIssue(Resources.SearchParameterDefinitionInvalidDefinitionUri, entryIndex);
                    continue;
                }

                SearchParameterInfo searchParameterInfo = GetOrCreateSearchParameterInfo(searchParameter, uriDictionary);
                uriDictionary.Add(new Uri(searchParameter.Url), searchParameterInfo);
            }
            catch (FormatException)
            {
                AddIssue(Resources.SearchParameterDefinitionInvalidDefinitionUri, entryIndex);
                continue;
            }
            catch (ArgumentException)
            {
                AddIssue(Resources.SearchParameterDefinitionDuplicatedEntry, searchParameter.Url);
                continue;
            }
        }

        EnsureNoIssues();

        var validatedSearchParameters = new List<(string ResourceType, SearchParameterInfo SearchParameter)>
        {
            // _type is currently missing from the search params definition bundle, so we inject it in here.
            (KnownResourceTypes.Resource, new SearchParameterInfo(SearchParameterNames.ResourceType, SearchParameterNames.ResourceType, SearchParamType.Token, SearchParameterNames.ResourceTypeUri, null, "Resource.type().name", null))
        };

        // Do the second pass to make sure the definition is valid.
        foreach (SearchParameterNavigator searchParameter in searchParameters)
        {
            if (searchParameter == null) continue;

            // If this is a composite search parameter, then make sure components are defined.
            if (string.Equals(searchParameter.Type, SearchParamType.Composite.GetLiteral(), StringComparison.OrdinalIgnoreCase))
            {
                IReadOnlyList<IElement> composites = searchParameter.Component;
                if (composites.Count == 0)
                {
                    AddIssue(Resources.SearchParameterDefinitionInvalidComponent, searchParameter.Url);
                    continue;
                }

                SearchParameterInfo compositeSearchParameter = GetOrCreateSearchParameterInfo(searchParameter, uriDictionary);

                for (int componentIndex = 0; componentIndex < composites.Count; componentIndex++)
                {
                    IElement component = composites[componentIndex];
                    string definitionUrl = GetComponentDefinition(component);

                    if (definitionUrl == null ||
                        !uriDictionary.TryGetValue(new Uri(definitionUrl), out SearchParameterInfo componentSearchParameter))
                    {
                        AddIssue(
                            Resources.SearchParameterDefinitionInvalidComponentReference,
                            searchParameter.Url,
                            componentIndex);
                        continue;
                    }

                    if (componentSearchParameter.Type == SearchParamType.Composite)
                    {
                        AddIssue(
                            Resources.SearchParameterDefinitionComponentReferenceCannotBeComposite,
                            searchParameter.Url,
                            componentIndex);
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(component.Scalar("expression")?.ToString()))
                    {
                        AddIssue(
                            Resources.SearchParameterDefinitionInvalidComponentExpression,
                            searchParameter.Url,
                            componentIndex);
                        continue;
                    }

                    compositeSearchParameter.Component[componentIndex].ResolvedSearchParameter = componentSearchParameter;
                }
            }

            // Make sure the base is defined.
            IReadOnlyList<string> bases = searchParameter.Base;
            if (bases.Count == 0)
            {
                AddIssue(Resources.SearchParameterDefinitionBaseNotDefined, searchParameter.Url);
                continue;
            }

            for (int baseElementIndex = 0; baseElementIndex < bases.Count; baseElementIndex++)
            {
                string code = bases[baseElementIndex];

                string baseResourceType = code;

                // Make sure the expression is not empty unless they are known to have empty expression.
                // These are special search parameters that searches across all properties and needs to be handled specially.
                if (ShouldExcludeEntry(baseResourceType, searchParameter.Name, modelInfoProvider))
                {
                    continue;
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(searchParameter.Expression))
                    {
                        AddIssue(Resources.SearchParameterDefinitionInvalidExpression, searchParameter.Url);
                        continue;
                    }
                }

                validatedSearchParameters.Add((baseResourceType, GetOrCreateSearchParameterInfo(searchParameter, uriDictionary)));
            }
        }

        EnsureNoIssues();

        return validatedSearchParameters;

        void AddIssue(string format, params object[] args)
        {
            issues.Add(new OperationOutcomeJsonNode.IssueComponent()
            {
                Severity = OperationOutcomeJsonNode.IssueSeverity.Fatal,
                Code = OperationOutcomeJsonNode.IssueType.Invalid,
                Diagnostics = string.Format(CultureInfo.InvariantCulture, format, args)
            });
        }

        void EnsureNoIssues()
        {
            if (issues.Count != 0)
                throw new InvalidDefinitionException(
                    Resources.SearchParameterDefinitionContainsInvalidEntry,
                    issues.ToArray());
        }
    }

    private static HashSet<SearchParameterInfo> BuildSearchParameterDefinition(
        ILookup<string, SearchParameterInfo> searchParametersLookup,
        string resourceType,
        ConcurrentDictionary<string, ConcurrentDictionary<string, SearchParameterInfo>> resourceTypeDictionary,
        IFhirSchemaProvider modelInfoProvider)
    {
        HashSet<SearchParameterInfo> results;
        if (resourceTypeDictionary.TryGetValue(resourceType, out ConcurrentDictionary<string, SearchParameterInfo> cachedSearchParameters))
            results = new HashSet<SearchParameterInfo>(cachedSearchParameters.Values);
        else
            results = new HashSet<SearchParameterInfo>();

        string baseType = null;

        if (!string.Equals(resourceType, KnownResourceTypes.Resource, StringComparison.Ordinal) && !string.Equals(resourceType, KnownResourceTypes.Base, StringComparison.Ordinal))
            baseType = KnownResourceTypes.Resource;
        else if (!string.Equals(resourceType, KnownResourceTypes.Base, StringComparison.Ordinal)) baseType = KnownResourceTypes.Base;

        if (baseType != null && !string.Equals(KnownResourceTypes.Base, baseType, StringComparison.OrdinalIgnoreCase))
        {
            HashSet<SearchParameterInfo> baseResults = BuildSearchParameterDefinition(searchParametersLookup, baseType, resourceTypeDictionary, modelInfoProvider);
            results.UnionWith(baseResults);
        }

        results.UnionWith(searchParametersLookup[resourceType]);

        var searchParameterDictionary = new ConcurrentDictionary<string, SearchParameterInfo>(
            results.ToDictionary(
                r => r.Code,
                r => r,
                StringComparer.Ordinal));

        if (!resourceTypeDictionary.TryAdd(resourceType, searchParameterDictionary)) resourceTypeDictionary[resourceType] = searchParameterDictionary;

        return results;
    }

    private static string GetComponentDefinition(IElement component)
    {
        // In Stu3 the Url is under 'definition.reference'
        return component.Scalar("definition.reference")?.ToString() ??
               component.Scalar("definition")?.ToString();
    }
}
