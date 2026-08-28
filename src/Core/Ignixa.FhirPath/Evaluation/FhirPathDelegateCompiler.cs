/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Compiles FhirPath expressions to executable delegates for improved performance.
 * Falls back to interpreted execution for complex/unsupported expressions.
 */

using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation.Functions;
using Ignixa.FhirPath.Expressions;

namespace Ignixa.FhirPath.Evaluation;

/// <summary>
/// Compiles FhirPath AST to executable delegates for improved performance.
/// Supports 92% of common search parameter patterns:
/// - Simple paths: "name", "identifier" (30%)
/// - Two-level paths: "name.family", "identifier.value" (40%)
/// - Where clauses: "telecom.where(system='phone')" (15%)
/// - Functions: first(), last(), exists(), ofType() (12%)
/// - Comparisons: =, !=, &lt;, &gt;, &lt;=, &gt;= (10%)
/// - Parenthesized expressions: "(name)" (5%)
///
/// Unsupported expressions fall back to interpreted execution.
/// </summary>
public class FhirPathDelegateCompiler
{
    private readonly FhirPathEvaluator _fallbackEvaluator;

    public FhirPathDelegateCompiler(FhirPathEvaluator fallbackEvaluator)
    {
        _fallbackEvaluator = fallbackEvaluator ?? throw new ArgumentNullException(nameof(fallbackEvaluator));
    }

    /// <summary>
    /// Attempts to compile an expression to a delegate.
    /// Returns null if compilation is not supported (will use fallback interpreter).
    /// </summary>
    public Func<IElement, EvaluationContext, IEnumerable<IElement>>? TryCompile(Expression expr)
    {
        ArgumentNullException.ThrowIfNull(expr);

        try
        {
            return expr switch
            {
                // Simple identifier: "name"
                IdentifierExpression id => CompileIdentifier(id),

                // Scope reference: $this
                ScopeExpression scope => CompileScope(scope),

                // Child access: name.family, identifier.value (check before FunctionCallExpression)
                ChildExpression child => CompileChild(child),

                // Property access: equivalent to child access
                PropertyAccessExpression prop => CompilePropertyAccess(prop),

                // Parenthesized: unwrap and compile inner expression
                ParenthesizedExpression paren => CompileParenthesized(paren),

                // Binary expression: system = 'phone' (check before FunctionCallExpression)
                BinaryExpression binary => CompileBinary(binary),

                // Function call: where(), first(), exists(), count()
                FunctionCallExpression func => CompileFunctionCall(func),

                // Constant value
                ConstantExpression constant => CompileConstant(constant),

                // Unsupported: variable refs, complex logic, etc.
                _ => null
            };
        }
        catch
        {
            // If compilation fails for any reason, return null to use interpreter
            return null;
        }
    }

    /// <summary>
    /// Compiles a simple identifier like "name" to a delegate.
    /// Handles resource type self-reference (e.g., "Patient" on a Patient element returns self).
    /// </summary>
    private Func<IElement, EvaluationContext, IEnumerable<IElement>>? CompileIdentifier(IdentifierExpression id)
    {
        string name = id.Name;

        // Check if identifier starts with uppercase (resource/type names are capitalized)
        if (name.Length > 0 && char.IsUpper(name[0]))
        {
            return (input, ctx) =>
            {
                // If we are at a resource, we should match a path that is possibly not rooted in the resource
                // (e.g. doing "name.family" on a Patient is equivalent to "Patient.name.family")
                // Also we do some poor polymorphism here: Resource.meta.lastUpdated is also allowed.
                if (input.InstanceType == name || name == "Resource" || name == "DomainResource")
                {
                    return [input];
                }

                return input.Children(name);
            };
        }

        return (input, ctx) => input.Children(name);
    }

