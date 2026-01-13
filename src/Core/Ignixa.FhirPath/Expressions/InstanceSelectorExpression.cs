/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * FhirPath instance selector expression for creating FHIR objects inline.
 * Syntax: TypeName { element: value, element: value, ... }
 */

using Ignixa.FhirPath.Visitors;

namespace Ignixa.FhirPath.Expressions;

/// <summary>
/// Represents an instance selector expression that creates a new FHIR object.
/// Examples:
/// - Coding { system: 'http://example.org', code: 'c1' }
/// - FHIR.Identifier { system: 'http://example.org', value: 'N0001' }
/// - Period {:}  (empty object)
/// </summary>
public class InstanceSelectorExpression : Expression
{
    public InstanceSelectorExpression(
        string typeName,
        IEnumerable<ElementAssignment> elements,
        string? namespacePrefix = null,
        bool isEmpty = false,
        ISourcePositionInfo? location = null) : base(location)
    {
        TypeName = typeName ?? throw new ArgumentNullException(nameof(typeName));
        Elements = elements?.ToList() ?? [];
        NamespacePrefix = namespacePrefix;
        IsEmpty = isEmpty;
    }

    /// <summary>Name of the type to create (e.g., "Identifier", "Coding")</summary>
    public string TypeName { get; }

    /// <summary>Optional namespace prefix (e.g., "FHIR" from "FHIR.Identifier")</summary>
    public string? NamespacePrefix { get; }

    /// <summary>Element assignments for the new object</summary>
    public IReadOnlyList<ElementAssignment> Elements { get; }

    /// <summary>True if this is an empty object initializer (e.g., "Period {:}")</summary>
    public bool IsEmpty { get; }

    /// <summary>Full type name including namespace if present</summary>
    public string FullTypeName => NamespacePrefix != null
        ? $"{NamespacePrefix}.{TypeName}"
        : TypeName;

    public override string ToString()
    {
        if (IsEmpty)
            return $"{FullTypeName} {{:}}";

        if (Elements.Count == 0)
            return $"{FullTypeName} {{}}";

        var assignments = string.Join(", ", Elements.Select(e => e.ToString()));
        return $"{FullTypeName} {{ {assignments} }}";
    }

    public override TOutput AcceptVisitor<TContext, TOutput>(
        IFhirPathExpressionVisitor<TContext, TOutput> visitor,
        TContext context) => visitor.VisitInstanceSelector(this, context);
}

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
