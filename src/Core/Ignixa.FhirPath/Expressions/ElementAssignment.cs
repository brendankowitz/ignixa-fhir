/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * A single element assignment within a FhirPath instance selector.
 * Syntax: element: value
 */

namespace Ignixa.FhirPath.Expressions;

/// <summary>
/// Represents a single element assignment within an instance selector.
/// Example: system: 'http://example.org'
/// </summary>
public class ElementAssignment
{
    public ElementAssignment(string elementName, Expression valueExpression)
    {
        ElementName = elementName ?? throw new ArgumentNullException(nameof(elementName));
        ValueExpression = valueExpression ?? throw new ArgumentNullException(nameof(valueExpression));
    }

    /// <summary>Name of the element being assigned</summary>
    public string ElementName { get; }

    /// <summary>Expression that produces the value for this element</summary>
    public Expression ValueExpression { get; }

    public override string ToString() => $"{ElementName}: {ValueExpression}";
}