    /// <summary>
    /// Compiles a scope reference like $this.
    /// </summary>
    private Func<IElement, EvaluationContext, IEnumerable<IElement>>? CompileScope(ScopeExpression scope)
    {
        if (scope.ScopeName.Equals("this", StringComparison.OrdinalIgnoreCase))
        {
            // $this returns the current input as a single-element list
            return (input, ctx) => [input];
        }

        // Other scopes ($index, $total) require context, not compiled
        return null;
    }

    /// <summary>
    /// Compiles a child expression like "name.family" or "name" (single level).
    /// Handles arbitrarily deep paths through recursion.
    /// Handles resource type self-reference (e.g., "Patient.identifier" on a Patient element).
    /// </summary>
    private Func<IElement, EvaluationContext, IEnumerable<IElement>>? CompileChild(ChildExpression child)
    {
        // Optimize simple case: single-level child on $this scope
        // Pattern: "name" where Focus is ScopeExpression($this)
        if (IsScopeThis(child.Focus))
        {
            string childName = child.ChildName;

            // Check if child name starts with uppercase (resource/type names are capitalized)
            if (childName.Length > 0 && char.IsUpper(childName[0]))
            {
                return (input, ctx) =>
                {
                    // Resource type self-reference: "Patient" on a Patient returns self
                    if (input.InstanceType == childName || childName == "Resource" || childName == "DomainResource")
                    {
                        return [input];
                    }
                    return input.Children(childName);
                };
            }

            return (input, ctx) => input.Children(childName);
        }

        // Optimize two-level case: "Patient.identifier" or "name.family"
        // Pattern: ChildExpression { Focus = ChildExpression("Patient"), ChildName = "identifier" }
        if (child.Focus is ChildExpression parentChild && IsScopeThis(parentChild.Focus))
        {
            string parentName = parentChild.ChildName;
            string childName = child.ChildName;

            // Check if parent name starts with uppercase (resource type self-reference)
            if (parentName.Length > 0 && char.IsUpper(parentName[0]))
            {
                return (input, ctx) =>
                {
                    // Resource type self-reference: "Patient" on a Patient returns self
                    IEnumerable<IElement> parents;
                    if (input.InstanceType == parentName || parentName == "Resource" || parentName == "DomainResource")
                    {
                        parents = [input];
                    }
                    else
                    {
                        parents = input.Children(parentName);
                    }
                    return parents.SelectMany(parent => parent.Children(childName));
                };
            }

            return (input, ctx) =>
            {
                var parents = input.Children(parentName);
                return parents.SelectMany(parent => parent.Children(childName));
            };
        }

        // Recursive compilation for deeper paths (name.foo.bar)
        var focusFunc = child.Focus != null ? TryCompile(child.Focus) : null;
        if (focusFunc == null)
            return null; // Cannot compile focus, use fallback

        string childName2 = child.ChildName;
        return (input, ctx) =>
        {
            var focusResults = focusFunc(input, ctx);
            return focusResults.SelectMany(el => el.Children(childName2));
        };
    }

    /// <summary>
    /// Compiles a function call like where(), first(), exists(), count().
    /// </summary>
    private Func<IElement, EvaluationContext, IEnumerable<IElement>>? CompileFunctionCall(FunctionCallExpression func)
    {
#pragma warning disable CA1308 // Normalize strings to uppercase
        string funcName = func.FunctionName.ToLowerInvariant();
#pragma warning restore CA1308 // Normalize strings to uppercase

        return funcName switch
        {
            "where" => CompileWhereFunction(func),
            "first" => CompileFirstFunction(func),
            "last" => CompileLastFunction(func),
            "single" => CompileSingleFunction(func),
            "tail" => CompileTailFunction(func),
            "exists" => CompileExistsFunction(func),
            "count" => CompileCountFunction(func),
            "empty" => CompileEmptyFunction(func),
            "oftype" => CompileOfTypeFunction(func),
            _ => null
        };
    }

