// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Immutable;
using Ignixa.Specification;

namespace Ignixa.FhirPath.Visitors;

/// <summary>
/// Immutable context for FhirPath expression visitors, particularly type inference visitors.
/// </summary>
/// <remarks>
/// <para>
/// This context object is designed for static analysis visitors (type checking, validation)
/// rather than the runtime evaluator. It provides:
/// </para>
/// <list type="bullet">
///   <item><description>Immutable variable storage for %resource, %rootResource, %context</description></item>
///   <item><description>Context stacks for nested expressions (where, select, aggregate)</description></item>
///   <item><description>Type information flow through the expression tree</description></item>
/// </list>
/// <para>
/// <b>Context Stack Usage:</b>
/// </para>
/// <para>
/// When evaluating expressions like <c>Patient.name.where(use = 'official')</c>, the criteria
/// expression <c>use = 'official'</c> evaluates in a different context:
/// </para>
/// <list type="bullet">
///   <item><description><c>PropertyContext</c>: The collection being filtered (HumanName[])</description></item>
///   <item><description><c>ExpressionContext</c>: Single item context for $this (HumanName)</description></item>
/// </list>
/// <para>
/// The stacks allow nested function calls to properly resolve $this, $that, $total, and $index.
/// </para>
/// <para>
/// <b>Immutability:</b>
/// </para>
/// <para>
/// All mutation operations return new instances, enabling safe parallel traversal and
/// simplified reasoning about state. Use <c>With*</c> methods to create modified contexts.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Creating a context for Patient validation
/// var context = FhirPathVisitorContext.Create(schema, "Patient");
///
/// // Pushing context for where() clause argument
/// var innerContext = context
///     .PushPropertyContext(focusTypes)
///     .PushExpressionContext(focusTypes.AsSingle());
///
/// // After processing argument, pop the stacks
/// var restoredContext = innerContext
///     .PopExpressionContext()
///     .PopPropertyContext();
/// </code>
/// </example>
internal sealed record FhirPathVisitorContext
{
    /// <summary>
    /// Creates a new empty context without schema binding.
    /// </summary>
    public FhirPathVisitorContext()
    {
        Variables = ImmutableDictionary<string, FhirPathTypeSet>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase);
        DefinedVariables = ImmutableDictionary<string, FhirPathTypeSet>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase);
        PropertyContextStack = ImmutableStack<FhirPathTypeSet>.Empty;
        ExpressionContextStack = ImmutableStack<FhirPathTypeSet>.Empty;
        AggregateTotalStack = ImmutableStack<FhirPathTypeSet>.Empty;
    }

    private FhirPathVisitorContext(
        IFhirSchemaProvider? schema,
        ImmutableDictionary<string, FhirPathTypeSet> variables,
        ImmutableDictionary<string, FhirPathTypeSet> definedVariables,
        ImmutableStack<FhirPathTypeSet> propertyContextStack,
        ImmutableStack<FhirPathTypeSet> expressionContextStack,
        ImmutableStack<FhirPathTypeSet> aggregateTotalStack,
        FhirPathTypeSet? rootContext)
    {
        Schema = schema;
        Variables = variables;
        DefinedVariables = definedVariables;
        PropertyContextStack = propertyContextStack;
        ExpressionContextStack = expressionContextStack;
        AggregateTotalStack = aggregateTotalStack;
        RootContext = rootContext;
    }

    /// <summary>
    /// The FHIR schema provider for type resolution.
    /// </summary>
    public IFhirSchemaProvider? Schema { get; init; }

    /// <summary>
    /// Standard FhirPath variables (%resource, %rootResource, %context, %ucum, %sct, %loinc).
    /// </summary>
    /// <remarks>
    /// These are registered via <see cref="WithVariable"/> and accessed when
    /// visiting <see cref="Expressions.VariableRefExpression"/>.
    /// </remarks>
    public ImmutableDictionary<string, FhirPathTypeSet> Variables { get; init; }

    /// <summary>
    /// User-defined variables from defineVariable() function.
    /// </summary>
    /// <remarks>
    /// Separate from standard variables to allow proper scoping and shadowing.
    /// User variables take precedence over standard variables when accessed.
    /// </remarks>
    public ImmutableDictionary<string, FhirPathTypeSet> DefinedVariables { get; init; }

    /// <summary>
    /// Stack of property contexts for nested navigation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Pushed when entering a function that needs to track its focus type.
    /// Used for resolving <c>$that</c> which refers to the current property context.
    /// </para>
    /// <para>
    /// Example: In <c>Patient.name.where(given.exists())</c>, when processing
    /// the <c>given.exists()</c> argument, the property context stack contains [HumanName[]].
    /// </para>
    /// </remarks>
    public ImmutableStack<FhirPathTypeSet> PropertyContextStack { get; init; }

    /// <summary>
    /// Stack of expression contexts for where(), select(), exists(), all(), aggregate().
    /// </summary>
    /// <remarks>
    /// <para>
    /// Pushed when entering a function whose argument evaluates on a single item context.
    /// Used for resolving <c>$this</c> which refers to the current item in iteration.
    /// </para>
    /// <para>
    /// The expression context is typically the property context marked as single (not collection)
    /// since where/select/exists iterate over items individually.
    /// </para>
    /// </remarks>
    public ImmutableStack<FhirPathTypeSet> ExpressionContextStack { get; init; }

    /// <summary>
    /// Stack of aggregate accumulator types for aggregate() function.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Pushed when entering an aggregate() function with the initial accumulator type.
    /// Used for resolving <c>$total</c> which refers to the running accumulator.
    /// </para>
    /// <para>
    /// Example: In <c>Patient.name.aggregate($total + 1, 0)</c>, the accumulator
    /// starts as integer(0) and $total resolves to integer.
    /// </para>
    /// </remarks>
    public ImmutableStack<FhirPathTypeSet> AggregateTotalStack { get; init; }

    /// <summary>
    /// The root context (entry point type for the expression).
    /// </summary>
    /// <remarks>
    /// Set via <see cref="Create"/> or <see cref="WithRootContext"/>.
    /// Used as fallback when context stacks are empty.
    /// </remarks>
    public FhirPathTypeSet? RootContext { get; init; }

    /// <summary>
    /// Creates a new context with the specified schema and root type.
    /// </summary>
    /// <param name="schema">FHIR schema provider for type resolution</param>
    /// <param name="rootTypeName">Root type name (e.g., "Patient", "Observation")</param>
    /// <returns>A new context configured for the specified root type</returns>
    /// <remarks>
    /// This method sets up standard variables:
    /// <list type="bullet">
    ///   <item><description>%resource: The root resource type</description></item>
    ///   <item><description>%rootResource: Same as %resource</description></item>
    ///   <item><description>%context: Same as %resource at root level</description></item>
    /// </list>
    /// </remarks>
    public static FhirPathVisitorContext Create(IFhirSchemaProvider schema, string rootTypeName)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(rootTypeName);

        var rootType = schema.GetTypeDefinition(rootTypeName);
        var rootProps = rootType != null
            ? new FhirPathTypeSet(rootType, rootTypeName)
            : new FhirPathTypeSet();

        var variables = ImmutableDictionary<string, FhirPathTypeSet>.Empty
            .WithComparers(StringComparer.OrdinalIgnoreCase)
            .Add("resource", rootProps)
            .Add("rootResource", rootProps)
            .Add("context", rootProps);

        return new FhirPathVisitorContext(
            schema,
            variables,
            ImmutableDictionary<string, FhirPathTypeSet>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase),
            ImmutableStack<FhirPathTypeSet>.Empty,
            ImmutableStack<FhirPathTypeSet>.Empty,
            ImmutableStack<FhirPathTypeSet>.Empty,
            rootProps);
    }

    /// <summary>
    /// Creates a new context with the specified schema and definition path.
    /// </summary>
    /// <param name="schema">FHIR schema provider for type resolution</param>
    /// <param name="definitionPath">Path like "Patient" or "Patient.name"</param>
    /// <returns>A new context with %context set to the path endpoint</returns>
    /// <remarks>
    /// <para>
    /// This is used when validating expressions that start at a nested path,
    /// such as FhirPath expressions in search parameters or constraints.
    /// </para>
    /// <para>
    /// For "Patient.name", %resource and %rootResource point to Patient,
    /// while %context points to HumanName (the nested element).
    /// </para>
    /// </remarks>
    public static FhirPathVisitorContext CreateForPath(IFhirSchemaProvider schema, string definitionPath)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(definitionPath);

        var path = definitionPath.Replace("[x]", "", StringComparison.OrdinalIgnoreCase);
        var dotIndex = path.IndexOf('.', StringComparison.Ordinal);
        var rootTypeName = dotIndex >= 0 ? path[..dotIndex] : path;
        var remainingPath = dotIndex >= 0 ? path[(dotIndex + 1)..] : null;

        var rootType = schema.GetTypeDefinition(rootTypeName);
        if (rootType == null)
        {
            throw new ArgumentException($"Could not resolve type: {rootTypeName}");
        }

        var rootProps = new FhirPathTypeSet(rootType, rootTypeName);

        if (string.IsNullOrEmpty(remainingPath))
        {
            return Create(schema, rootTypeName);
        }

        var currentProps = NavigateToPath(schema, rootType, remainingPath);

        var variables = ImmutableDictionary<string, FhirPathTypeSet>.Empty
            .WithComparers(StringComparer.OrdinalIgnoreCase)
            .Add("resource", rootProps)
            .Add("rootResource", rootProps)
            .Add("context", currentProps);

        return new FhirPathVisitorContext(
            schema,
            variables,
            ImmutableDictionary<string, FhirPathTypeSet>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase),
            ImmutableStack<FhirPathTypeSet>.Empty,
            ImmutableStack<FhirPathTypeSet>.Empty,
            ImmutableStack<FhirPathTypeSet>.Empty,
            currentProps);
    }

    private static FhirPathTypeSet NavigateToPath(
        IFhirSchemaProvider schema,
        Abstractions.IType startType,
        string path)
    {
        var result = new FhirPathTypeSet();
        var currentTypes = new List<Abstractions.IType> { startType };
        var pathParts = path.Split('.');

        foreach (var part in pathParts)
        {
            var nextTypes = new List<Abstractions.IType>();
            foreach (var type in currentTypes)
            {
                var child = type.Children.FirstOrDefault(c =>
                    c.Info.Name.Equals(part, StringComparison.OrdinalIgnoreCase) ||
                    c.Info.Name.StartsWith(part, StringComparison.OrdinalIgnoreCase));

                if (child != null)
                {
                    nextTypes.Add(child);
                }
            }
            currentTypes = nextTypes;
        }

        foreach (var type in currentTypes)
        {
            result.AddType(type);
        }

        return result;
    }

    /// <summary>
    /// Returns a new context with the specified variable added or updated.
    /// </summary>
    public FhirPathVisitorContext WithVariable(string name, FhirPathTypeSet props) =>
        this with { Variables = Variables.SetItem(name, props) };

    /// <summary>
    /// Returns a new context with the specified user-defined variable added or updated.
    /// </summary>
    public FhirPathVisitorContext WithDefinedVariable(string name, FhirPathTypeSet props) =>
        this with { DefinedVariables = DefinedVariables.SetItem(name, props) };

    /// <summary>
    /// Returns a new context with the specified root context.
    /// </summary>
    public FhirPathVisitorContext WithRootContext(FhirPathTypeSet props) =>
        this with { RootContext = props };

    /// <summary>
    /// Pushes a new property context onto the stack.
    /// </summary>
    public FhirPathVisitorContext PushPropertyContext(FhirPathTypeSet props) =>
        this with { PropertyContextStack = PropertyContextStack.Push(props) };

    /// <summary>
    /// Pops the top property context from the stack.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when stack is empty</exception>
    public FhirPathVisitorContext PopPropertyContext() =>
        this with { PropertyContextStack = PropertyContextStack.Pop() };

    /// <summary>
    /// Returns the current property context, or RootContext if stack is empty.
    /// </summary>
    public FhirPathTypeSet? CurrentPropertyContext =>
        PropertyContextStack.IsEmpty ? RootContext : PropertyContextStack.Peek();

    /// <summary>
    /// Pushes a new expression context onto the stack.
    /// </summary>
    public FhirPathVisitorContext PushExpressionContext(FhirPathTypeSet props) =>
        this with { ExpressionContextStack = ExpressionContextStack.Push(props) };

    /// <summary>
    /// Pops the top expression context from the stack.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when stack is empty</exception>
    public FhirPathVisitorContext PopExpressionContext() =>
        this with { ExpressionContextStack = ExpressionContextStack.Pop() };

    /// <summary>
    /// Returns the current expression context, or null if stack is empty.
    /// </summary>
    public FhirPathTypeSet? CurrentExpressionContext =>
        ExpressionContextStack.IsEmpty ? null : ExpressionContextStack.Peek();

    /// <summary>
    /// Pushes a new aggregate total type onto the stack.
    /// </summary>
    public FhirPathVisitorContext PushAggregateTotal(FhirPathTypeSet props) =>
        this with { AggregateTotalStack = AggregateTotalStack.Push(props) };

    /// <summary>
    /// Pops the top aggregate total from the stack.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when stack is empty</exception>
    public FhirPathVisitorContext PopAggregateTotal() =>
        this with { AggregateTotalStack = AggregateTotalStack.Pop() };

    /// <summary>
    /// Returns the current aggregate total type, or null if stack is empty.
    /// </summary>
    public FhirPathTypeSet? CurrentAggregateTotal =>
        AggregateTotalStack.IsEmpty ? null : AggregateTotalStack.Peek();

    /// <summary>
    /// Resolves a variable by name, checking user-defined variables first, then standard variables.
    /// </summary>
    /// <param name="name">Variable name without % prefix</param>
    /// <returns>The variable's type props, or null if not found</returns>
    public FhirPathTypeSet? ResolveVariable(string name)
    {
        if (DefinedVariables.TryGetValue(name, out var definedProps))
        {
            return definedProps;
        }

        if (Variables.TryGetValue(name, out var props))
        {
            return props;
        }

        return null;
    }

    /// <summary>
    /// Resolves a scope reference ($this, $that, $total, $index).
    /// </summary>
    /// <param name="scopeName">Scope name without $ prefix</param>
    /// <returns>The resolved type props, or null if not applicable</returns>
    /// <remarks>
    /// <list type="bullet">
    ///   <item><description><c>$this</c>: Current expression context (single item in iteration)</description></item>
    ///   <item><description><c>$that</c>: Current property context</description></item>
    ///   <item><description><c>$total</c>: Aggregate accumulator</description></item>
    ///   <item><description><c>$index</c>: Always returns integer</description></item>
    /// </list>
    /// </remarks>
    public FhirPathTypeSet? ResolveScope(string scopeName)
    {
        // FhirPath scope names are case-insensitive per spec
#pragma warning disable CA1308 // Normalize strings to uppercase - FhirPath spec uses lowercase scope names
        return scopeName.ToLowerInvariant() switch
#pragma warning restore CA1308
        {
            "this" => CurrentExpressionContext ?? RootContext,
            "that" => CurrentPropertyContext,
            "total" => CurrentAggregateTotal,
            "index" => CreateIntegerProps(),
            _ => null
        };
    }

    private static FhirPathTypeSet CreateIntegerProps()
    {
        var props = new FhirPathTypeSet();
        props.AddPrimitiveType("integer");
        return props;
    }
}
