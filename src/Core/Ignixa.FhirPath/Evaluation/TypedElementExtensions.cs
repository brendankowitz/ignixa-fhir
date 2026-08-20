/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Extension methods for IElement to evaluate FhirPath expressions.
 * Provides API compatibility with Firely SDK FhirPath implementation.
 */

using Ignixa.FhirPath.Evaluation.Functions;
using Ignixa.FhirPath.Expressions;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Parser;

namespace Ignixa.FhirPath.Evaluation;

/// <summary>
/// Extension methods for evaluating FhirPath expressions on IElement.
/// Integrates both AST caching and compiled delegate caching for optimal performance.
/// Compiled delegates provide 7x speedup for common patterns; complex expressions fall back to interpreter.
/// </summary>
public static class TypedElementExtensions
{
    /// <summary>
    /// Entries retained per generation by each expression cache, so twice this in the worst case.
    /// </summary>
    /// <remarks>
    /// Sized from the shipped corpus rather than guessed: the generated SearchParameter definitions for
    /// STU3, R4, R4B, R5 and R6 carry 2,396 distinct expressions between them, so this holds every
    /// version's parameters at once - a host serving one version uses a fraction of it - with headroom
    /// for the hand-written expressions in Search, IPS, DeId and TestScript. Custom SearchParameters
    /// push past it, which is the point: past it the cache evicts instead of growing.
    /// </remarks>
    private const int ExpressionCacheCapacity = 4096;

    // Thread-safe cache for compiled expressions (string -> Expression AST)
    private static readonly BoundedExpressionCache<Expression> _astCache = new(ExpressionCacheCapacity);

    // Thread-safe cache for compiled delegates (Expression -> compiled delegate)
    // Key: Expression object hash code and expression string combined
    // Value: Compiled delegate or null if compilation not supported
    private static readonly BoundedExpressionCache<Func<IElement, EvaluationContext, IEnumerable<IElement>>?> _delegateCache = new(ExpressionCacheCapacity);

    // Shared compiler instances
    private static readonly FhirPathParser AstParser = new FhirPathParser(preserveTrivia: false);
    private static readonly FhirPathDelegateCompiler _delegateCompiler = new FhirPathDelegateCompiler(new FhirPathEvaluator());

    // Shared evaluator instance
    private static readonly FhirPathEvaluator _evaluator = new FhirPathEvaluator();

    /// <summary>
    /// Parses and caches a FhirPath expression string to AST.
    /// </summary>
    private static Expression CompileExpressionToAst(string expression)
    {
        return _astCache.GetOrAdd(expression, expr => AstParser.Parse(expr));
    }

    /// <summary>
    /// Attempts to compile an AST expression to a delegate and caches the result.
    /// Returns the compiled delegate if successful, null if the expression pattern is not supported.
    /// </summary>
    private static Func<IElement, EvaluationContext, IEnumerable<IElement>>? CompileExpressionToDelegate(Expression ast, string expressionString)
    {
        // Use expression string as cache key (stable across invocations)
        return _delegateCache.GetOrAdd(expressionString, _ => _delegateCompiler.TryCompile(ast));
    }

    /// <summary>
    /// Evaluates a FhirPath expression and returns matching elements.
    /// Attempts to use compiled delegate for performance; falls back to interpreted evaluation if needed.
    /// </summary>
    /// <param name="input">The root element to evaluate against</param>
    /// <param name="expression">FhirPath expression string</param>
    /// <param name="context">Optional evaluation context</param>
    /// <returns>Collection of elements that match the expression</returns>
    /// <remarks>
    /// <para>
    /// <b>This defaults <c>%resource</c> to <paramref name="input"/>; <see cref="FhirPathEvaluator.Evaluate"/>
    /// deliberately does not.</b> The difference is the contract, not an oversight, and the two must not be
    /// aligned by making the engine default as well.
    /// </para>
    /// <para>
    /// This overload documents <paramref name="input"/> as "the root element", so treating it as the resource
    /// is a defensible reading of its own contract - and FHIR blesses the equality explicitly: "The resource is
    /// very often the context, such that %resource = %context". <see cref="FhirPathEvaluator.Evaluate"/> makes
    /// no such promise: its input is "the node handed to the engine", and its callers routinely hand it a
    /// sub-element (invariants are attached to elements, SQL-on-FHIR evaluates columns against
    /// <c>forEach</c> items, narrative templates re-enter with an extracted node). Defaulting there would bind
    /// <c>%resource</c> to a non-resource, which FHIR defines it never to be - "the resource that contains the
    /// original node that is in %context" - and would replace an honest empty with a confidently wrong node.
    /// For invariant evaluation that is a strict downgrade: an unevaluable constraint currently degrades to a
    /// non-failing warning, whereas a wrong <c>%resource</c> produces a wrong verdict.
    /// </para>
    /// <para>
    /// The engine cannot infer its way out of this. Unlike Firely's <c>ScopedNode</c>, <see cref="IElement"/>
    /// carries no parent link, so there is no containing resource to walk up to - the host is the only party
    /// that knows, and unbound therefore resolves to empty (FHIRPath 1.9: a defined variable with no value
    /// specified is empty, only an *undefined* name is an error).
    /// </para>
    /// <para>
    /// Narrowing this default to fire only when <paramref name="input"/> is genuinely a resource is the correct
    /// end state - the <c>getResourceKey()</c> justification below is stale, since SQL-on-FHIR binds
    /// <c>RootResource</c> itself and never reaches this method - but its only remaining coverage lives in the
    /// SQL-on-FHIR conformance suite, so it is left for a change that can run those tests.
    /// </para>
    /// </remarks>
    public static IEnumerable<IElement> Select(this IElement input, string expression, EvaluationContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);

        context ??= new EvaluationContext();

