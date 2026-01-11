/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * FhirPath evaluation context.
 * Immutable context for expression evaluation - follows the same pattern as AnalysisContext.
 */

using System.Collections.Immutable;
using Ignixa.Abstractions;

namespace Ignixa.FhirPath.Evaluation;

/// <summary>
/// Immutable context for evaluating FhirPath expressions at runtime.
/// </summary>
/// <remarks>
/// <para>
/// <b>Immutable Design:</b>
/// </para>
/// <para>
/// This context follows the same immutable pattern as <see cref="Analysis.AnalysisContext"/>.
/// All state changes create new context instances via fluent methods like
/// <see cref="WithFocus"/>, <see cref="PushThis"/>, <see cref="WithEnvironmentVariable"/>.
/// </para>
/// <para>
/// <b>Runtime vs Static Analysis Context:</b>
/// </para>
/// <para>
/// This class is designed for <b>runtime evaluation</b> where actual IElement values are available.
/// For <b>static analysis</b> (type inference, validation), use
/// <see cref="Analysis.AnalysisContext"/> which provides immutable context stacks
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
///   <item><description><c>%ucum</c>, <c>%sct</c>, <c>%loinc</c>: Terminology URIs via <see cref="WithEnvironmentVariable"/></description></item>
/// </list>
/// <para>
/// <b>Context Propagation in Nested Expressions:</b>
/// </para>
/// <para>
/// Functions like <c>where()</c>, <c>select()</c>, and <c>exists()</c> evaluate their arguments
/// in a modified context where <c>$this</c> refers to the current iteration item.
/// This is handled immutably using <see cref="PushThis"/> and the stack-based pattern:
/// </para>
/// <code>
/// // Create new context with $this bound to current element
/// var innerContext = context.PushThis(currentElement);
/// var result = evaluateExpression([currentElement], criteria, innerContext);
/// // Original context is unchanged - no need for save/restore
/// </code>
/// </remarks>
public record EvaluationContext
{
    private EvaluationContext(
        ImmutableList<IElement> focus,
        ImmutableStack<IElement> thisStack,
        ImmutableStack<IElement> indexStack,
        ImmutableDictionary<string, ImmutableList<IElement>> environment,
        IElement? resource,
        IElement? rootResource)
    {
        Focus = focus;
        ThisStack = thisStack;
        IndexStack = indexStack;
        Environment = environment;
        Resource = resource;
        RootResource = rootResource;
    }

