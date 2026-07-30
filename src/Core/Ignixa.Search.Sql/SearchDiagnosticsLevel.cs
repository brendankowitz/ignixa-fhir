namespace Ignixa.Search.Sql;

/// <summary>
/// How much a compile records about its own work. The default is <see cref="None"/>: diagnostics cost
/// allocations on every compile, and a production search path wants none of them.
/// </summary>
public enum SearchDiagnosticsLevel
{
    /// <summary>Nothing is captured. No outcome list is passed to the builder and the plan explainer never runs.</summary>
    None = 0,

    /// <summary>Per-parameter outcomes, implicit parameters, and failure attribution.</summary>
    Parameters,

    /// <summary>Everything in <see cref="Parameters"/>, plus plan explain rows and SQL text ranges.</summary>
    Full,
}
