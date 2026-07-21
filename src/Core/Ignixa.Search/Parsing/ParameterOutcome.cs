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

    /// <summary>The parameter was dropped by FHIR lenient handling rather than failing the request.</summary>
    public sealed record Ignored(string Reason, SourceSpan? Span) : ParameterOutcome;

    /// <summary>The parameter failed at a named stage.</summary>
    public sealed record Failed(TraceStage Stage, string Message, SourceSpan? Span) : ParameterOutcome;
}
