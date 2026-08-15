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
///   <item><description><c>%rootResource</c>: Set via <see cref="RootResource"/> property, falling back to <see cref="Resource"/></description></item>
///   <item><description><c>%context</c>: Set via <see cref="ContextNode"/>, which <see cref="FhirPathEvaluator.Evaluate"/> fills in from the node it is handed</description></item>
///   <item><description><c>%ucum</c>, <c>%sct</c>, <c>%loinc</c>, <c>%vs-…</c>, <c>%ext-…</c>: fixed URIs, overridable via <see cref="WithEnvironmentVariable"/></description></item>
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
    protected EvaluationContext(
        ImmutableList<IElement> focus,
        ImmutableStack<IElement> thisStack,
        ImmutableStack<IElement> indexStack,
        ImmutableDictionary<string, ImmutableList<IElement>> environment,
        IElement? resource,
        IElement? rootResource,
        VariableScope? variables = null)
    {
        Focus = focus;
        ThisStack = thisStack;
        IndexStack = indexStack;
        Environment = environment;
        Resource = resource;
        RootResource = rootResource;
        Variables = variables ?? new VariableScope();
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
    /// The current <c>defineVariable</c> frame. Definitions flow forward along an invocation chain and are
    /// contained by the scope they were made in - see <see cref="VariableScope"/>.
    /// </summary>
    public VariableScope Variables { get; init; }

    /// <summary>
    /// The original node the expression is being evaluated against, exposed to expressions as <c>%context</c>.
    /// </summary>
    /// <remarks>
    /// Set by <see cref="FhirPathEvaluator.Evaluate"/> from the node it is handed, which is what the FHIR
    /// profile of FHIRPath defines <c>%context</c> to be. It is not always the resource: a constraint declared
    /// on <c>Patient.contact</c> is evaluated with a <c>contact</c> node as its context and the Patient as
    /// <see cref="Resource"/>.
    /// </remarks>
    public IElement? ContextNode { get; init; }

    /// <summary>
    /// Optional model the type operators resolve type identifiers against.
    /// </summary>
    /// <remarks>
    /// <para>
    /// FHIRPath's type operators are the one place the language requires an evaluator to know the model:
    /// <c>as</c> must signal an error when its identifier does not name a type, which cannot be decided
    /// from the instance alone - <see cref="TypeMatcher"/> otherwise compares type names as strings and
    /// has nothing to check an unknown name against.
    /// </para>
    /// <para>
    /// Optional on purpose. Callers that evaluate against elements from a known model (validation, the
    /// conformance suites) supply it and get the stricter behaviour; callers that have no model - ad-hoc
    /// expressions over hand-built elements - leave it null and keep the permissive behaviour, because
    /// "no model" is not evidence that a type identifier is wrong.
    /// </para>
    /// </remarks>
    public ISchema? Schema { get; init; }

    /// <summary>
    /// Optional callback for trace() function output.
    /// When set, trace() calls will invoke this handler with trace information.
    /// </summary>
    public Action<TraceEntry>? TraceHandler { get; init; }

    /// <summary>
    /// Optional callback invoked after each expression node is evaluated during debug tracing.
    /// When set, the evaluator will call this handler with details about each node evaluation.
    /// Exceptions thrown by the handler will propagate to the caller.
    /// </summary>
    public Action<NodeEvaluationEntry>? NodeEvaluationHandler { get; init; }

    /// <summary>
    /// Optional host-provided delegate for instance-selector object creation
    /// (<c>Type { element: value, ... }</c>). Mirrors the <c>resolve()</c> hook
    /// (<see cref="FhirEvaluationContext.ElementResolver"/>): when set, the engine
    /// delegates construction; when unset, evaluating an instance selector throws
    /// <see cref="InvalidOperationException"/>, because the engine has no object model
    /// of its own to fall back on. Return null to decline a type — the engine then
    /// yields an empty result.
    /// </summary>
    public Func<InstanceCreationRequest, IElement?>? InstanceCreator { get; init; }

    /// <summary>
    /// Creates a context that evaluates in a nested <c>defineVariable</c> scope: it can read the variables
    /// defined so far, and anything it defines is invisible once the nested expression is done.
    /// </summary>
    /// <remarks>
    /// Used for the operands of <c>|</c>, which are parallel evaluations of the same input rather than the
    /// "subsequent expressions on the output collection" that <c>defineVariable</c> is defined to affect.
    /// <see cref="PushThis"/> forks as well, because every per-item argument scope needs the same containment.
    /// </remarks>
    public EvaluationContext ForkVariableScope()
    {
        return this with { Variables = Variables.Fork() };
    }

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
    /// Pushes a $this binding onto the stack and enters a nested <c>defineVariable</c> scope.
    /// Used by where(), select(), exists() etc. for iteration context.
    /// </summary>
    /// <remarks>
    /// Binding <c>$this</c> is what "entering a per-item expression scope" looks like in this engine - every
    /// caller of this method is evaluating a sub-expression once per focus item - so the variable scope is
    /// forked here rather than at each call site. Doing it centrally is what stops a function added later
    /// from leaking its argument's variables into the enclosing expression by omission.
    /// </remarks>
    public EvaluationContext PushThis(IElement element)
    {
        return this with { ThisStack = ThisStack.Push(element), Variables = Variables.Fork() };
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
    /// Creates a new context that resolves type identifiers against the specified model.
    /// </summary>
    public EvaluationContext WithSchema(ISchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);

        return this with { Schema = schema };
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
    /// Creates a new context whose <c>%context</c> is the specified node.
    /// </summary>
    public EvaluationContext WithContextNode(IElement contextNode)
    {
        ArgumentNullException.ThrowIfNull(contextNode);

        return this with { ContextNode = contextNode };
    }

    /// <summary>
    /// Creates a new context with the specified trace handler.
    /// The trace handler will be invoked when trace() function is called.
    /// </summary>
    public EvaluationContext WithTraceHandler(Action<TraceEntry> traceHandler)
    {
        return this with { TraceHandler = traceHandler };
    }

    /// <summary>
    /// Creates a new context with the specified node evaluation handler for debug tracing.
    /// </summary>
    public EvaluationContext WithNodeEvaluationHandler(Action<NodeEvaluationEntry> handler)
    {
        return this with { NodeEvaluationHandler = handler };
    }

    /// <summary>
    /// Creates a new context with the specified instance-creation delegate.
    /// The delegate is invoked when an instance selector (<c>Type { ... }</c>) is evaluated.
    /// </summary>
    public EvaluationContext WithInstanceCreator(Func<InstanceCreationRequest, IElement?> instanceCreator)
    {
        return this with { InstanceCreator = instanceCreator };
    }

    /// <summary>
    /// Gets an environment variable value, or null when the name resolves to nothing.
    /// </summary>
    /// <remarks>
    /// A null return conflates "no such variable" with "bound to an empty collection". Callers that must
    /// tell those apart - FHIRPath makes the first an error and the second a value - use
    /// <see cref="TryGetEnvironmentVariable"/>.
    /// </remarks>
    public object? GetEnvironmentVariable(string name)
    {
        return TryGetEnvironmentVariable(name, out var value) ? value : null;
    }

    /// <summary>
    /// Resolves an environment variable, reporting whether the name is defined at all.
    /// </summary>
    /// <param name="name">The variable name, without the leading <c>%</c>.</param>
    /// <param name="value">The bound value: a single <see cref="IElement"/>, a collection of them, or null when the binding is empty.</param>
    /// <returns><see langword="true"/> when the name is defined, even if its value is empty.</returns>
    /// <remarks>
    /// <para>
    /// The engine-managed names resolve first and are always considered defined, so an expression that reads
    /// <c>%resource</c> or <c>%context</c> against a context that carries neither gets an empty collection
    /// rather than an error - absence of a host binding is not the same as the expression naming something
    /// that does not exist.
    /// </para>
    /// <para>
    /// <c>defineVariable</c> bindings and host-supplied <see cref="Environment"/> entries are consulted before
    /// the fixed FHIRPath constants so a host can override <c>%vs-…</c> / <c>%ext-…</c> with a real value.
    /// </para>
    /// </remarks>
    public bool TryGetEnvironmentVariable(string name, out object? value)
    {
        ArgumentNullException.ThrowIfNull(name);

        switch (name)
        {
            case "this":
                value = GetThis();
                return true;

            case "index":
                var idx = GetIndex();
                value = idx.HasValue ? new IndexElement(idx.Value) : null;
                return true;

            case "context":
                value = ContextNode ?? Resource;
                return true;

            case "resource":
                value = Resource;
                return true;

            // Per the FHIR profile of FHIRPath, %rootResource is the container of %resource and is the same
            // as %resource whenever the resource is not contained in another one.
            case "rootResource":
                value = RootResource ?? Resource;
                return true;

            // Not-supported environment variables that require external services
            case "terminologies":
                throw new NotSupportedException("Environment variable '%terminologies' is not supported. It requires terminology service integration.");
        }

        if (Variables.TryResolve(name, out var definedValue))
        {
            value = definedValue.Count == 1 ? definedValue[0] : definedValue;
            return true;
        }

        if (Environment.TryGetValue(name, out var environmentValue))
        {
            value = environmentValue.Count == 1 ? environmentValue[0] : environmentValue;
            return true;
        }

        if (GetStandardConstant(name) is { } standardValue)
        {
            value = new StringElement(standardValue);
            return true;
        }

        value = null;
        return false;
    }

    /// <summary>
    /// Gets the value for a standard FHIRPath external constant.
    /// These are defined by the FHIRPath specification and have fixed values.
    /// </summary>
    /// <remarks>
    /// The <c>vs-</c> and <c>ext-</c> families are expanded by rule rather than enumerated: the FHIR profile
    /// of FHIRPath defines <c>%vs-[name]</c> and <c>%ext-[name]</c> for every name in the specification, so
    /// a fixed list of two of them just makes the other several hundred silently unresolvable.
    /// </remarks>
    private static string? GetStandardConstant(string name)
    {
        return name switch
        {
            "sct" => "http://snomed.info/sct",
            "loinc" => "http://loinc.org",
            "ucum" => "http://unitsofmeasure.org",
            _ when name.StartsWith("vs-", StringComparison.Ordinal) && name.Length > 3
                => "http://hl7.org/fhir/ValueSet/" + name[3..],
            _ when name.StartsWith("ext-", StringComparison.Ordinal) && name.Length > 4
                => "http://hl7.org/fhir/StructureDefinition/" + name[4..],
            _ => null
        };
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
        public bool HasPrimitiveValue => true;

        public IReadOnlyList<IElement> Children(string? name = null) => [];

        public T? Meta<T>() where T : class => null;
    }

    /// <summary>
    /// Simple implementation of IElement for string constant values.
    /// </summary>
    private sealed class StringElement(string value) : IElement
    {
        public string Name => string.Empty;
        public string InstanceType => "string";
        public object Value { get; } = value;
        public string Location => string.Empty;
        public IType? Type => null;
        public bool HasPrimitiveValue => true;

        public IReadOnlyList<IElement> Children(string? name = null) => [];

        public T? Meta<T>() where T : class => null;
    }
}
