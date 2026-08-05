// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using Ignixa.Abstractions;
using Ignixa.Search.Definition;
using Ignixa.Search.Exceptions;
using Ignixa.Search.Expressions.Parsers.Binding;
using Ignixa.Search.Expressions.Parsers.Syntax;
using Ignixa.Search.Indexing;
using Ignixa.Search.Models;
using Ignixa.Serialization;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Expressions.Parsers;

/// <summary>
/// Resolves a scanned <see cref="Syntax.SearchKeySyntax"/> against the schema (the search-parameter
/// definitions and the FHIR schema) into a <see cref="Binding.BoundSearchKey"/> — turning names into
/// resolved parameters and validating chains, includes, and reverse chains.
/// </summary>
internal sealed class SearchKeyBinder(ISearchParameterDefinitionManager definitionManager, IFhirSchemaProvider schemaProvider)
{
    private static readonly FrozenDictionary<string, SearchModifierCode> SearchParamModifierMapping = Enum
        .GetValues<SearchModifierCode>()
        .Where(code => code != SearchModifierCode.Type)
        .ToFrozenDictionary(code => code.GetLiteral(), code => code, StringComparer.Ordinal);

    internal BoundSearchKey Bind(string[] resourceTypes, SearchKeySyntax syntax)
    {
        ArgumentNullException.ThrowIfNull(resourceTypes);
        ArgumentNullException.ThrowIfNull(syntax);

        return syntax switch
        {
            ParameterKeySyntax parameterSyntax => BindParameter(resourceTypes, parameterSyntax),
            ForwardChainKeySyntax forwardSyntax => BindForward(resourceTypes, forwardSyntax),
            ReverseChainKeySyntax reverseSyntax => BindReverse(resourceTypes, reverseSyntax),
            _ => throw new UnreachableException()
        };
    }

