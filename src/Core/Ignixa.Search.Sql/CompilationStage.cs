namespace Ignixa.Search.Sql;

/// <summary>Which stage of the compiler produced a failure.</summary>
public enum CompilationStage
{
    /// <summary>The options builder turning query parameters into a <c>SearchOptions</c>.</summary>
    Build,

    /// <summary>Resolving search parameters, compartments, and access constraints to storage symbols.</summary>
    Resolve,

    /// <summary>Turning the bound expression tree into a <see cref="Ast.QueryPlan"/>.</summary>
    Lower,

    /// <summary>Emitting SQL text and bound parameters from the plan.</summary>
    Emit,
}
