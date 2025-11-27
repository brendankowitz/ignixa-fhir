/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * FhirPath evaluation context.
 * Stores variables and resources available during expression evaluation.
 */

using Ignixa.Abstractions;

namespace Ignixa.FhirPath.Evaluation;

/// <summary>
/// Context for evaluating FhirPath expressions, including environment variables and resources.
/// </summary>
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
