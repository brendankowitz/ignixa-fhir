using Ignixa.Search.Parsing;

namespace Ignixa.Search.Sql.Tracing;

/// <summary>
/// A full pipeline trace: per-parameter outcomes plus the plan and SQL they produced.
/// A null <see cref="Plan"/> means compilation stopped before or during Lower, and is the authoritative
/// success signal -- read it alongside <see cref="Parameters"/>, never instead of it. A Lower or Emit
/// failure that names no attributable source span leaves every parameter outcome Compiled, so parameter
/// outcomes alone can read as a success the query never had.
/// </summary>
public sealed record SearchTrace(
    string ResourceType,
    IReadOnlyList<ParameterTrace> Parameters,
    QueryPlanTrace? Plan,
    EmittedSqlTrace? Sql);
