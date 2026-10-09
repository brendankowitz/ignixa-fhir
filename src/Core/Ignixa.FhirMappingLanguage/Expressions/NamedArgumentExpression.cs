/*
 * Copyright (c) 2026, Ignixa Contributors
 */

namespace Ignixa.FhirMappingLanguage.Expressions;

/// <summary>
/// A named transform parameter, such as <c>map_uri: 'http://example.org/map'</c>.
/// </summary>
public sealed class NamedArgumentExpression(string name, Expression value) : Expression
{
    public string Name { get; } = name;
    public Expression Value { get; } = value;
}
