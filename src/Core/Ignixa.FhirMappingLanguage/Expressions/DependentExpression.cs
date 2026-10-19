/*
 * Copyright (c) 2026, Ignixa Contributors
 */

namespace Ignixa.FhirMappingLanguage.Expressions;

/// <summary>
/// A sequence of dependent group invocations, optionally followed by nested rules.
/// </summary>
public sealed class DependentExpression(
    IEnumerable<GroupInvocationExpression> invocations,
    RuleSetExpression? rules = null) : Expression
{
    public IReadOnlyList<GroupInvocationExpression> Invocations { get; } = invocations.ToList();
    public RuleSetExpression? Rules { get; } = rules;
}
