using Ignixa.Search.Parsing;

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
    string ResourceType,
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
}
