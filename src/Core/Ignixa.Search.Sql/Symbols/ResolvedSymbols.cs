using Ignixa.Search.Models;

namespace Ignixa.Search.Sql.Symbols;

/// <summary>
/// The resolved symbol table plus the parameters the resolver could not find. Unresolved parameters are
/// reported rather than silently dropped, so callers can explain the failure instead of hitting a
/// KeyNotFoundException later in lowering.
/// </summary>
internal sealed record ResolvedSymbols(SymbolTable Symbols, IReadOnlyList<SearchParameterInfo> Unresolved);