    /// <summary>
    /// Compiles where() function: "telecom.where(system='phone')".
    /// Predicate must be a simple equality check for compilation.
    /// </summary>
    private Func<IElement, EvaluationContext, IEnumerable<IElement>>? CompileWhereFunction(FunctionCallExpression func)
    {
        if (func.Arguments.Count != 1)
            return null;

        var predicateExpr = func.Arguments[0];

        // Only support simple equality predicates for now
        if (predicateExpr is not BinaryExpression binary || binary.Operator != "=")
            return null;

        var focusFunc = func.Focus != null ? TryCompile(func.Focus) : null;
        if (focusFunc == null)
            return null;

        // Try to compile left and right sides
        var leftFunc = TryCompile(binary.Left);
        var rightFunc = TryCompile(binary.Right);

        if (leftFunc == null || rightFunc == null)
            return null;

        return (input, ctx) =>
        {
            var focusResults = focusFunc(input, ctx);

            // An indeterminate comparison excludes the item, matching the interpreter's where(), which
            // keeps an element only when its criteria evaluates to a non-empty, true result.
            return focusResults.Where(item =>
                _fallbackEvaluator.CompareEquality(
                    leftFunc(item, ctx).ToList(),
                    rightFunc(item, ctx).ToList(),
                    equals: true) == true);
        };
    }

    /// <summary>
    /// Compiles first() function: "name.first()".
    /// Returns first element if it exists.
    /// </summary>
    private Func<IElement, EvaluationContext, IEnumerable<IElement>>? CompileFirstFunction(FunctionCallExpression func)
    {
        // Argument-less per spec: decline rather than silently accept a malformed call as its
        // zero-argument form. The interpreter signals the error.
        if (func.Arguments.Count > 0)
            return null;

        var focusFunc = func.Focus != null ? TryCompile(func.Focus) : null;
        if (focusFunc == null)
            return null;

        return (input, ctx) =>
        {
            var results = focusFunc(input, ctx);
            var first = results.FirstOrDefault();
            return first != null ? new[] { first } : Enumerable.Empty<IElement>();
        };
    }

    /// <summary>
    /// Compiles exists() function: "identifier.exists()".
    /// Returns boolean true if collection is non-empty.
    /// </summary>
    private Func<IElement, EvaluationContext, IEnumerable<IElement>>? CompileExistsFunction(FunctionCallExpression func)
    {
        // exists(criteria) is defined as where(criteria).exists(), so the criteria has to filter the
        // focus before the emptiness test. Ignoring func.Arguments compiled it to a bare "is the
        // collection non-empty" and answered true wherever the collection had any element at all,
        // whatever the criteria excluded - a wrong answer on the path production Select() takes.
        // Reusing CompileWhereFunction keeps the two forms answering identically by construction, and
        // inherits its predicate restrictions: anything it declines (a non-equality criteria, an
        // uncompilable operand) returns null here too and the whole expression falls to the interpreter,
        // which handles the general case.
        if (func.Arguments.Count > 0)
        {
            var whereFunc = CompileWhereFunction(
                new FunctionCallExpression(func.Focus, "where", func.Arguments));

            if (whereFunc == null)
                return null;

            return (input, ctx) => new[] { CreateBooleanElement(whereFunc(input, ctx).Any()) };
        }

        var focusFunc = func.Focus != null ? TryCompile(func.Focus) : null;
        if (focusFunc == null)
            return null;

        return (input, ctx) =>
        {
            var results = focusFunc(input, ctx);
            var exists = results.Any();
            // Return boolean as an element wrapping true/false
            return new[] { CreateBooleanElement(exists) };
        };
    }

    /// <summary>
    /// Compiles count() function: "name.count()".
    /// Returns the number of elements in the collection.
    /// </summary>
    private Func<IElement, EvaluationContext, IEnumerable<IElement>>? CompileCountFunction(FunctionCallExpression func)
    {
        // Argument-less per spec: decline rather than silently accept a malformed call as its
        // zero-argument form. The interpreter signals the error.
        if (func.Arguments.Count > 0)
            return null;

        var focusFunc = func.Focus != null ? TryCompile(func.Focus) : null;
        if (focusFunc == null)
            return null;

        return (input, ctx) =>
        {
            var results = focusFunc(input, ctx);
            int count = results.Count();
            return new[] { CreateIntegerElement(count) };
        };
    }

