using Ignixa.Search.Definition;
using Ignixa.Search.Sql.Symbols;

namespace Ignixa.Search.Sql.Compilation;

/// <summary>
/// The three collaborators Resolve needs, grouped so the compiler holds them once from construction
/// rather than threading three optional arguments through every call.
/// </summary>
/// <remarks>
/// Both definition managers are optional because most searches need neither. A compartment search or
/// <c>$everything</c> needs <see cref="CompartmentDefinitionManager"/>; a <c>_not-referenced</c> path
/// filter needs <see cref="SearchParameterDefinitionManager"/>. Each throws naming itself when a query
/// requires it and it was not supplied.
/// </remarks>
internal sealed record SymbolResolution(
    ISymbolResolver Resolver,
    ICompartmentDefinitionManager? CompartmentDefinitionManager = null,
    ISearchParameterDefinitionManager? SearchParameterDefinitionManager = null);
