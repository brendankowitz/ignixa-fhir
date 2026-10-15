// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System.Diagnostics.CodeAnalysis;
using Ignixa.Abstractions;
using Ignixa.Search.Indexing;
using Ignixa.Search.Models;

namespace Ignixa.Search.InMemory;

/// <summary>
/// Runs a parsed search over an <see cref="IInMemorySearchSource"/>: filtering (with chains and <c>_has</c>),
/// <c>_sort</c>, and <c>_include</c>/<c>_revinclude</c> for a page the caller chose, each only when enabled.
/// </summary>
/// <remarks>
/// <para>
/// Paging is the caller's: <see cref="Search"/> returns every match in order, and <see cref="Include"/> resolves
/// includes for whichever page of them the caller returns. Requesting a feature that is not enabled throws
/// <see cref="SearchOperationNotSupportedException"/> before any work is done.
/// </para>
/// <para>
/// Experimental (<c>IGNIXAEXP001</c>): the shape of this API may change before it is declared stable.
/// </para>
/// </remarks>
[CLSCompliant(false)]
[Experimental(InMemoryExperimental.DiagnosticId)]
public sealed class InMemorySearchEngine
{
    private readonly IInMemorySearchSource _source;
    private readonly InMemorySearchFeatures _features;
    private readonly InMemoryCompartmentMembership? _compartments;
    private readonly SearchQueryInterpreter _interpreter;

    /// <summary>Creates an engine over <paramref name="source"/> with the given <paramref name="features"/> enabled; compartment searches throw.</summary>
    /// <param name="source">The indexed resources to search; read on every call, never cached by the engine.</param>
    /// <param name="features">The features beyond filtering that searches may use; any other requested feature throws.</param>
    public InMemorySearchEngine(IInMemorySearchSource source, InMemorySearchFeatures features)
    {
        ArgumentNullException.ThrowIfNull(source);

        _source = source;
        _features = features;
        _interpreter = new SearchQueryInterpreter(source, features);
    }

    /// <summary>
    /// Creates an engine like <see cref="InMemorySearchEngine(IInMemorySearchSource, InMemorySearchFeatures)"/> that also
    /// evaluates compartment searches (<see cref="Expressions.CompartmentSearchExpression"/>, such as the one
    /// <c>Patient/123/Observation</c> parses to) with <paramref name="compartments"/>. A compartment search filters the
    /// searched type's own index entries, so it needs no feature flag.
    /// </summary>
    /// <param name="source">The indexed resources to search; read on every call, never cached by the engine.</param>
    /// <param name="features">The features beyond filtering that searches may use; any other requested feature throws.</param>
    /// <param name="compartments">Compartment membership for the FHIR version the source was indexed with.</param>
    public InMemorySearchEngine(IInMemorySearchSource source, InMemorySearchFeatures features, InMemoryCompartmentMembership compartments)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(compartments);

        _source = source;
        _features = features;
        _compartments = compartments;
        _interpreter = new SearchQueryInterpreter(source, features, compartments);
    }

    /// <summary>
    /// The resources of <see cref="SearchOptions.ResourceType"/> matching <see cref="SearchOptions.Expression"/>
    /// (every resource when it is null), ordered by <see cref="SearchOptions.Sort"/> when given and in source order
    /// otherwise.
    /// </summary>
    /// <exception cref="SearchOperationNotSupportedException">
    /// The options ask for a feature that is not enabled, contain an expression in-memory search cannot evaluate, include
    /// through a parameter it cannot follow, or search a compartment the searched type's membership in cannot be evaluated
    /// (each checked here, so such a search fails before it matches anything).
    /// </exception>
    public IReadOnlyList<(ResourceKey Location, IReadOnlyCollection<SearchIndexEntry> Index)> Search(SearchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrEmpty(options.ResourceType, nameof(options));

        Require(InMemorySearchFeatures.Sort, options.Sort.Count > 0, "_sort");
        Require(InMemorySearchFeatures.Include, options.Include.Count > 0, "_include");
        Require(InMemorySearchFeatures.RevInclude, options.RevInclude.Count > 0, "_revinclude");
        InMemoryIncludeResolver.Validate(options.Include, options.RevInclude);
        RefuseUnevaluableCompartments(options.Expression, options.ResourceType);

        // Read once: each predicate in the tree enumerates its input, and a source need not be cheap to enumerate.
        List<(ResourceKey Location, IReadOnlyCollection<SearchIndexEntry> Index)> candidates = [.. _source.Enumerate(options.ResourceType)];
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
    /// <param name="page">The matches being returned.</param>
    /// <param name="options">The parsed search.</param>
    /// <param name="maxIncluded">
    /// The most resources to resolve; resolution stops there, so pass one more than will be returned to learn whether more
    /// exist. Bounds <c>:iterate</c>, which can otherwise reach every resource connected to the page.
    /// </param>
    /// <exception cref="SearchOperationNotSupportedException">The options ask for an include kind that is not enabled.</exception>
    public IReadOnlyList<(ResourceKey Location, IReadOnlyCollection<SearchIndexEntry> Index)> Include(
        IReadOnlyCollection<(ResourceKey Location, IReadOnlyCollection<SearchIndexEntry> Index)> page,
        SearchOptions options,
        int maxIncluded)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfNegative(maxIncluded);

        Require(InMemorySearchFeatures.Include, options.Include.Count > 0, "_include");
        Require(InMemorySearchFeatures.RevInclude, options.RevInclude.Count > 0, "_revinclude");

        return InMemoryIncludeResolver.Resolve(_source, page, options.Include, options.RevInclude, maxIncluded);
    }

    /// <summary>
    /// Refuses a compartment search of a type whose membership cannot be evaluated before anything is matched, so the refusal
    /// depends on the definitions and never on whether the source holds resources of that type.
    /// </summary>
    private void RefuseUnevaluableCompartments(Expressions.Expression? expression, string resourceType)
    {
        if (_compartments is null)
        {
            return;
        }

        foreach (Expressions.CompartmentSearchExpression compartment in Compartments(expression))
        {
            if (_compartments.CompartmentTypes.Contains(compartment.CompartmentType, StringComparer.Ordinal))
            {
                _compartments.ThrowIfUnevaluable(compartment.CompartmentType, resourceType);
            }
        }
    }

    private static IEnumerable<Expressions.CompartmentSearchExpression> Compartments(Expressions.Expression? expression) =>
        expression switch
        {
            Expressions.CompartmentSearchExpression compartment => [compartment],
            Expressions.MultiaryExpression multiary => multiary.Expressions.SelectMany(Compartments),
            Expressions.UnionExpression union => union.Expressions.SelectMany(Compartments),
            Expressions.NotExpression not => Compartments(not.Expression),
            _ => [],
        };

    private void Require(InMemorySearchFeatures feature, bool requested, string parameter)
    {
        if (requested && !_features.HasFlag(feature))
        {
            throw new SearchOperationNotSupportedException($"{parameter} is not enabled for this in-memory search.");
        }
    }
}
