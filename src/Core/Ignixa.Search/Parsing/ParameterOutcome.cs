// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Ignixa.Search.Expressions;

namespace Ignixa.Search.Parsing;

/// <summary>What happened to one search parameter during parsing.</summary>
/// <remarks>
/// A closed union: the private constructor keeps the nested records the only possible cases, so a consumer
/// switching over them can rely on exhaustiveness. Nested types can still reach it, external ones cannot.
/// </remarks>
public abstract record ParameterOutcome
{
    private ParameterOutcome()
    {
    }

    /// <summary>The parameter parsed and contributed to the search expression.</summary>
    public sealed record Compiled : ParameterOutcome;

    /// <summary>
    /// The parameter compiled, but to a predicate that can never match — a value the server has no symbol
    /// for, such as an unknown token system or quantity code.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="Ignored"/> (the parameter did not reach the query at all) and from
    /// <see cref="Failed"/> (the request is in error). The query is well formed and runs; it is just
    /// structurally incapable of returning a row, which is otherwise visible only as a <c>1 = 0</c> buried
    /// in the emitted SQL.
    /// </remarks>
    public sealed record KnownMiss(string Reason, SourceSpan? Span) : ParameterOutcome;

    /// <summary>The parameter was dropped by FHIR lenient handling rather than failing the request.</summary>
    public sealed record Ignored(string Reason, SourceSpan? Span) : ParameterOutcome;

    /// <summary>The parameter failed at a named stage.</summary>
    public sealed record Failed(TraceStage Stage, string Message, SourceSpan? Span) : ParameterOutcome;
}
