using Ignixa.Search.Parsing;
using Ignixa.Search.Sql.Ast;

namespace Ignixa.Search.Sql.Tracing;

/// <summary>
/// A full pipeline trace: per-parameter outcomes plus the plan and SQL they produced.
/// A null <see cref="Plan"/> means compilation stopped before or during Lower. Whenever it stopped — on an
/// unresolved symbol, or on a Lower or Emit failure — <see cref="Failure"/> carries that failure's stage and
/// message, even where nothing could be attributed to a parameter and so every parameter outcome reads
/// Compiled. Read <see cref="Failure"/> alongside <see cref="Parameters"/>, never parameter outcomes alone.
/// <see cref="Implicit"/> covers the opposite gap: control values that took effect without appearing in
/// <see cref="Parameters"/> at all, because the caller never sent them.
/// </summary>
public sealed record SearchTrace(
    string? ResourceType,
    IReadOnlyList<ParameterTrace> Parameters,
    QueryPlanTrace? Plan,
    EmittedSqlTrace? Sql)
{
    /// <summary>
    /// Why compilation stopped, or null when it did not. Declared outside the positional list alongside
    /// <see cref="Implicit"/>: the constructor stays the four always-meaningful fields, so a further optional
    /// field can be added without touching every construction site.
    /// </summary>
    public TraceFailure? Failure { get; init; }

    /// <summary>
    /// Control parameters the server supplied itself. Declared outside the positional list, and empty
    /// rather than null, so every existing construction site keeps compiling without any caller having to
    /// null-check a collection.
    /// </summary>
    public IReadOnlyList<ImplicitParameter> Implicit { get; init; } = [];

    /// <summary>
    /// The real <see cref="QueryPlan"/> Lower produced, or null when compilation stopped before Lower ran.
    /// Declared outside the positional list for the same reason as <see cref="Failure"/>/<see cref="Implicit"/>.
    /// A production caller that needs to branch on the plan's own structure (e.g. whether <c>Includes</c> or
    /// <c>Sort</c> is populated, to pick the right result-row shape) reads this directly, rather than
    /// re-deriving it from the caller's own <c>SearchOptions</c> — <c>Lower.BuildIncludeStages</c> can drop a
    /// degenerate stage and return null even when the caller's <c>options.Include</c> is non-empty, so the
    /// two can diverge; <see cref="QueryPlanTrace"/> (<see cref="Plan"/>) is a display-only projection with
    /// no <c>Includes</c>/<c>Sort</c> structure of its own and cannot substitute for this.
    /// </summary>
    public QueryPlan? CompiledPlan { get; init; }
}