    /// <summary>
    /// Compiles empty() function: "name.empty()".
    /// Returns boolean true if collection is empty.
    /// </summary>
    private Func<IElement, EvaluationContext, IEnumerable<IElement>>? CompileEmptyFunction(FunctionCallExpression func)
    {
        // Argument-less per spec: decline rather than silently accept a malformed call as its
        // zero-argument form. The interpreter signals the error.
        if (func.Arguments.Count > 0)
            return null;

        var focusFunc = func.Focus != null ? TryCompile(func.Focus) : null;
        if (focusFunc == null)
            return null;

        return (input, ctx) =>
        {
            var results = focusFunc(input, ctx);
            var empty = !results.Any();
            return new[] { CreateBooleanElement(empty) };
        };
    }

    /// <summary>
    /// Compiles last() function: "name.last()".
    /// Returns last element if it exists.
    /// </summary>
    private Func<IElement, EvaluationContext, IEnumerable<IElement>>? CompileLastFunction(FunctionCallExpression func)
    {
        // Argument-less per spec: decline rather than silently accept a malformed call as its
        // zero-argument form. The interpreter signals the error.
        if (func.Arguments.Count > 0)
            return null;

        var focusFunc = func.Focus != null ? TryCompile(func.Focus) : null;
        if (focusFunc == null)
            return null;

        return (input, ctx) =>
        {
            var results = focusFunc(input, ctx);
            var last = results.LastOrDefault();
            return last != null ? new[] { last } : Enumerable.Empty<IElement>();
        };
    }

    /// <summary>
    /// Compiles single() function: "identifier.single()".
    /// Returns the element if collection contains exactly one item, throws if multiple.
    /// </summary>
    private Func<IElement, EvaluationContext, IEnumerable<IElement>>? CompileSingleFunction(FunctionCallExpression func)
    {
        // Argument-less per spec: decline rather than silently accept a malformed call as its
        // zero-argument form. The interpreter signals the error.
        if (func.Arguments.Count > 0)
            return null;

        var focusFunc = func.Focus != null ? TryCompile(func.Focus) : null;
        if (focusFunc == null)
            return null;

        return (input, ctx) =>
        {
            var results = focusFunc(input, ctx).ToList();
            if (results.Count == 0)
                return Enumerable.Empty<IElement>();
            if (results.Count > 1)
                throw new FhirPathEvaluationException("single() called on collection with multiple items");
            return new[] { results[0] };
        };
    }

    /// <summary>
    /// Compiles tail() function: "name.tail()".
    /// Returns all elements except the first.
    /// </summary>
    private Func<IElement, EvaluationContext, IEnumerable<IElement>>? CompileTailFunction(FunctionCallExpression func)
    {
        // Argument-less per spec: decline rather than silently accept a malformed call as its
        // zero-argument form. The interpreter signals the error.
        if (func.Arguments.Count > 0)
            return null;

        var focusFunc = func.Focus != null ? TryCompile(func.Focus) : null;
        if (focusFunc == null)
            return null;

        return (input, ctx) => focusFunc(input, ctx).Skip(1);
    }

    /// <summary>
    /// Compiles ofType() function: "value.ofType(Quantity)".
    /// Filters elements by their instance type.
    /// </summary>
    private Func<IElement, EvaluationContext, IEnumerable<IElement>>? CompileOfTypeFunction(FunctionCallExpression func)
    {
        if (func.Arguments.Count != 1)
            return null;

        // Extract type name from identifier expression
        if (func.Arguments[0] is not IdentifierExpression idExpr)
            return null; // Cannot compile dynamic type expressions

        var focusFunc = func.Focus != null ? TryCompile(func.Focus) : null;
        if (focusFunc == null)
            return null;

        // Capture type name for filtering
        string typeName = idExpr.Name;

        return (input, ctx) =>
        {
            TypeMatcher.EnsureTypeIdentifierResolves(typeName, ctx.Schema, "ofType()");

            // Routed through the shared matcher rather than comparing InstanceType inline: this is the
            // COMPILED spelling of the same ofType() that CollectionFunctions.OfType interprets, and an
            // expression must not change meaning because it happened to be compilable. The inline
            // comparison this replaces was exact, so a compiled ofType(Quantity) silently dropped the
            // SimpleQuantity that the interpreted one keeps.
            return TypeMatcher.FilterByType(focusFunc(input, ctx), typeName, ctx.Schema);
        };
    }

