// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Immutable;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Expressions;
using Ignixa.FhirPath.Visitors;

namespace Ignixa.FhirPath.Analysis;

/// <summary>
/// Immutable context for FhirPath static analysis and type inference.
/// </summary>
/// <remarks>
/// This context is designed for static analysis visitors (type checking, validation)
/// rather than runtime evaluation. It provides:
/// <list type="bullet">
///   <item><description>Type tracking through expression traversal</description></item>
///   <item><description>Issue collection for validation errors and warnings</description></item>
///   <item><description>Variable and scope resolution</description></item>
///   <item><description>Immutable operations for safe visitor traversal</description></item>
/// </list>
/// </remarks>
public sealed record AnalysisContext
{
    private readonly List<ValidationIssue> _issues;

    private AnalysisContext(
        IFhirSchemaProvider schema,
        string rootType,
        List<ValidationIssue> issues,
        ImmutableDictionary<string, FhirPathTypeSet> variables,
        Dictionary<string, FhirPathTypeSet>? definedVariables,
        ImmutableStack<FhirPathTypeSet> typeStack,
        ImmutableStack<FhirPathTypeSet> expressionContextStack,
        ImmutableStack<FhirPathTypeSet> aggregateTotalStack,
        FhirPathTypeSet? currentFocus)
    {
        Schema = schema;
        RootType = rootType;
        _issues = issues;
        Variables = variables;
        DefinedVariables = definedVariables ?? new Dictionary<string, FhirPathTypeSet>(StringComparer.OrdinalIgnoreCase);
        TypeStack = typeStack;
        ExpressionContextStack = expressionContextStack;
        AggregateTotalStack = aggregateTotalStack;
        CurrentFocus = currentFocus;
    }

    /// <summary>
    /// Gets the FHIR schema provider for type resolution.
    /// </summary>
    public IFhirSchemaProvider Schema { get; init; }

    /// <summary>
    /// Gets the root type name for this analysis context.
    /// </summary>
    public string RootType { get; init; }

    /// <summary>
    /// Gets the collected validation issues.
    /// </summary>
    public IReadOnlyList<ValidationIssue> Issues => _issues;

    /// <summary>
    /// Gets the standard FhirPath variables (%resource, %rootResource, %context).
    /// </summary>
    public ImmutableDictionary<string, FhirPathTypeSet> Variables { get; init; }

    /// <summary>
    /// Gets the user-defined variables from defineVariable() function.
    /// Mutable dictionary shared across context copies to allow defineVariable side effects during analysis.
    /// </summary>
    public Dictionary<string, FhirPathTypeSet> DefinedVariables { get; init; }

    /// <summary>
    /// Gets the type stack for nested navigation.
    /// </summary>
    public ImmutableStack<FhirPathTypeSet> TypeStack { get; init; }

    /// <summary>
    /// Gets the expression context stack for where(), select(), etc.
    /// </summary>
    public ImmutableStack<FhirPathTypeSet> ExpressionContextStack { get; init; }

    /// <summary>
    /// Gets the aggregate total stack for aggregate() function.
    /// </summary>
    public ImmutableStack<FhirPathTypeSet> AggregateTotalStack { get; init; }

    /// <summary>
    /// Gets the current focus type set.
    /// </summary>
    public FhirPathTypeSet? CurrentFocus { get; init; }

    /// <summary>
    /// Creates a new analysis context for the specified root type.
    /// </summary>
    /// <remarks>
    /// The schema provider handles case-insensitive BackboneElement type lookups internally.
    /// For example, "ElementDefinition.slicing" will be resolved to "ElementDefinition.Slicing"
    /// by the schema provider's GetTypeDefinition method.
    /// </remarks>
    public static AnalysisContext Create(IFhirSchemaProvider schema, string rootTypeName)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(rootTypeName);

        // The schema provider handles case-insensitive lookups for BackboneElement types
        // (e.g., "ElementDefinition.slicing" -> "ElementDefinition.Slicing")
        var rootType = schema.GetTypeDefinition(rootTypeName);

