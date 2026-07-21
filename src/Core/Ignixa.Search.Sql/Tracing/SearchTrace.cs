using Ignixa.Search.Parsing;

namespace Ignixa.Search.Sql.Tracing;

/// <summary>A full pipeline trace: per-parameter outcomes plus the plan and SQL they produced.</summary>
public sealed record SearchTrace(
    string ResourceType,
    IReadOnlyList<ParameterTrace> Parameters,
    QueryPlanTrace? Plan,
    EmittedSqlTrace? Sql);
