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
/// Represents a variable reference in a FhirPath expression.
/// Examples: %context, %resource, %`ext-patient-birthTime`.
/// </summary>
public class VariableRefExpression : Expression
{
    /// <summary>
    /// Creates a reference to a bare <c>%name</c>. This arity predates <see cref="IsDelimited"/>, and a
    /// caller reaching it has no delimited spelling to report, so bare is the correct value to forward.
    /// </summary>
    public VariableRefExpression(string name, ISourcePositionInfo? location = null)
        : this(name, location, isDelimited: false)
    {
    }

    /// <summary>
    /// Creates a reference, recording whether it was written in the backtick-delimited form.
    /// </summary>
    /// <param name="name">The variable name, with the leading <c>%</c> and any backticks removed.</param>
    /// <param name="location">Where the reference appeared in the source expression.</param>
    /// <param name="isDelimited">Whether the reference was written as <c>%`name`</c>; see <see cref="IsDelimited"/>.</param>
    /// <remarks>
    /// Internal: <paramref name="isDelimited"/> is a parse artifact - whether the author typed backticks -
    /// that only the parser and the engine's own analysis and evaluation need to see. It is not part of
    /// the published AST contract.
    /// </remarks>
    internal VariableRefExpression(string name, ISourcePositionInfo? location, bool isDelimited)
        : base(location)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        IsDelimited = isDelimited;
    }

    /// <summary>
    /// The variable name, with the leading <c>%</c> and any surrounding backticks removed.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Whether the reference was written in the backtick-delimited form, <c>%`name`</c>, rather than bare.
    /// </summary>
    /// <remarks>
    /// The two spellings are not interchangeable: expansion of the <c>vs-</c>/<c>ext-</c> families is
    /// delimited-only, per the FHIR profile of FHIRPath and HAPI's FHIRPathEngine - see
    /// <see cref="StandardConstantFamilies"/>. Ignixa's lexer accepts <c>-</c> in the bare form so names
    /// like <c>%p-inactive</c>, which appear in published cqf-expression content, lex as one token; this
    /// flag is what stops that lexical allowance becoming a resolution rule the spec does not have.
    /// Internal because it records how the expression was <em>written</em>, while the published AST
    /// contract describes what it <em>means</em>. That has a real cost: host-supplied
    /// <see cref="Evaluation.EvaluationContext.Environment"/> bindings resolve <em>before</em> the fixed
    /// FHIRPath constants, so a host that binds <c>vs-x</c> is what bare <c>%vs-x</c> resolves to while
    /// <c>%`vs-x`</c> still expands to the ValueSet URL - and an out-of-assembly
    /// <see cref="Visitors.IFhirPathExpressionVisitor{TContext, TOutput}"/> cannot tell the two apart.
    /// </remarks>
    internal bool IsDelimited { get; }

    public override string ToString() => $"Variable(%{Name})";

    public override TOutput AcceptVisitor<TContext, TOutput>(
        IFhirPathExpressionVisitor<TContext, TOutput> visitor,
        TContext context) => visitor.VisitVariable(this, context);
}
