using Ignixa.Search.Definition;
using Ignixa.Search.Sql.Symbols;

namespace Ignixa.Search.Sql.Compilation;

/// <summary>
/// The three collaborators Resolve needs, held once from construction rather than threaded through every
/// call. Both definition managers are optional (most searches need neither); each throws naming itself when
/// a compartment/$everything or <c>_not-referenced</c> query requires it and it was not supplied.
/// </summary>
internal sealed record SymbolResolution(
    ISymbolResolver Resolver,
    ICompartmentDefinitionManager? CompartmentDefinitionManager = null,
    ISearchParameterDefinitionManager? SearchParameterDefinitionManager = null)
{
    /// <summary>Guarded because <c>Ignixa.Search</c> compiles nullable-disabled, so a null could reach here
    /// and surface as a <see cref="NullReferenceException"/> deep inside resolution.</summary>
    public ISymbolResolver Resolver { get; } = Resolver ?? throw new ArgumentNullException(nameof(Resolver));
}
