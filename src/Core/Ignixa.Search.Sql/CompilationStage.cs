namespace Ignixa.Search.Sql;

/// <summary>Which stage of the compiler produced a failure.</summary>
public enum CompilationStage
{
    /// <summary>
    /// Turning caller input into the compiler's request, before any part of the query is examined: the options
    /// builder parsing query parameters into a <c>SearchOptions</c>, and mapping that <c>SearchOptions</c> onto
    /// the compilation context. A malformed input reaches the caller here, not at a later stage.
    /// </summary>
    Build,

    /// <summary>Resolving search parameters, compartments, and access constraints to storage symbols.</summary>
    Resolve,

    /// <summary>Turning the bound expression tree into a <see cref="Ast.QueryPlan"/>.</summary>
    Lower,

    /// <summary>Emitting SQL text and bound parameters from the plan.</summary>
    Emit,
}