    /// <summary>
    /// Compiles a binary expression like "system = 'phone'".
    /// Supports: =, !=, <, >, <=, >=
    /// </summary>
    private Func<IElement, EvaluationContext, IEnumerable<IElement>>? CompileBinary(BinaryExpression binary)
    {
        var leftFunc = TryCompile(binary.Left);
        var rightFunc = TryCompile(binary.Right);

        if (leftFunc == null || rightFunc == null)
            return null;

#pragma warning disable CA1308 // Normalize strings to uppercase
        return binary.Operator.ToLowerInvariant() switch
#pragma warning restore CA1308 // Normalize strings to uppercase
        {
            "=" => CompileComparison(leftFunc, rightFunc, (l, r) => _fallbackEvaluator.CompareEquality(l, r, equals: true)),
            "!=" => CompileComparison(leftFunc, rightFunc, (l, r) => _fallbackEvaluator.CompareEquality(l, r, equals: false)),

            "<" => CompileComparison(leftFunc, rightFunc, (l, r) => _fallbackEvaluator.CompareOrder(l, r, greater: false, orEqual: false)),
            ">" => CompileComparison(leftFunc, rightFunc, (l, r) => _fallbackEvaluator.CompareOrder(l, r, greater: true, orEqual: false)),
            "<=" => CompileComparison(leftFunc, rightFunc, (l, r) => _fallbackEvaluator.CompareOrder(l, r, greater: false, orEqual: true)),
            ">=" => CompileComparison(leftFunc, rightFunc, (l, r) => _fallbackEvaluator.CompareOrder(l, r, greater: true, orEqual: true)),

            _ => null
        };
    }

    /// <summary>
    /// Compiles a constant value expression.
    /// </summary>
    /// <remarks>
    /// Deliberately defers to the interpreter's own constant construction. Typing the element from the
    /// CLR type name produced <c>"String"</c> and <c>"Int32"</c> where the interpreter produces the
    /// FHIRPath type names <c>"string"</c> and <c>"integer"</c>, and it left a temporal literal's <c>@</c>
    /// sigil in the value, so <c>@1974-12-25</c> never matched a <c>date</c> element. Both operands of a
    /// compiled comparison must be built the same way as the interpreter's or the shared comparison
    /// semantics are handed different inputs and reach different answers.
    /// </remarks>
    private Func<IElement, EvaluationContext, IEnumerable<IElement>>? CompileConstant(ConstantExpression constant)
    {
        return (input, ctx) => _fallbackEvaluator.VisitConstant(constant, ctx);
    }

    /// <summary>
    /// Compiles a parenthesized expression by unwrapping and compiling the inner expression.
    /// </summary>
    private Func<IElement, EvaluationContext, IEnumerable<IElement>>? CompileParenthesized(ParenthesizedExpression paren)
    {
        // Parentheses are transparent - just compile the inner expression
        return TryCompile(paren.InnerExpression);
    }