        var rootProps = rootType != null
            ? new FhirPathTypeSet(rootType, rootTypeName)
            : new FhirPathTypeSet();

        if (rootType != null)
        {
            rootProps.IsRoot = true;
        }

        var variables = ImmutableDictionary<string, FhirPathTypeSet>.Empty
            .WithComparers(StringComparer.OrdinalIgnoreCase)
            .Add("resource", rootProps)
            .Add("rootResource", rootProps)
            .Add("context", rootProps)
            .Add("ucum", CreateStringTypeSet())
            .Add("sct", CreateStringTypeSet())
            .Add("loinc", CreateStringTypeSet());

        return new AnalysisContext(
            schema,
            rootTypeName,
            [],
            variables,
            null,  // Will create new mutable dictionary in constructor
            ImmutableStack<FhirPathTypeSet>.Empty,
            ImmutableStack<FhirPathTypeSet>.Empty,
            ImmutableStack<FhirPathTypeSet>.Empty,
            rootProps);
    }

    /// <summary>
    /// Adds a validation issue to the context.
    /// </summary>
    public void AddIssue(ValidationIssueSeverity severity, string message, Expression? location = null)
    {
        _issues.Add(new ValidationIssue
        {
            Severity = severity,
            Message = message,
            Location = location?.Location?.ToString(),
            Expression = location?.ToString()
        });
    }

    /// <summary>
    /// Adds an error issue.
    /// </summary>
    public void AddError(string message, Expression? location = null)
    {
        AddIssue(ValidationIssueSeverity.Error, message, location);
    }

    /// <summary>
    /// Adds a warning issue.
    /// </summary>
    public void AddWarning(string message, Expression? location = null)
    {
        AddIssue(ValidationIssueSeverity.Warning, message, location);
    }

    /// <summary>
    /// Adds an informational issue.
    /// </summary>
    public void AddInfo(string message, Expression? location = null)
    {
        AddIssue(ValidationIssueSeverity.Information, message, location);
    }

    /// <summary>
    /// Gets the root context type set.
    /// </summary>
    public FhirPathTypeSet GetRootContext()
    {
        if (Variables.TryGetValue("context", out var context))
        {
            return context;
        }

        return new FhirPathTypeSet();
    }

    /// <summary>
    /// Gets the current type context, falling back to root if stack is empty.
    /// </summary>
    public FhirPathTypeSet GetCurrentType()
    {
        return CurrentFocus ?? GetRootContext();
    }

    /// <summary>
    /// Returns a new context with an updated focus.
    /// </summary>
    public AnalysisContext WithFocus(FhirPathTypeSet focus)
    {
        return new AnalysisContext(Schema, RootType, _issues, Variables, DefinedVariables,
            TypeStack, ExpressionContextStack, AggregateTotalStack, focus);
    }

    /// <summary>
    /// Pushes a type onto the type stack.
    /// </summary>
    public AnalysisContext PushTypeContext(FhirPathTypeSet types)
    {
        return new AnalysisContext(Schema, RootType, _issues, Variables, DefinedVariables,
            TypeStack.Push(types), ExpressionContextStack, AggregateTotalStack, CurrentFocus);
    }

    /// <summary>
    /// Pops a type from the type stack.
    /// </summary>
    public AnalysisContext PopTypeContext()
    {
        if (TypeStack.IsEmpty)
        {
            return this;
        }

        return new AnalysisContext(Schema, RootType, _issues, Variables, DefinedVariables,
            TypeStack.Pop(), ExpressionContextStack, AggregateTotalStack, CurrentFocus);
    }

    /// <summary>
    /// Pushes an expression context (for where, select, etc.).
    /// </summary>
    public AnalysisContext PushExpressionContext(FhirPathTypeSet types)
    {
        return new AnalysisContext(Schema, RootType, _issues, Variables, DefinedVariables,
            TypeStack, ExpressionContextStack.Push(types), AggregateTotalStack, CurrentFocus);
    }

    /// <summary>
    /// Pops an expression context.
    /// </summary>
    public AnalysisContext PopExpressionContext()
    {
        if (ExpressionContextStack.IsEmpty)
        {
            return this;
        }

        return new AnalysisContext(Schema, RootType, _issues, Variables, DefinedVariables,
            TypeStack, ExpressionContextStack.Pop(), AggregateTotalStack, CurrentFocus);
    }

    /// <summary>
    /// Gets the current expression context ($this).
    /// </summary>
    public FhirPathTypeSet? GetExpressionContext()
    {
        return ExpressionContextStack.IsEmpty ? null : ExpressionContextStack.Peek();
    }

    /// <summary>
    /// Pushes an aggregate total context.
    /// </summary>
    public AnalysisContext PushAggregateTotal(FhirPathTypeSet types)
    {
        return new AnalysisContext(Schema, RootType, _issues, Variables, DefinedVariables,
            TypeStack, ExpressionContextStack, AggregateTotalStack.Push(types), CurrentFocus);
    }

    /// <summary>
    /// Pops an aggregate total context.
    /// </summary>
    public AnalysisContext PopAggregateTotal()
    {
        if (AggregateTotalStack.IsEmpty)
        {
            return this;
        }

        return new AnalysisContext(Schema, RootType, _issues, Variables, DefinedVariables,
            TypeStack, ExpressionContextStack, AggregateTotalStack.Pop(), CurrentFocus);
    }

    /// <summary>
    /// Gets the current aggregate total ($total).
    /// </summary>
    public FhirPathTypeSet? GetAggregateTotal()
    {
        return AggregateTotalStack.IsEmpty ? null : AggregateTotalStack.Peek();
    }

    /// <summary>
    /// Returns a new context with a variable added.
    /// </summary>
    public AnalysisContext WithVariable(string name, FhirPathTypeSet value)
    {
        return new AnalysisContext(Schema, RootType, _issues, Variables.SetItem(name, value), DefinedVariables,
            TypeStack, ExpressionContextStack, AggregateTotalStack, CurrentFocus);
    }

    /// <summary>
    /// Adds a defined variable to the shared mutable dictionary.
    /// Unlike other With* methods, this mutates the shared dictionary for analysis of subsequent operations.
    /// </summary>
    public AnalysisContext WithDefinedVariable(string name, FhirPathTypeSet value)
    {
        DefinedVariables[name] = value;
        return this;
    }

    /// <summary>
    /// Creates a forked context for analyzing a branch expression (e.g., union operands).
    /// The forked context has its own copy of DefinedVariables so that variables defined
    /// in one branch don't leak to sibling branches during static analysis.
    /// </summary>
    /// <remarks>
    /// Per FHIRPath spec, defineVariable affects "subsequent expressions on the output collection".
    /// Union branches are NOT subsequent - they're parallel evaluations from the same input.
    /// </remarks>
    public AnalysisContext ForkForBranch()
    {
        return new AnalysisContext(Schema, RootType, _issues, Variables,
            new Dictionary<string, FhirPathTypeSet>(DefinedVariables, StringComparer.OrdinalIgnoreCase),
            TypeStack, ExpressionContextStack, AggregateTotalStack, CurrentFocus);
    }

    /// <summary>
    /// Resolves a variable by name.
    /// </summary>
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
    public FhirPathTypeSet? ResolveScope(string scopeName)
    {
#pragma warning disable CA1308
        return scopeName.ToLowerInvariant() switch
#pragma warning restore CA1308
        {
            "this" => GetExpressionContext() ?? GetRootContext(),
            "that" => TypeStack.IsEmpty ? GetRootContext() : TypeStack.Peek(),
            "total" => GetAggregateTotal(),
            "index" => CreateIntegerTypeSet(),
            _ => null
        };
    }

    private static FhirPathTypeSet CreateIntegerTypeSet()
    {
        var props = new FhirPathTypeSet();
        props.AddPrimitiveType("integer");
        return props;
    }

    private static FhirPathTypeSet CreateStringTypeSet()
    {
        var props = new FhirPathTypeSet();
        props.AddPrimitiveType("string");
        return props;
    }
}
