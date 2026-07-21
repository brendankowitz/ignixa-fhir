using Ignixa.Search.Parsing;

namespace Ignixa.Search.Sql.Tracing;

/// <summary>
/// A full pipeline trace: per-parameter outcomes plus the plan and SQL they produced.
/// A null <see cref="Plan"/> means compilation stopped before or during Lower. When it stopped on a Lower or
/// Emit failure, <see cref="Failure"/> always carries that failure's stage and message, even for the guards
/// that name no parameter and so leave every parameter outcome Compiled — read <see cref="Failure"/>
/// alongside <see cref="Parameters"/>, never parameter outcomes alone.
/// <see cref="Implicit"/> covers the opposite gap: control values that took effect without appearing in
/// <see cref="Parameters"/> at all, because the caller never sent them.
/// </summary>
public sealed record SearchTrace(
    string ResourceType,
    IReadOnlyList<ParameterTrace> Parameters,
    QueryPlanTrace? Plan,
    EmittedSqlTrace? Sql,
    TraceFailure? Failure = null)
{
    /// <summary>
    /// Control parameters the server supplied itself. Declared outside the positional list, and empty
    /// rather than null, so every existing construction site keeps compiling without any caller having to
    /// null-check a collection.
    /// </summary>
    public IReadOnlyList<ImplicitParameter> Implicit { get; init; } = [];
}