    internal BoundIncludeKey BindInclude(string[] resourceTypes, IncludeKeySyntax syntax, bool isReversed, bool iterate)
    {
        ArgumentNullException.ThrowIfNull(resourceTypes);
        ArgumentNullException.ThrowIfNull(syntax);

        if (resourceTypes.Length == 1 && string.Equals(resourceTypes[0], "DomainResource", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidSearchOperationException(Resources.IncludeCannotBeAgainstBase);
        }

        if (syntax.TargetResourceType is not null && !schemaProvider.ResourceTypeNames.Contains(syntax.TargetResourceType))
        {
            throw new InvalidSearchOperationException(
                string.Format(
                    Resources.IncludeInvalidTargetResourceType,
                    isReversed ? "_revinclude" : "_include",
                    syntax.SourceResourceType,
                    syntax.SearchParameterName,
                    syntax.TargetResourceType));
        }

        SearchParameterInfo? referenceSearchParameter = syntax.Wildcard
            ? null
            : definitionManager.GetSearchParameter(syntax.SourceResourceType, syntax.SearchParameterName!);

        string? targetResourceType = syntax.TargetResourceType;
        if (isReversed && !iterate && targetResourceType is null && resourceTypes.Length > 0)
        {
            targetResourceType = resourceTypes[0];
        }

        ImmutableArray<string> referencedTypes = syntax.Wildcard
            ? ResolveWildcardReferencedTypes(resourceTypes)
            : ImmutableArray<string>.Empty;

        return new BoundIncludeKey(
            referenceSearchParameter,
            syntax.SourceResourceType,
            targetResourceType,
            referencedTypes,
            syntax.Wildcard);
    }

    internal BoundNotReferencedKey BindNotReferenced(NotReferencedKeySyntax syntax)
    {
        ArgumentNullException.ThrowIfNull(syntax);

        if (syntax.SourceResourceType is not null && !schemaProvider.ResourceTypeNames.Contains(syntax.SourceResourceType))
        {
            throw new InvalidSearchOperationException($"Invalid resource type in _not-referenced: '{syntax.SourceResourceType}'");
        }

        return new BoundNotReferencedKey(syntax.SourceResourceType, syntax.ReferencePath);
    }

    private BoundParameterKey BindParameter(string[] resourceTypes, ParameterKeySyntax syntax)
    {
        SearchParameterInfo searchParameter = ResolveCommonSearchParameter(resourceTypes, syntax.Name);
        SearchModifier? modifier = BindModifier(searchParameter, syntax.Modifier);
        return new BoundParameterKey(searchParameter, modifier);
    }

    private BoundSearchKey BindForward(string[] resourceTypes, ForwardChainKeySyntax syntax)
    {
        SearchParameterInfo referenceSearchParameter = ResolveCommonSearchParameter(resourceTypes, syntax.ReferenceName);
        EnsureReferenceSearchParameter(referenceSearchParameter);

        ImmutableArray<string> candidates = ResolveForwardCandidates(referenceSearchParameter, syntax.TargetResourceType);
        ImmutableArray<string> boundResourceTypes = resourceTypes.ToImmutableArray();

        if (candidates.Length == 1)
        {
            string candidate = candidates[0];
            BoundSearchKey next = BindSingleForwardCandidate(candidate, syntax.Next);
            return new BoundChainKey(
                boundResourceTypes,
                referenceSearchParameter,
                [candidate],
                false,
                next);
        }

        var matches = new List<BoundChainKey>(candidates.Length);

        foreach (string candidate in candidates)
        {
            try
            {
                BoundSearchKey next = Bind([candidate], syntax.Next);
                matches.Add(new BoundChainKey(
                    boundResourceTypes,
                    referenceSearchParameter,
                    [candidate],
                    false,
                    next));
            }
            catch (SearchParameterNotSupportedException)
            {
                // Unsupported candidates are intentionally filtered so chain binding can resolve supported targets.
            }
        }

        if (matches.Count == 0)
        {
            throw new InvalidSearchOperationException(Resources.ChainedParameterNotSupported);
        }

        if (matches.Count == 1)
        {
            return matches[0];
        }

        throw new InvalidSearchOperationException(
            string.Format(
                CultureInfo.CurrentCulture,
                Resources.ChainedParameterSpecifyType,
                referenceSearchParameter.Name,
                string.Join(Resources.OrDelimiter, matches.Select(match => $"{referenceSearchParameter.Code}:{match.TargetResourceTypes[0]}"))));
    }

    private BoundSearchKey BindSingleForwardCandidate(string candidate, SearchKeySyntax next)
    {
        try
        {
            return Bind([candidate], next);
        }
        catch (SearchParameterNotSupportedException)
        {
            throw new InvalidSearchOperationException(Resources.ChainedParameterNotSupported);
        }
    }

    private BoundSearchKey BindReverse(string[] resourceTypes, ReverseChainKeySyntax syntax)
    {
        if (!schemaProvider.ResourceTypeNames.Contains(syntax.SourceResourceType))
        {
            throw new InvalidSearchOperationException(string.Format(Resources.ResourceNotSupported, syntax.SourceResourceType));
        }

        SearchParameterInfo referenceSearchParameter = definitionManager.GetSearchParameter(syntax.SourceResourceType, syntax.ReferenceName);
        EnsureReferenceSearchParameter(referenceSearchParameter);

        ImmutableArray<string> targetResourceTypes = referenceSearchParameter.TargetResourceTypes
            .Where(target => resourceTypes.Contains(target, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToImmutableArray();

        if (targetResourceTypes.IsEmpty)
        {
            throw new InvalidSearchOperationException(Resources.ChainedParameterNotSupported);
        }

        BoundSearchKey next = Bind([syntax.SourceResourceType], syntax.Next);
        return new BoundChainKey(
            [syntax.SourceResourceType],
            referenceSearchParameter,
            targetResourceTypes,
            true,
            next);
    }

    private SearchParameterInfo ResolveCommonSearchParameter(string[] resourceTypes, string code)
    {
        SearchParameterInfo searchParameter = definitionManager.GetSearchParameter(resourceTypes[0], code);

        for (var index = 1; index < resourceTypes.Length; index++)
        {
            string resourceType = resourceTypes[index];
            SearchParameterInfo current = definitionManager.GetSearchParameter(resourceType, code);
            if (!ReferenceEquals(searchParameter, current))
            {
                throw new BadSearchRequestException(
                    string.Format(Resources.SearchParameterMustBeCommon, code, resourceTypes[0], resourceType));
            }
        }

        return searchParameter;
    }

    private static SearchModifier? BindModifier(SearchParameterInfo searchParameter, string? modifier)
    {
        if (string.IsNullOrEmpty(modifier))
        {
            return null;
        }

        if (SearchParamModifierMapping.TryGetValue(modifier, out SearchModifierCode searchModifierCode))
        {
            return new SearchModifier(searchModifierCode);
        }

        if (searchParameter.Type == SearchParamType.Reference &&
            searchParameter.TargetResourceTypes.Contains(modifier, StringComparer.OrdinalIgnoreCase))
        {
            return new SearchModifier(SearchModifierCode.Type, modifier);
        }

        throw new SearchModifierNotSupportedException(
            string.Format(Resources.ModifierNotSupported, modifier, searchParameter.Code));
    }

    private ImmutableArray<string> ResolveForwardCandidates(SearchParameterInfo referenceSearchParameter, string? targetResourceType)
    {
        if (targetResourceType is null)
        {
            return referenceSearchParameter.TargetResourceTypes.ToImmutableArray();
        }

        if (!schemaProvider.ResourceTypeNames.Contains(targetResourceType))
        {
            throw new InvalidSearchOperationException(string.Format(Resources.ResourceNotSupported, targetResourceType));
        }

        string? firstMatch = null;
        ImmutableArray<string>.Builder? additionalMatches = null;

        foreach (string target in referenceSearchParameter.TargetResourceTypes)
        {
            if (!string.Equals(target, targetResourceType, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (firstMatch is null)
            {
                firstMatch = target;
                continue;
            }

            additionalMatches ??= ImmutableArray.CreateBuilder<string>();
            if (additionalMatches.Count == 0)
            {
                additionalMatches.Add(firstMatch);
            }

            additionalMatches.Add(target);
        }

        return additionalMatches?.ToImmutable()
            ?? (firstMatch is null
                ? ImmutableArray<string>.Empty
                : ImmutableArray.Create(firstMatch));
    }

    private static void EnsureReferenceSearchParameter(SearchParameterInfo searchParameter)
    {
        if (searchParameter.Type != SearchParamType.Reference)
        {
            throw new InvalidSearchOperationException(Resources.ChainedParameterMustBeReferenceSearchParamType);
        }
    }

    private ImmutableArray<string> ResolveWildcardReferencedTypes(string[] resourceTypes)
    {
        var referencedTypes = new List<string>();

        foreach (SearchParameterInfo searchParameter in resourceTypes
                     .SelectMany(resourceType => definitionManager.GetSearchParameters(resourceType))
                     .Where(searchParameter => searchParameter.Type == SearchParamType.Reference))
        {
            foreach (string targetResourceType in searchParameter.TargetResourceTypes)
            {
                if (!referencedTypes.Contains(targetResourceType, StringComparer.OrdinalIgnoreCase))
                {
                    referencedTypes.Add(targetResourceType);
                }
            }
        }

        return referencedTypes.ToImmutableArray();
    }
}
