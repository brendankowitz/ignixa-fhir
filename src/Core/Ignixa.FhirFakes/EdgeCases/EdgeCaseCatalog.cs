// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.FhirFakes.EdgeCases.Strategies;

namespace Ignixa.FhirFakes.EdgeCases;

/// <summary>
/// An instance-scoped registry of edge-case strategies. Built-ins are registered via
/// <see cref="CreateDefault"/>; consumers add their own via <see cref="Register"/>.
/// Selection (<see cref="Resolve"/>) accepts family names, specific categories, or nothing (= all).
/// </summary>
public sealed class EdgeCaseCatalog
{
    private readonly List<IEdgeCaseStrategy> _strategies = [];

    /// <summary>Creates an empty catalog. Prefer <see cref="CreateDefault"/> for the built-in set.</summary>
    public EdgeCaseCatalog()
    {
    }

    /// <summary>Creates a catalog pre-populated with all built-in strategies (unicode, temporal, and string-boundary families).</summary>
    public static EdgeCaseCatalog CreateDefault()
    {
        var catalog = new EdgeCaseCatalog();
        catalog.RegisterBuiltIns();
        return catalog;
    }

    /// <summary>Registers a strategy. Later registrations are returned after earlier ones in <see cref="All"/>.</summary>
    public EdgeCaseCatalog Register(IEdgeCaseStrategy strategy)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        _strategies.Add(strategy);
        return this;
    }

    /// <summary>Returns all registered strategies in registration order.</summary>
    public IReadOnlyList<IEdgeCaseStrategy> All() => _strategies;

    /// <summary>
    /// Resolves selectors to matching strategies and reports which selectors matched nothing.
    /// A selector may be a family name ("unicode"), a specific category ("unicode.rtl"),
    /// or the set may be null/empty meaning ALL strategies. Matching is case-insensitive.
    /// </summary>
    /// <param name="selectors">Selectors to resolve.</param>
    /// <param name="unmatched">Selectors that produced no matches.</param>
    public IReadOnlyList<IEdgeCaseStrategy> Resolve(IEnumerable<string>? selectors, out IReadOnlyList<string> unmatched)
    {
        var selectorList = selectors?.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        if (selectorList is null || selectorList.Count == 0)
        {
            unmatched = [];
            return _strategies;
        }

        var matched = _strategies.Where(s => MatchesAny(s, selectorList)).ToList();
        unmatched = selectorList
            .Where(sel => !_strategies.Any(s => Matches(s, sel)))
            .ToList();
        return matched;
    }

    /// <summary>
    /// Resolves selectors to matching strategies. A selector may be a family name ("unicode"),
    /// a specific category ("unicode.rtl"), or the set may be null/empty meaning ALL strategies.
    /// Matching is case-insensitive. Unknown selectors match nothing.
    /// </summary>
    public IReadOnlyList<IEdgeCaseStrategy> Resolve(IEnumerable<string>? selectors)
    {
        var selectorList = selectors?.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        if (selectorList is null || selectorList.Count == 0)
        {
            return _strategies;
        }

        return _strategies.Where(s => MatchesAny(s, selectorList)).ToList();
    }

    private static bool MatchesAny(IEdgeCaseStrategy strategy, List<string> selectors)
        => selectors.Any(selector => Matches(strategy, selector));

    private static bool Matches(IEdgeCaseStrategy strategy, string selector)
    {
        if (string.Equals(strategy.Category, selector, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(strategy.Family.ToString(), selector, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Allow category-prefix matching so "string" selects "string.max-length", "string.injection-like", etc.
        return strategy.Category.StartsWith(selector + ".", StringComparison.OrdinalIgnoreCase);
    }

    private void RegisterBuiltIns()
    {
        Register(new CjkUnicodeStrategy());
        Register(new RtlUnicodeStrategy());
        Register(new CombiningUnicodeStrategy());
        Register(new EmojiUnicodeStrategy());
        Register(new ZeroWidthUnicodeStrategy());
        Register(new MultiScriptLongUnicodeStrategy());
        Register(new LeapYearTemporalStrategy());
        Register(new YearBoundaryTemporalStrategy());
        Register(new FarPastTemporalStrategy());
        Register(new FarFutureTemporalStrategy());
        Register(new PartialPrecisionTemporalStrategy());
        Register(new MaxLengthStringStrategy());
        Register(new InjectionLikeStringStrategy());
        Register(new ControlCharsStringStrategy());
        Register(new EmptyPresentStringStrategy());
        Register(new WhitespaceOnlyStringStrategy());
    }
}