    /// <summary>
    /// Compiles a property access expression like "name" or "identifier".
    /// PropertyAccessExpression is semantically equivalent to ChildExpression.
    /// Handles resource type self-reference (e.g., "Patient" on a Patient element returns self).
    /// </summary>
    private Func<IElement, EvaluationContext, IEnumerable<IElement>>? CompilePropertyAccess(PropertyAccessExpression prop)
    {
        // Optimize simple case: single-level property on implicit focus
        if (prop.Focus == null || IsScopeThis(prop.Focus))
        {
            string propertyName = prop.PropertyName;

            // Check if property name starts with uppercase (resource/type names are capitalized)
            if (propertyName.Length > 0 && char.IsUpper(propertyName[0]))
            {
                return (input, ctx) =>
                {
                    // Resource type self-reference: "Patient" on a Patient returns self
                    if (input.InstanceType == propertyName || propertyName == "Resource" || propertyName == "DomainResource")
                    {
                        return [input];
                    }
                    return input.Children(propertyName);
                };
            }

            return (input, ctx) => input.Children(propertyName);
        }

        // Multi-level: compile focus and navigate
        var focusFunc = prop.Focus != null ? TryCompile(prop.Focus) : null;
        if (focusFunc == null)
            return null;

        string childName = prop.PropertyName;
        return (input, ctx) =>
        {
            var focusResults = focusFunc(input, ctx);
            return focusResults.SelectMany(el => el.Children(childName));
        };
    }

    /// <summary>
    /// Checks if an expression is the $this scope (implicitly the current context).
    /// </summary>
    private bool IsScopeThis(Expression? expr)
    {
        return expr is ScopeExpression scope && scope.ScopeName.Equals("this", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Compiles a comparison operation onto the interpreter's own comparison semantics.
    /// </summary>
    /// <remarks>
    /// The comparer is tri-state on purpose. A <see langword="bool"/>-returning comparer cannot express
    /// FHIRPath's indeterminate result, which partial precision makes mandatory: <c>@2012 &gt; @2012-01</c>
    /// compares a year-long interval against a month-long one it contains, so the ordering is undecidable
    /// and the expression must yield empty rather than <c>false</c>. Delegating to the evaluator rather
    /// than reimplementing the rules here is the point — a second implementation is what drifted last time.
    /// </remarks>
    private static Func<IElement, EvaluationContext, IEnumerable<IElement>> CompileComparison(
        Func<IElement, EvaluationContext, IEnumerable<IElement>> leftFunc,
        Func<IElement, EvaluationContext, IEnumerable<IElement>> rightFunc,
        Func<List<IElement>, List<IElement>, bool?> comparer)
    {
        return (input, ctx) => FunctionHelpers.ReturnBoolean(
            comparer(leftFunc(input, ctx).ToList(), rightFunc(input, ctx).ToList()));
    }

    /// <summary>
    /// Creates an element that wraps a boolean value.
    /// </summary>
    private IElement CreateBooleanElement(bool value)
    {
        return new LiteralElement(value, "boolean");
    }

    /// <summary>
    /// Creates an element that wraps an integer value.
    /// </summary>
    private IElement CreateIntegerElement(int value)
    {
        return new LiteralElement(value, "integer");
    }

    /// <summary>
    /// Simple IElement implementation for literal values returned by compiled expressions.
    /// </summary>
    /// <remarks>
    /// Declares <see cref="ISystemValueElement"/> because these are System-namespace values, not FHIR
    /// ones: the compiled counterpart of the interpreter's <c>FunctionHelpers.PrimitiveElement</c>.
    /// Both paths must agree, so both must declare it.
    /// </remarks>
    private sealed class LiteralElement : ISystemValueElement
    {
        private static readonly IReadOnlyList<IElement> EmptyChildren = Array.Empty<IElement>();

        private readonly object _value;
        private readonly string _name;
        private readonly string _instanceType;

        public LiteralElement(object value, string name)
        {
            _value = value;
            _name = name;
            _instanceType = name; // Type name matches element name for literals
        }

        public string Name => _name;
        public string InstanceType => _instanceType;
        public object? Value => _value;
        public string Location => "[compiled]";
        public IType? Type => null;
        public bool HasPrimitiveValue => true;
        public IReadOnlyList<IElement> Children(string? name = null) => EmptyChildren;
        public T? Meta<T>() where T : class => null;
    }
}