        // Set the Resource and RootResource for FHIR-specific functions like getResourceKey()
        // If input is the root resource element, set both to the input (immutable pattern)
        if (context.Resource is null || context.RootResource is null)
        {
            context = context with
            {
                Resource = context.Resource ?? input,
                RootResource = context.RootResource ?? input
            };
        }

        // 1. Parse expression to AST (cached)
        var ast = CompileExpressionToAst(expression);

        // 2. Try to compile to delegate (cached, may return null)
        var compiledDelegate = CompileExpressionToDelegate(ast, expression);

        // 3. If compiled, execute delegate; otherwise fall back to interpreter
        if (compiledDelegate != null)
        {
            return compiledDelegate(input, context);
        }

        // Fallback: Use interpreted evaluation
        return _evaluator.Evaluate(input, ast, context);
    }

    /// <summary>
    /// Evaluates a FhirPath expression and returns a single scalar value.
    /// Returns null if the expression returns an empty collection or multiple values.
    /// </summary>
    /// <param name="input">The root element to evaluate against</param>
    /// <param name="expression">FhirPath expression string</param>
    /// <param name="context">Optional evaluation context</param>
    /// <returns>Single scalar value, or null if expression returns empty/multiple values</returns>
    /// <remarks>
    /// Returns the raw boxed value (bool, decimal, string, ...). Calling <c>.ToString()</c> on it
    /// produces CLR formatting, not FhirPath formatting — a boolean renders as "True", not the
    /// spec's "true", and decimals are culture-sensitive. To get the FhirPath string representation
    /// of a result, use <see cref="AsString"/> on <see cref="Select"/> instead, e.g.
    /// <c>input.Select(expression).AsString()</c>.
    /// </remarks>
    public static object? Scalar(this IElement input, string expression, EvaluationContext? context = null)
    {
        var results = input.Select(expression, context).ToList();

        if (results.Count == 1)
        {
            return results[0].Value;
        }

        return null;
    }

    /// <summary>
    /// Converts a single-item element collection to its FhirPath string representation
    /// (the spec's <c>toString()</c> rules: lowercase booleans, invariant-culture decimals).
    /// </summary>
    /// <param name="elements">The element collection to convert, typically the result of <see cref="Select"/></param>
    /// <returns>The FhirPath string representation, or null if the collection is empty, has multiple items, or the single element has no primitive value (e.g. a complex/backbone element)</returns>
    /// <remarks>
    /// Use this instead of <see cref="Scalar"/> followed by <c>.ToString()</c>, which produces CLR
    /// formatting rather than the spec's — see the warning on <see cref="Scalar"/>.
    /// </remarks>
    /// <seealso cref="Scalar"/>
    /// <example>
    /// <code>
    /// var hasAddress = element.Select("address.exists()").AsString(); // "false", not "False"
    /// </code>
    /// </example>
    /// <remarks>
    /// The arity check is made here rather than delegated to
    /// <see cref="TypeConversionFunctions.ToString"/>, which signals an error on a multi-item collection
    /// as FHIRPath's Conversion section requires. That rule governs the <c>toString()</c> <i>function</i>,
    /// reached by evaluating an expression; this is a host-side accessor whose whole contract is "the
    /// string, or nothing", and whose callers - TestScript variable extraction - hand it expressions over
    /// repeating elements precisely because a miss is an ordinary outcome. Letting the spec's error escape
    /// through here would turn a documented <see langword="null"/> into a throw at four call sites that
    /// have no way to act on it.
    /// </remarks>
    public static string? AsString(this IEnumerable<IElement> elements)
    {
        ArgumentNullException.ThrowIfNull(elements);

        var list = elements.ToList();
        return list.Count == 1
            ? TypeConversionFunctions.ToString(list).SingleOrDefault()?.Value as string
            : null;
    }

    /// <summary>
    /// Evaluates a FhirPath expression as a boolean predicate.
    /// Returns true if the expression evaluates to a single true value, false otherwise.
    /// </summary>
    /// <param name="input">The root element to evaluate against</param>
    /// <param name="expression">FhirPath expression string</param>
    /// <param name="context">Optional evaluation context</param>
    /// <returns>True if expression evaluates to true, false otherwise</returns>
    public static bool Predicate(this IElement input, string expression, EvaluationContext? context = null)
    {
        return input.IsTrue(expression, context);
    }

    /// <summary>
    /// Evaluates a FhirPath expression and checks if result is true.
    /// </summary>
    /// <param name="input">The root element to evaluate against</param>
    /// <param name="expression">FhirPath expression string</param>
    /// <param name="context">Optional evaluation context</param>
    /// <returns>True if expression evaluates to a single true boolean value</returns>
    public static bool IsTrue(this IElement input, string expression, EvaluationContext? context = null)
    {
        var results = input.Select(expression, context).ToList();
        return results.Count == 1 && results[0].Value is bool b && b;
    }

    /// <summary>
    /// Evaluates a FhirPath expression and checks if result matches the specified boolean value.
    /// </summary>
    /// <param name="input">The root element to evaluate against</param>
    /// <param name="expression">FhirPath expression string</param>
    /// <param name="value">Expected boolean value</param>
    /// <param name="context">Optional evaluation context</param>
    /// <returns>True if expression evaluates to the specified boolean value</returns>
    public static bool IsBoolean(this IElement input, string expression, bool value, EvaluationContext? context = null)
    {
        var results = input.Select(expression, context).ToList();
        return results.Count == 1 && results[0].Value is bool b && b == value;
    }

    /// <summary>
    /// Clears all expression caches (AST and compiled delegates).
    /// Useful for testing or memory management in long-running processes.
    /// </summary>
    public static void ClearCache()
    {
        _astCache.Clear();
        _delegateCache.Clear();
    }
}
