// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Ignixa.Abstractions;
using Ignixa.Search.Indexing;
using Ignixa.Search.Models;

namespace Ignixa.Search.InMemory;

/// <summary>
/// Runs a parsed search over an <see cref="IInMemorySearchSource"/>: filtering (with chains and <c>_has</c>),
/// <c>_sort</c>, and <c>_include</c>/<c>_revinclude</c> for a page the caller chose, each only when enabled.
/// </summary>
/// <remarks>
/// Paging is the caller's: <see cref="Search"/> returns every match in order, and <see cref="Include"/> resolves
/// includes for whichever page of them the caller returns. Requesting a feature that is not enabled throws
/// <see cref="SearchOperationNotSupportedException"/> before any work is done.
/// </remarks>
[CLSCompliant(false)]
public sealed class InMemorySearchEngine
{
    private readonly IInMemorySearchSource _source;
    private readonly InMemorySearchFeatures _features;
    private readonly SearchQueryInterpreter _interpreter;

    public InMemorySearchEngine(IInMemorySearchSource source, InMemorySearchFeatures features)
    {
        ArgumentNullException.ThrowIfNull(source);

        _source = source;
        _features = features;
        _interpreter = new SearchQueryInterpreter(source, features);
    }

    /// <summary>
    /// The resources of <see cref="SearchOptions.ResourceType"/> matching <see cref="SearchOptions.Expression"/>
    /// (every resource when it is null), ordered by <see cref="SearchOptions.Sort"/> when given and in source order
    /// otherwise.
    /// </summary>
    /// <exception cref="SearchOperationNotSupportedException">
    /// The options ask for a feature that is not enabled, or contain an expression in-memory search cannot evaluate.
    /// </exception>
    public IReadOnlyList<(ResourceKey Location, IReadOnlyCollection<SearchIndexEntry> Index)> Search(SearchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrEmpty(options.ResourceType, nameof(options));

        Require(InMemorySearchFeatures.Sort, options.Sort.Count > 0, "_sort");
        Require(InMemorySearchFeatures.Include, options.Include.Count > 0, "_include");
        Require(InMemorySearchFeatures.RevInclude, options.RevInclude.Count > 0, "_revinclude");

        IEnumerable<(ResourceKey Location, IReadOnlyCollection<SearchIndexEntry> Index)> candidates = _source.Enumerate(options.ResourceType);
        IEnumerable<(ResourceKey Location, IReadOnlyCollection<SearchIndexEntry> Index)> matches = options.Expression is null
            ? candidates
            : options.Expression.AcceptVisitor(_interpreter, _interpreter.InitialContext)(candidates);

        return options.Sort.Count > 0
            ? InMemorySortOrder.Order(matches, options.Sort)
            : [.. matches];
    }

    /// <summary>
    /// The resources <see cref="SearchOptions.Include"/> and <see cref="SearchOptions.RevInclude"/> add to
    /// <paramref name="page"/>, excluding the page's own resources; empty when the options have neither.
    /// </summary>
    /// <exception cref="SearchOperationNotSupportedException">The options ask for an include kind that is not enabled.</exception>
    public IReadOnlyList<(ResourceKey Location, IReadOnlyCollection<SearchIndexEntry> Index)> Include(
        IReadOnlyCollection<(ResourceKey Location, IReadOnlyCollection<SearchIndexEntry> Index)> page,
        SearchOptions options)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(options);

        Require(InMemorySearchFeatures.Include, options.Include.Count > 0, "_include");
        Require(InMemorySearchFeatures.RevInclude, options.RevInclude.Count > 0, "_revinclude");

        return InMemoryIncludeResolver.Resolve(_source, page, options.Include, options.RevInclude);
    }

    private void Require(InMemorySearchFeatures feature, bool requested, string parameter)
    {
        if (requested && !_features.HasFlag(feature))
        {
            throw new SearchOperationNotSupportedException($"{parameter} is not enabled for this in-memory search.");
        }
    }
}
