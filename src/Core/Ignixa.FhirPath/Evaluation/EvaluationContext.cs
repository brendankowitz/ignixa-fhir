/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * FhirPath evaluation context.
 * Stores variables and resources available during expression evaluation.
 */

using Ignixa.Abstractions;

namespace Ignixa.FhirPath.Evaluation;

/// <summary>
/// Context for evaluating FhirPath expressions at runtime, including environment variables and resources.
/// </summary>
/// <remarks>
/// <para>
/// <b>Runtime vs Static Analysis Context:</b>
/// </para>
/// <para>
/// This class is designed for <b>runtime evaluation</b> where actual IElement values are available.
/// For <b>static analysis</b> (type inference, validation), use
/// <see cref="Visitors.FhirPathVisitorContext"/> which provides immutable context stacks
/// and type-based variable storage.
/// </para>
/// <para>
/// <b>Variable Registration:</b>
/// </para>
/// <para>
/// Standard FhirPath variables are supported:
/// </para>
/// <list type="bullet">
///   <item><description><c>%resource</c>: Set via <see cref="Resource"/> property</description></item>
///   <item><description><c>%rootResource</c>: Set via <see cref="RootResource"/> property</description></item>
///   <item><description><c>%context</c>: Typically same as %resource at root</description></item>
///   <item><description><c>%ucum</c>, <c>%sct</c>, <c>%loinc</c>: Terminology URIs via <see cref="SetEnvironmentVariable"/></description></item>
/// </list>
/// <para>
/// <b>Context Propagation in Nested Expressions:</b>
/// </para>
/// <para>
/// Functions like <c>where()</c>, <c>select()</c>, and <c>exists()</c> evaluate their arguments
/// in a modified context where <c>$this</c> refers to the current iteration item.
/// The evaluator handles this by temporarily setting the "this" environment variable
/// (see <see cref="Functions.CollectionFunctions.Where"/> for implementation pattern).
/// </para>
/// <para>
/// Example save/restore pattern used in where():
/// </para>
/// <code>
/// var oldThis = context.GetEnvironmentVariable("this");
/// context.SetEnvironmentVariable("this", currentElement);
/// try
/// {
///     var result = evaluateExpression([currentElement], criteria, context);
///     // ... process result
/// }
/// finally
/// {
///     if (oldThis != null)
///         context.SetEnvironmentVariable("this", oldThis);
///     else
///         context.RemoveEnvironmentVariable("this");
/// }
/// </code>
/// </remarks>
public class EvaluationContext
{
    /// <summary>
    /// Environment variables available to FhirPath expressions.
    /// Variable names map to collections of IElement values.
    /// </summary>
    public IDictionary<string, IEnumerable<IElement>> Environment { get; } = new Dictionary<string, IEnumerable<IElement>>();

    /// <summary>
    /// The data represented by %resource variable.
    /// </summary>
    public IElement? Resource { get; set; }

    /// <summary>
    /// The data represented by %rootResource variable.
    /// </summary>
    public IElement? RootResource { get; set; }

    /// <summary>
    /// The current focus (input elements) being evaluated.
    /// Used by the visitor pattern to pass focus through the evaluation chain.
    /// </summary>
    public IEnumerable<IElement> Focus { get; set; } = [];

    /// <summary>
    /// Gets an environment variable value.
    /// </summary>
    public object? GetEnvironmentVariable(string name)
    {
        if (Environment.TryGetValue(name, out var value))
        {
            var list = value.ToList();
            return list.Count == 1 ? list[0] : list;
        }
        return null;
    }

    /// <summary>
    /// Sets an environment variable value.
    /// </summary>
    public void SetEnvironmentVariable(string name, object value)
    {
        if (value is IElement element)
        {
            Environment[name] = [element];
        }
        else if (value is IEnumerable<IElement> elements)
        {
            Environment[name] = elements;
        }
        else
        {
            throw new ArgumentException($"Environment variable value must be IElement or IEnumerable<IElement>, got {value?.GetType().Name}");
        }
    }

    /// <summary>
    /// Removes an environment variable.
    /// </summary>
    public void RemoveEnvironmentVariable(string name)
    {
        Environment.Remove(name);
    }
}
