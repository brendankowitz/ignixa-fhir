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
/// <b>Two deliberate exceptions:</b> <see cref="DefinedVariables"/> and
/// <see cref="ReferenceIndexCache"/> are mutable holders, and a plain <c>with</c> copy carries the
/// same reference forward rather than cloning it - so every derived context still mutates and sees
/// the one shared <see cref="DefinedVariables"/> dictionary and the one shared
/// <see cref="ReferenceIndexCache"/> instance. This is intentional, not an oversight: it is what
/// lets <c>defineVariable()</c> stay visible across <c>with</c>-derived copies within the same
/// expression, and what lets <see cref="ReferenceIndexCache"/> build its index once per root instead
/// of once per copy. <see cref="ForkVariableScope"/> is the one place that deliberately breaks the
/// sharing - it clones <see cref="DefinedVariables"/> so union branches cannot leak variables to
/// each other. Do not "fix" the sharing elsewhere by cloning these two properties on every
/// <c>with</c>; that would silently defeat the reference index cache and break variable visibility
/// across nested evaluation.
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
        // Ordinal for the same reason as VariableScope: host-supplied names are read back through the same
        // case-sensitive %name syntax, a few lines below the ordinal switch that resolves %resource and
        // friends. A lenient comparer here would make %v and %V collide for hosts only, which is a
        // difference no caller asked for rather than a convenience.
        ImmutableDictionary<string, ImmutableList<IElement>>.Empty.WithComparers(StringComparer.Ordinal),
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
    /// Cache holder for the in-instance <see cref="ReferenceIndex"/> that <c>resolve()</c> uses to
    /// look up contained resources and, for a Bundle/Parameters root, sibling entries, before
    /// falling back to <see cref="FhirEvaluationContext.ElementResolver"/>. A single holder
    /// instance is shared by every <c>with</c>-derived copy of this context, so the index is built
    /// at most once per root even though the context itself is copied on every
    /// <see cref="PushThis"/> / <see cref="WithFocus"/> / etc. Internal: an implementation detail
    /// of <c>resolve()</c>, not part of the public evaluation API.
    /// </summary>
    internal ReferenceIndexCache ReferenceIndexCache { get; init; } = new();

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
    /// <para>
    /// This overload resolves by name alone and therefore expands the <c>vs-</c> / <c>ext-</c> families, which is
    /// right for a host asking about a name it already holds. The evaluator instead calls the overload taking
    /// <paramref name="isDelimited"/>, because in an <em>expression</em> the spelling decides: see
    /// <see cref="GetStandardConstant"/>.
    /// </para>
    /// </remarks>
    public bool TryGetEnvironmentVariable(string name, out object? value)
        => TryGetEnvironmentVariable(name, isDelimited: true, out value);

    /// <summary>
    /// Resolves an environment variable written in a FHIRPath expression, reporting whether the name is defined
    /// at all.
    /// </summary>
    /// <param name="name">The variable name, without the leading <c>%</c> or any surrounding backticks.</param>
    /// <param name="isDelimited">
    /// Whether the reference was written as <c>%`name`</c> rather than bare. Only the delimited spelling expands
    /// the <c>vs-</c> and <c>ext-</c> families; see <see cref="GetStandardConstant"/> for why.
    /// </param>
    /// <param name="value">The bound value: a single <see cref="IElement"/>, a collection of them, or null when the binding is empty.</param>
    /// <returns><see langword="true"/> when the name is defined, even if its value is empty.</returns>
    /// <remarks>
    /// Internal: <paramref name="isDelimited"/> mirrors <see cref="Expressions.VariableRefExpression.IsDelimited"/>,
    /// an engine-internal parse artifact rather than part of the published evaluation contract. The only caller
    /// of this overload, <see cref="FhirPathEvaluator"/>, is in this assembly's <c>InternalsVisibleTo</c> set.
    /// </remarks>
    internal bool TryGetEnvironmentVariable(string name, bool isDelimited, out object? value)
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
                throw new FhirPathFunctionNotSupportedException("%terminologies", "Environment variable '%terminologies' is not supported. It requires terminology service integration.");
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

        if (GetStandardConstant(name, isDelimited) is { } standardValue)
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
    /// <param name="name">The constant's name, without the leading <c>%</c> or any surrounding backticks.</param>
    /// <param name="isDelimited">
    /// Whether the reference was written as <c>%`name`</c>. Governs the <c>vs-</c> / <c>ext-</c> families only;
    /// <c>%sct</c>, <c>%loinc</c> and <c>%ucum</c> are spelled bare in the specification and resolve either way.
    /// </param>
    /// <remarks>
    /// <para>
    /// The <c>vs-</c> and <c>ext-</c> families are expanded by rule rather than enumerated: the FHIR profile
    /// of FHIRPath defines <c>%vs-[name]</c> and <c>%ext-[name]</c> for every name in the specification, so
    /// a fixed list of two of them just makes the other several hundred silently unresolvable.
    /// </para>
    /// <para>
    /// <b>Only the delimited spelling expands (#438 review).</b> The specification writes these two families as
    /// <c>%`vs-[name]`</c> and <c>%`ext-[name]`</c> and says the names "are quoted (just like paths) to allow
    /// '-' in the name"; HAPI's FHIRPathEngine tests <c>startsWith("%`vs-")</c> and <c>startsWith("%`ext-")</c>,
    /// so a bare <c>%vs-mine</c> there is not a ValueSet URI - it falls through to the host's constant resolver
    /// and then to an unknown-constant error. Ignixa's tokenizer accepts <c>-</c> in the bare form because
    /// HAPI's <em>lexer</em> does and real published cqf-expression content relies on it (<c>%p-inactive</c>),
    /// but that is a lexical allowance. Expanding the bare form as well would make Ignixa resolve a spelling
    /// neither the specification nor HAPI resolves, so <paramref name="isDelimited"/> gates it and bare
    /// <c>%vs-mine</c> reports an undefined variable, exactly as HAPI does.
    /// </para>
    /// <para>
    /// The rule itself lives in <see cref="StandardConstantFamilies"/>, not here - both halves of it: the
    /// prefix-and-suffix test the static analyzer and the SQL-on-FHIR validator also use to decide whether a
    /// reference is in these families (#442's review found the three had drifted apart on an empty suffix,
    /// <c>%`vs-`</c>), and the two canonical URL bases the names expand to. This method asks one question
    /// and gets both, so a clause added to the family rule cannot leave the expansion behind.
    /// </para>
    /// </remarks>
    private static string? GetStandardConstant(string name, bool isDelimited)
    {
        return name switch
        {
            "sct" => "http://snomed.info/sct",
            "loinc" => "http://loinc.org",
            "ucum" => "http://unitsofmeasure.org",
            _ when StandardConstantFamilies.TryResolveCanonicalUrl(name, isDelimited, out var url) => url,
            _ => null
        };
    }

    /// <summary>
    /// The <c>$index</c> iteration counter, as an element.
    /// </summary>
    /// <remarks>
    /// Declares <see cref="ISystemValueElement"/>: <c>$index</c> is a <c>System.Integer</c> the
    /// evaluator produces, never a value read from a resource. Without the declaration
    /// <c>select($index).ofType(Integer)</c> returned empty from R5 onwards - the pre-R5 cast alias
    /// had been rescuing the misclassified value, and the R5 gate withdraws it - and
    /// <c>$index is Integer</c> was false on every version.
    /// </remarks>
    private sealed class IndexElement(int value) : ISystemValueElement
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
    /// A standard FHIRPath external constant - <c>%sct</c>, <c>%loinc</c>, <c>%ucum</c> and the
    /// <c>%vs-</c> and <c>%ext-</c> families - as an element.
    /// </summary>
    /// <remarks>
    /// Declares <see cref="ISystemValueElement"/>: <see cref="GetStandardConstant"/> returns values
    /// the specification defines, which the evaluator materialises here as <c>System.String</c>; none
    /// of them is read from a resource. Without the declaration <c>%ucum.ofType(String)</c> returned
    /// empty from R5 onwards and <c>%ucum is String</c> was false on every version. Environment
    /// variables supplied by the caller do not come through here - they arrive as elements the caller
    /// already built, and classifying those is the caller's decision.
    /// </remarks>
    private sealed class StringElement(string value) : ISystemValueElement
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
