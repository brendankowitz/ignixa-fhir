using Ignixa.Search.Expressions;
using Ignixa.Search.Parsing;

namespace Ignixa.Search.Sql.Tracing;

/// <summary>
/// A Lower- or Emit-stage failure that stopped compilation, recorded whether or not it could be attributed
/// to a single parameter. Many guards throw from outside the two lowering dispatchers (sort-key caps, chain
/// depth, wildcard-compartment combinations) and so name no parameter at all; without this the message would
/// be lost and the trace would show only an unexplained absent plan.
/// </summary>
public sealed record TraceFailure(TraceStage Stage, string Message, SourceSpan? Span);
