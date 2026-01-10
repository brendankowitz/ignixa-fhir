/*
 * Copyright (c) 2015, Firely (info@fire.ly) and contributors
 * Copyright (c) 2025, Ignixa Contributors
 *
 * This file is based on the Firely .NET SDK.
 * Licensed under the BSD 3-Clause license.
 */

using Ignixa.FhirPath.Visitors;

namespace Ignixa.FhirPath.Expressions;

/// <summary>
/// Base class for all FhirPath expression nodes in the abstract syntax tree (AST).
/// </summary>
public abstract class Expression
{
    protected Expression()
    {
    }

    protected Expression(ISourcePositionInfo? location)
    {
        Location = location;
    }

    /// <summary>
    /// Location information for this expression component in the parsed FhirPath expression.
    /// </summary>
    public ISourcePositionInfo? Location { get; set; }

    /// <summary>
    /// Original source text for this expression (preserves whitespace and comments for round-tripping).
    /// Only populated when FhirPathCompiler is constructed with preserveTrivia = true.
    /// </summary>
    public string? SourceText { get; set; }

    /// <summary>
    /// Sets position information and returns this expression for fluent chaining.
    /// </summary>
    public T SetPosition<T>(ISourcePositionInfo location) where T : Expression
    {
        Location = location;
        return (T)this;
    }

    /// <summary>
    /// Converts this expression back to FhirPath syntax.
    /// If SourceText is available (preserveTrivia mode), returns the original text.
    /// Otherwise, reconstructs from the AST using ToString().
    /// </summary>
    public string ToFhirPath() => SourceText ?? ToString() ?? string.Empty;

    /// <summary>
    /// Accepts a visitor for traversing the expression tree.
    /// </summary>
    /// <typeparam name="TContext">The context type passed through the visitor</typeparam>
    /// <typeparam name="TOutput">The output type produced by the visitor</typeparam>
    /// <param name="visitor">The visitor to accept</param>
    /// <param name="context">The context to pass to the visitor</param>
    /// <returns>The output produced by visiting this expression</returns>
    public abstract TOutput AcceptVisitor<TContext, TOutput>(
        IFhirPathExpressionVisitor<TContext, TOutput> visitor,
        TContext context);
}