    /// <summary>
    /// Creates a new empty evaluation context.
    /// </summary>
    public EvaluationContext() : this(
        ImmutableList<IElement>.Empty,
        ImmutableStack<IElement>.Empty,
        ImmutableStack<IElement>.Empty,
        ImmutableDictionary<string, ImmutableList<IElement>>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase),
        null,
        null)
    {
    }

    /// <summary>
    /// The current focus (input elements) being evaluated.
    /// Immutable - use <see cref="WithFocus"/> to create a new context with different focus.
    /// </summary>
    public ImmutableList<IElement> Focus { get; init; }

    /// <summary>
    /// Stack of $this bindings for nested expressions (where, select, exists, etc.).
    /// Use <see cref="PushThis"/> to add a binding and access via <see cref="GetThis"/>.
    /// </summary>
    public ImmutableStack<IElement> ThisStack { get; init; }

    /// <summary>
    /// Stack of $index bindings for indexed iterations (aggregate, etc.).
    /// </summary>
    public ImmutableStack<IElement> IndexStack { get; init; }

    /// <summary>
    /// Environment variables available to FhirPath expressions.
    /// Variable names map to collections of IElement values.
    /// Immutable - use <see cref="WithEnvironmentVariable"/> to add variables.
    /// </summary>
    public ImmutableDictionary<string, ImmutableList<IElement>> Environment { get; init; }

    /// <summary>
    /// The data represented by %resource variable.
    /// </summary>
    public IElement? Resource { get; init; }

    /// <summary>
    /// The data represented by %rootResource variable.
    /// </summary>
    public IElement? RootResource { get; init; }

    /// <summary>
    /// Creates a new context with the specified focus.
    /// </summary>
    public EvaluationContext WithFocus(IEnumerable<IElement> focus)
    {
        return this with { Focus = focus.ToImmutableList() };
    }

    /// <summary>
    /// Creates a new context with a single element as focus.
    /// </summary>
    public EvaluationContext WithFocus(IElement element)
    {
        return this with { Focus = [element] };
    }

    /// <summary>
    /// Pushes a $this binding onto the stack.
    /// Used by where(), select(), exists() etc. for iteration context.
    /// </summary>
    public EvaluationContext PushThis(IElement element)
    {
        return this with { ThisStack = ThisStack.Push(element) };
    }

    /// <summary>
    /// Creates a context with the top $this binding removed.
    /// Note: In most cases, you don't need this - just discard the inner context.
    /// </summary>
    public EvaluationContext PopThis()
    {
        if (ThisStack.IsEmpty)
        {
            return this;
        }

        return this with { ThisStack = ThisStack.Pop() };
    }

    /// <summary>
    /// Gets the current $this value, or null if no binding exists.
    /// </summary>
    public IElement? GetThis()
    {
        return ThisStack.IsEmpty ? null : ThisStack.Peek();
    }

    /// <summary>
    /// Pushes an $index binding onto the stack.
    /// </summary>
    public EvaluationContext PushIndex(int index)
    {
        var indexElement = new IndexElement(index);
        return this with { IndexStack = IndexStack.Push(indexElement) };
    }

    /// <summary>
    /// Gets the current $index value, or null if no binding exists.
    /// </summary>
    public int? GetIndex()
    {
        if (IndexStack.IsEmpty)
        {
            return null;
        }

        var element = IndexStack.Peek();
        return element.Value is int i ? i : null;
    }

    /// <summary>
    /// Creates a new context with the specified environment variable set.
    /// </summary>
    public EvaluationContext WithEnvironmentVariable(string name, IElement element)
    {
        return this with
        {
            Environment = Environment.SetItem(name, [element])
        };
    }

    /// <summary>
    /// Creates a new context with the specified environment variable set to a collection.
    /// </summary>
    public EvaluationContext WithEnvironmentVariable(string name, IEnumerable<IElement> elements)
    {
        return this with
        {
            Environment = Environment.SetItem(name, elements.ToImmutableList())
        };
    }

    /// <summary>
    /// Creates a new context with the specified environment variable removed.
    /// </summary>
    public EvaluationContext WithoutEnvironmentVariable(string name)
    {
        return this with
        {
            Environment = Environment.Remove(name)
        };
    }

    /// <summary>
    /// Creates a new context with the specified resource.
    /// </summary>
    public EvaluationContext WithResource(IElement resource)
    {
        return this with { Resource = resource };
    }

    /// <summary>
    /// Creates a new context with the specified root resource.
    /// </summary>
    public EvaluationContext WithRootResource(IElement rootResource)
    {
        return this with { RootResource = rootResource };
    }

    /// <summary>
    /// Gets an environment variable value.
    /// Returns the single element if collection has one item, otherwise returns the list.
    /// </summary>
    public object? GetEnvironmentVariable(string name)
    {
        if (name == "this")
        {
            return GetThis();
        }

        if (name == "index")
        {
            var idx = GetIndex();
            return idx.HasValue ? new IndexElement(idx.Value) : null;
        }

        if (Environment.TryGetValue(name, out var value))
        {
            return value.Count == 1 ? value[0] : value;
        }

        return null;
    }

    /// <summary>
    /// Simple implementation of IElement for index values.
    /// </summary>
    private sealed class IndexElement(int value) : IElement
    {
        public string Name => string.Empty;
        public string InstanceType => "integer";
        public object Value { get; } = value;
        public string Location => string.Empty;
        public IType? Type => null;

        public IReadOnlyList<IElement> Children(string? name = null) => [];

        public T? Meta<T>() where T : class => null;
    }
}
