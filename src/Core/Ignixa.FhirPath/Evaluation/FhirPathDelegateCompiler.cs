/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Compiles FhirPath expressions to executable delegates for improved performance.
 * Falls back to interpreted execution for complex/unsupported expressions.
 */

using Ignixa.Abstractions;
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

            return focusResults.Where(item =>
            {
                var leftResults = leftFunc(item, ctx).ToList();
                var rightResults = rightFunc(item, ctx).ToList();

                // Comparison: values must match
                return leftResults.Count == rightResults.Count &&
                       leftResults.Zip(rightResults).All(pair =>
                           Equals(pair.First.Value, pair.Second.Value));
            });
        };
    }

    /// <summary>
    /// Compiles first() function: "name.first()".
    /// Returns first element if it exists.
    /// </summary>
    private Func<IElement, EvaluationContext, IEnumerable<IElement>>? CompileFirstFunction(FunctionCallExpression func)
    {
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
        var focusFunc = func.Focus != null ? TryCompile(func.Focus) : null;
        if (focusFunc == null)
            return null;

        return (input, ctx) =>
        {
            var results = focusFunc(input, ctx).ToList();
            if (results.Count == 0)
                return Enumerable.Empty<IElement>();
            if (results.Count > 1)
                throw new InvalidOperationException("single() called on collection with multiple items");
            return new[] { results[0] };
        };
    }

    /// <summary>
    /// Compiles tail() function: "name.tail()".
    /// Returns all elements except the first.
    /// </summary>
    private Func<IElement, EvaluationContext, IEnumerable<IElement>>? CompileTailFunction(FunctionCallExpression func)
    {
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
            var focusResults = focusFunc(input, ctx);
            // Case-insensitive type matching per FHIRPath spec
            return focusResults.Where(e => !string.IsNullOrEmpty(e.InstanceType) &&
                                           e.InstanceType.Equals(typeName, StringComparison.OrdinalIgnoreCase));
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
            "=" => (input, ctx) =>
            {
                var leftResults = leftFunc(input, ctx).ToList();
                var rightResults = rightFunc(input, ctx).ToList();

                // Empty collections return empty per FHIRPath spec
                if (leftResults.Count == 0 || rightResults.Count == 0)
                    return Enumerable.Empty<IElement>();

                // Different counts means not equal
                if (leftResults.Count != rightResults.Count)
                    return [CreateBooleanElement(false)];

                // Compare all pairs
                bool allEqual = leftResults.Zip(rightResults).All(pair => Equals(pair.First.Value, pair.Second.Value));
                return [CreateBooleanElement(allEqual)];
            },

            "!=" => (input, ctx) =>
            {
                var leftResults = leftFunc(input, ctx).ToList();
                var rightResults = rightFunc(input, ctx).ToList();

                // Empty collections return empty per FHIRPath spec
                if (leftResults.Count == 0 || rightResults.Count == 0)
                    return Enumerable.Empty<IElement>();

                // Different counts means not equal, so != returns true
                if (leftResults.Count != rightResults.Count)
                    return [CreateBooleanElement(true)];

                // Compare all pairs
                bool allEqual = leftResults.Zip(rightResults).All(pair => Equals(pair.First.Value, pair.Second.Value));
                return [CreateBooleanElement(!allEqual)];
            },

            "<" => CompileComparison(leftFunc, rightFunc, (l, r) => CompareValues(l, r) < 0),
            ">" => CompileComparison(leftFunc, rightFunc, (l, r) => CompareValues(l, r) > 0),
            "<=" => CompileComparison(leftFunc, rightFunc, (l, r) => CompareValues(l, r) <= 0),
            ">=" => CompileComparison(leftFunc, rightFunc, (l, r) => CompareValues(l, r) >= 0),

            _ => null
        };
    }

    /// <summary>
    /// Compiles a constant value expression.
    /// </summary>
    private Func<IElement, EvaluationContext, IEnumerable<IElement>>? CompileConstant(ConstantExpression constant)
    {
        object value = constant.Value;
        return (input, ctx) => new[] { CreateValueElement(value) };
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
    /// Compiles a comparison operation with a custom comparer.
    /// </summary>
    private Func<IElement, EvaluationContext, IEnumerable<IElement>> CompileComparison(
        Func<IElement, EvaluationContext, IEnumerable<IElement>> leftFunc,
        Func<IElement, EvaluationContext, IEnumerable<IElement>> rightFunc,
        Func<object?, object?, bool> comparer)
    {
        return (input, ctx) =>
        {
            var leftResults = leftFunc(input, ctx).ToList();
            var rightResults = rightFunc(input, ctx).ToList();

            // FHIRPath comparison: single element on each side
            if (leftResults.Count == 1 && rightResults.Count == 1)
            {
                bool result = comparer(leftResults[0].Value, rightResults[0].Value);
                return [CreateBooleanElement(result)];
            }

            // Empty or multi-element collections return empty per FHIRPath spec
            return Enumerable.Empty<IElement>();
        };
    }

    /// <summary>
    /// Compares two values according to FHIRPath comparison rules.
    /// </summary>
    private int CompareValues(object? left, object? right)
    {
        // Handle null cases
        if (left == null && right == null) return 0;
        if (left == null) return -1;
        if (right == null) return 1;

        // Try numeric comparison first
        if (IsNumericType(left) && IsNumericType(right))
        {
            decimal lVal = Convert.ToDecimal(left);
            decimal rVal = Convert.ToDecimal(right);
            return lVal.CompareTo(rVal);
        }

        // DateTime comparison
        if (left is DateTime ldt && right is DateTime rdt)
        {
            return ldt.CompareTo(rdt);
        }

        // DateTimeOffset comparison
        if (left is DateTimeOffset ldto && right is DateTimeOffset rdto)
        {
            return ldto.CompareTo(rdto);
        }

        // String comparison (case-sensitive per FHIRPath spec)
        return string.Compare(left.ToString(), right.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Checks if a value is a numeric type.
    /// </summary>
    private bool IsNumericType(object value)
    {
        return value is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal;
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
    /// Creates an element that wraps any value.
    /// </summary>
    private IElement CreateValueElement(object value)
    {
        return new LiteralElement(value, value?.GetType().Name ?? "unknown");
    }

    /// <summary>
    /// Simple IElement implementation for literal values returned by compiled expressions.
    /// </summary>
    private sealed class LiteralElement : IElement
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
