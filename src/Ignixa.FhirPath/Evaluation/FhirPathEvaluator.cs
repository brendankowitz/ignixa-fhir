/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * FhirPath expression evaluator.
 * Executes parsed FhirPath AST against ITypedElement trees.
 */

using Ignixa.FhirPath.Expressions;
using Ignixa.Abstractions;

namespace Ignixa.FhirPath.Evaluation;

/// <summary>
/// Evaluates FhirPath expressions against FHIR resources represented as ITypedElement trees.
/// </summary>
public class FhirPathEvaluator
{
    /// <summary>
    /// Evaluates a FhirPath expression against an input element and returns matching elements.
    /// </summary>
    /// <param name="input">The root element to evaluate against</param>
    /// <param name="expression">The parsed FhirPath expression</param>
    /// <param name="context">Optional evaluation context</param>
    /// <returns>Collection of elements that match the expression</returns>
    public IEnumerable<ITypedElement> Evaluate(ITypedElement input, Expression expression, EvaluationContext? context = null)
    {
        context ??= new EvaluationContext();

        return EvaluateExpression(new[] { input }, expression, context);
    }

    private IEnumerable<ITypedElement> EvaluateExpression(IEnumerable<ITypedElement> focus, Expression expr, EvaluationContext context)
    {
        return expr switch
        {
            // Check specific types before base types (ChildExpression/BinaryExpression/UnaryExpression/IndexerExpression inherit from FunctionCallExpression)
            ChildExpression child => EvaluateChildExpression(focus, child, context),
            BinaryExpression binary => EvaluateBinaryExpression(focus, binary, context),
            UnaryExpression unary => EvaluateUnary(focus, unary, context),
            IndexerExpression indexer => EvaluateIndexer(focus, indexer, context),
            FunctionCallExpression func => EvaluateFunctionCall(focus, func, context),
            ConstantExpression constant => EvaluateConstant(constant),
            AxisExpression axis => EvaluateAxis(focus, axis, context),
            IdentifierExpression id => EvaluateIdentifier(focus, id),
            VariableRefExpression var => EvaluateVariable(var, context),
            ParenthesizedExpression paren => EvaluateExpression(focus, paren.InnerExpression, context),
            EmptyExpression => Enumerable.Empty<ITypedElement>(),
            QuantityExpression => throw new NotImplementedException("Quantity literals not yet supported in evaluation"),
            _ => throw new NotSupportedException($"Expression type {expr.GetType().Name} is not yet supported")
        };
    }

    private IEnumerable<ITypedElement> EvaluateChildExpression(IEnumerable<ITypedElement> focus, ChildExpression child, EvaluationContext context)
    {
        // First evaluate the focus expression if present
        var focusElements = child.Focus != null
            ? EvaluateExpression(focus, child.Focus, context)
            : focus;

        // Then navigate to children with the specified name
        foreach (var element in focusElements)
        {
            foreach (var childElement in element.Children(child.ChildName))
            {
                yield return childElement;
            }
        }
    }

    private IEnumerable<ITypedElement> EvaluateFunctionCall(IEnumerable<ITypedElement> focus, FunctionCallExpression func, EvaluationContext context)
    {
        // Evaluate focus first
        var focusElements = func.Focus != null
            ? EvaluateExpression(focus, func.Focus, context)
            : focus;

        // Handle built-in functions
        // FhirPath function names are case-insensitive, ToLowerInvariant is intentional
#pragma warning disable CA1308 // Normalize strings to uppercase
        return func.FunctionName.ToLowerInvariant() switch
#pragma warning restore CA1308 // Normalize strings to uppercase
        {
            // Existence functions
            "exists" => EvaluateExists(focusElements, func.Arguments, context),
            "empty" => EvaluateEmpty(focusElements),
            "count" => EvaluateCount(focusElements),
            "distinct" => focusElements.Distinct(),
            "isdistinct" => EvaluateIsDistinct(focusElements),

            // Filtering and projection functions
            "where" => EvaluateWhere(focusElements, func.Arguments, context),
            "select" => EvaluateSelect(focusElements, func.Arguments, context),
            "all" => EvaluateAll(focusElements, func.Arguments, context),
            "any" => EvaluateAny(focusElements, func.Arguments, context),
            "repeat" => EvaluateRepeat(focusElements, func.Arguments, context),
            "oftype" => EvaluateOfType(focusElements, func.Arguments, context),
            "as" => EvaluateAs(focusElements, func.Arguments),

            // Subsetting functions
            "first" => EvaluateFirst(focusElements),
            "last" => EvaluateLast(focusElements),
            "single" => EvaluateSingle(focusElements),
            "tail" => EvaluateTail(focusElements),
            "skip" => EvaluateSkip(focusElements, func.Arguments, context),
            "take" => EvaluateTake(focusElements, func.Arguments, context),
            "intersect" => EvaluateIntersect(focusElements, func.Arguments, context),
            "exclude" => EvaluateExclude(focusElements, func.Arguments, context),

            // Combining functions
            "union" => EvaluateUnionFunction(focusElements, func.Arguments, context),
            "combine" => EvaluateCombine(focusElements, func.Arguments, context),

            // Boolean collection functions
            "alltrue" => EvaluateAllTrue(focusElements),
            "anytrue" => EvaluateAnyTrue(focusElements),
            "allfalse" => EvaluateAllFalse(focusElements),
            "anyfalse" => EvaluateAnyFalse(focusElements),
            "not" => EvaluateNot(focusElements),

            // Set operations
            "subsetof" => EvaluateSubsetOf(focusElements, func.Arguments, context),
            "supersetof" => EvaluateSupersetOf(focusElements, func.Arguments, context),

            // Conversion functions
            "tointeger" => EvaluateToInteger(focusElements),
            "todecimal" => EvaluateToDecimal(focusElements),
            "tostring" => EvaluateToString(focusElements),
            "toboolean" => EvaluateToBoolean(focusElements),
            "todate" => EvaluateToDate(focusElements),
            "todatetime" => EvaluateToDateTime(focusElements),
            "totime" => EvaluateToTime(focusElements),
            "toquantity" => EvaluateToQuantity(focusElements, func.Arguments),

            // Type checking and filtering functions
            "convertstointeger" => EvaluateConvertsToInteger(focusElements),
            "convertstodecimal" => EvaluateConvertsToDecimal(focusElements),
            "convertstostring" => EvaluateConvertsToString(focusElements),
            "convertstoboolean" => EvaluateConvertsToBoolean(focusElements),
            "convertstodate" => EvaluateConvertsToDate(focusElements),
            "convertstodatetime" => EvaluateConvertsToDateTime(focusElements),
            "convertstotime" => EvaluateConvertsToTime(focusElements),
            "convertstoquantity" => EvaluateConvertsToQuantity(focusElements, func.Arguments),

            // Conditional function
            "iif" => EvaluateIif(focusElements, func.Arguments, context),

            // String manipulation functions
            "indexof" => EvaluateIndexOf(focusElements, func.Arguments, context),
            "substring" => EvaluateSubstring(focusElements, func.Arguments, context),
            "startswith" => EvaluateStartsWith(focusElements, func.Arguments, context),
            "endswith" => EvaluateEndsWith(focusElements, func.Arguments, context),
            "upper" => EvaluateUpper(focusElements),
            "lower" => EvaluateLower(focusElements),
            "length" => EvaluateLength(focusElements),
            "replace" => EvaluateReplace(focusElements, func.Arguments, context),
            "matches" => EvaluateMatches(focusElements, func.Arguments, context),
            "replacematches" => EvaluateReplaceMatches(focusElements, func.Arguments, context),
            "tochars" => EvaluateToChars(focusElements),
            "join" => EvaluateJoin(focusElements, func.Arguments, context),

            // Boundary functions
            "lowboundary" => EvaluateLowBoundary(focusElements),
            "highboundary" => EvaluateHighBoundary(focusElements),

            // Tree navigation functions
            "children" => EvaluateChildren(focusElements),
            "descendants" => EvaluateDescendants(focusElements),

            // FHIR-specific functions
            "extension" => EvaluateExtension(focusElements, func.Arguments, context),
            "resolve" => EvaluateResolve(focusElements, context),

            // SQL on FHIR v2 Reference functions
            "getresourcekey" => EvaluateGetResourceKey(context),
            "getreferencekey" => EvaluateGetReferenceKey(focusElements, func.Arguments, context),

            // Utility functions
            "trace" => EvaluateTrace(focusElements, func.Arguments, context),
            "now" => EvaluateNow(),
            "today" => EvaluateToday(),
            "timeofday" => EvaluateTimeOfDay(),

            // For bare identifiers (e.g., "Patient"), treat as child navigation
            _ when func.Arguments.Count == 0 && func.Focus == AxisExpression.That
                => EvaluateIdentifier(focus, new IdentifierExpression(func.FunctionName)),

            _ => throw new NotSupportedException($"Function '{func.FunctionName}' is not yet implemented")
        };
    }

    private IEnumerable<ITypedElement> EvaluateIdentifier(IEnumerable<ITypedElement> focus, IdentifierExpression id)
    {
        // Identifiers navigate to child elements, with special handling for resource type names
        foreach (var element in focus)
        {
            // Check if identifier starts with uppercase (resource/type names are capitalized)
            if (id.Name.Length > 0 && char.IsUpper(id.Name[0]))
            {
                // If we are at a resource, we should match a path that is possibly not rooted in the resource
                // (e.g. doing "name.family" on a Patient is equivalent to "Patient.name.family")
                // Also we do some poor polymorphism here: Resource.meta.lastUpdated is also allowed.
                var baseClasses = new[] { "Resource", "DomainResource" };
                if (element.InstanceType == id.Name || baseClasses.Contains(id.Name))
                {
                    yield return element;
                    continue;
                }
            }

            // Navigate to child elements with this name
            foreach (var child in element.Children(id.Name))
            {
                yield return child;
            }
        }
    }

    private IEnumerable<ITypedElement> EvaluateExists(IEnumerable<ITypedElement> focus, IReadOnlyList<Expression> arguments, EvaluationContext context)
    {
        var hasCriteria = arguments.Count > 0;
        bool exists;

        if (hasCriteria)
        {
            // exists(criteria): returns true if any element matches the criteria
            exists = focus.Any(element =>
            {
                var result = EvaluateExpression(new[] { element }, arguments[0], context);
                return result.Any() && IsTrue(result);
            });
        }
        else
        {
            // exists(): returns true if collection is not empty
            exists = focus.Any();
        }

        // Per SQL on FHIR: boolean functions must return [true] or [false], never empty
        // This ensures columns with type: "boolean" get true/false/null, not true/null
        return new[] { CreateBoolean(exists) };
    }

    private IEnumerable<ITypedElement> EvaluateEmpty(IEnumerable<ITypedElement> focus)
    {
        var isEmpty = !focus.Any();
        // Per SQL on FHIR: boolean functions must return [true] or [false], never empty
        return new[] { CreateBoolean(isEmpty) };
    }

    private IEnumerable<ITypedElement> EvaluateCount(IEnumerable<ITypedElement> focus)
    {
        var count = focus.Count();
        return new[] { CreateInteger(count) };
    }

    private IEnumerable<ITypedElement> EvaluateFirst(IEnumerable<ITypedElement> focus)
    {
        var first = focus.FirstOrDefault();
        return first != null ? new[] { first } : Enumerable.Empty<ITypedElement>();
    }

    private IEnumerable<ITypedElement> EvaluateLast(IEnumerable<ITypedElement> focus)
    {
        var last = focus.LastOrDefault();
        return last != null ? new[] { last } : Enumerable.Empty<ITypedElement>();
    }

    private IEnumerable<ITypedElement> EvaluateWhere(IEnumerable<ITypedElement> focus, IReadOnlyList<Expression> arguments, EvaluationContext context)
    {
        if (arguments.Count == 0)
            throw new ArgumentException("where() requires a criteria argument");

        var criteria = arguments[0];

        foreach (var element in focus)
        {
            // Evaluate criteria with $this bound to current element
            var oldThis = context.GetEnvironmentVariable("this");
            context.SetEnvironmentVariable("this", element);

            try
            {
                var result = EvaluateExpression(new[] { element }, criteria, context);
                if (result.Any() && IsTrue(result))
                {
                    yield return element;
                }
            }
            finally
            {
                if (oldThis != null)
                    context.SetEnvironmentVariable("this", oldThis);
                else
                    context.RemoveEnvironmentVariable("this");
            }
        }
    }

    private IEnumerable<ITypedElement> EvaluateSelect(IEnumerable<ITypedElement> focus, IReadOnlyList<Expression> arguments, EvaluationContext context)
    {
        if (arguments.Count == 0)
            throw new ArgumentException("select() requires a projection argument");

        var projection = arguments[0];

        foreach (var element in focus)
        {
            foreach (var result in EvaluateExpression(new[] { element }, projection, context))
            {
                yield return result;
            }
        }
    }

    private IEnumerable<ITypedElement> EvaluateAll(IEnumerable<ITypedElement> focus, IReadOnlyList<Expression> arguments, EvaluationContext context)
    {
        if (arguments.Count == 0)
            throw new ArgumentException("all() requires a criteria argument");

        var criteria = arguments[0];
        var allMatch = focus.All(element =>
        {
            var result = EvaluateExpression(new[] { element }, criteria, context);
            return result.Any() && IsTrue(result);
        });

        // Per SQL on FHIR: boolean functions must return [true] or [false], never empty
        return new[] { CreateBoolean(allMatch) };
    }

    private IEnumerable<ITypedElement> EvaluateAny(IEnumerable<ITypedElement> focus, IReadOnlyList<Expression> arguments, EvaluationContext context)
    {
        if (arguments.Count == 0)
        {
            // any() without criteria: returns true if collection is not empty
            // Per SQL on FHIR: boolean functions must return [true] or [false], never empty
            return new[] { CreateBoolean(focus.Any()) };
        }

        var criteria = arguments[0];
        var anyMatch = focus.Any(element =>
        {
            var result = EvaluateExpression(new[] { element }, criteria, context);
            return result.Any() && IsTrue(result);
        });

        // Per SQL on FHIR: boolean functions must return [true] or [false], never empty
        return new[] { CreateBoolean(anyMatch) };
    }

    // Subsetting functions
    private IEnumerable<ITypedElement> EvaluateSingle(IEnumerable<ITypedElement> focus)
    {
        var list = focus.ToList();
        if (list.Count == 0)
            return Enumerable.Empty<ITypedElement>();

        if (list.Count > 1)
            throw new InvalidOperationException("single() called on collection with multiple items");

        return new[] { list[0] };
    }

    private IEnumerable<ITypedElement> EvaluateTail(IEnumerable<ITypedElement> focus)
    {
        return focus.Skip(1);
    }

    private IEnumerable<ITypedElement> EvaluateSkip(IEnumerable<ITypedElement> focus, IReadOnlyList<Expression> arguments, EvaluationContext context)
    {
        if (arguments.Count == 0)
            throw new ArgumentException("skip() requires a num argument");

        var numResult = EvaluateExpression(focus, arguments[0], context).SingleOrDefault();
        if (numResult?.Value is not int num)
            return Enumerable.Empty<ITypedElement>();

        return num <= 0 ? focus : focus.Skip(num);
    }

    private IEnumerable<ITypedElement> EvaluateTake(IEnumerable<ITypedElement> focus, IReadOnlyList<Expression> arguments, EvaluationContext context)
    {
        if (arguments.Count == 0)
            throw new ArgumentException("take() requires a num argument");

        var numResult = EvaluateExpression(focus, arguments[0], context).SingleOrDefault();
        if (numResult?.Value is not int num)
            return Enumerable.Empty<ITypedElement>();

        return num <= 0 ? Enumerable.Empty<ITypedElement>() : focus.Take(num);
    }

    private IEnumerable<ITypedElement> EvaluateIntersect(IEnumerable<ITypedElement> focus, IReadOnlyList<Expression> arguments, EvaluationContext context)
    {
        if (arguments.Count == 0)
            throw new ArgumentException("intersect() requires an other argument");

        var other = EvaluateExpression(focus, arguments[0], context).ToList();
        var result = new List<ITypedElement>();

        foreach (var item in focus)
        {
            if (other.Any(o => AreEqual(o.Value, item.Value)) && !result.Any(r => AreEqual(r.Value, item.Value)))
            {
                result.Add(item);
            }
        }

        return result;
    }

    private IEnumerable<ITypedElement> EvaluateExclude(IEnumerable<ITypedElement> focus, IReadOnlyList<Expression> arguments, EvaluationContext context)
    {
        if (arguments.Count == 0)
            throw new ArgumentException("exclude() requires an other argument");

        var other = EvaluateExpression(focus, arguments[0], context).ToList();
        var result = new List<ITypedElement>();

        foreach (var item in focus)
        {
            if (!other.Any(o => AreEqual(o.Value, item.Value)))
            {
                result.Add(item);
            }
        }

        return result;
    }

    // Boolean collection functions
    private IEnumerable<ITypedElement> EvaluateAllTrue(IEnumerable<ITypedElement> focus)
    {
        var list = focus.ToList();
        if (list.Count == 0)
            return new[] { CreateBoolean(true) }; // Empty collection returns true

        var allTrue = list.All(e => e.Value is bool b && b);
        // Per SQL on FHIR: boolean functions must return [true] or [false], never empty
        return new[] { CreateBoolean(allTrue) };
    }

    private IEnumerable<ITypedElement> EvaluateAnyTrue(IEnumerable<ITypedElement> focus)
    {
        var list = focus.ToList();
        // Per SQL on FHIR: boolean functions must return [true] or [false], never empty
        // Empty collection means false (no true values found)
        if (list.Count == 0)
            return new[] { CreateBoolean(false) };

        var anyTrue = list.Any(e => e.Value is bool b && b);
        return new[] { CreateBoolean(anyTrue) };
    }

    private IEnumerable<ITypedElement> EvaluateAllFalse(IEnumerable<ITypedElement> focus)
    {
        var list = focus.ToList();
        if (list.Count == 0)
            return new[] { CreateBoolean(true) }; // Empty collection returns true

        var allFalse = list.All(e => e.Value is bool b && !b);
        // Per SQL on FHIR: boolean functions must return [true] or [false], never empty
        return new[] { CreateBoolean(allFalse) };
    }

    private IEnumerable<ITypedElement> EvaluateAnyFalse(IEnumerable<ITypedElement> focus)
    {
        var list = focus.ToList();
        // Per SQL on FHIR: boolean functions must return [true] or [false], never empty
        // Empty collection means false (no false values found)
        if (list.Count == 0)
            return new[] { CreateBoolean(false) };

        var anyFalse = list.Any(e => e.Value is bool b && !b);
        return new[] { CreateBoolean(anyFalse) };
    }

    private IEnumerable<ITypedElement> EvaluateNot(IEnumerable<ITypedElement> focus)
    {
        var list = focus.ToList();

        // Empty collection returns empty (per FHIRPath spec)
        if (list.Count == 0)
            return Enumerable.Empty<ITypedElement>();

        // Single boolean: negate it
        if (list.Count == 1 && list[0].Value is bool b)
        {
            // Per SQL on FHIR: boolean functions must return [true] or [false], never empty
            return new[] { CreateBoolean(!b) };
        }

        // Multiple items or non-boolean: per spec, this is an error
        // Return empty for safety
        return Enumerable.Empty<ITypedElement>();
    }

    // Set operations
    private IEnumerable<ITypedElement> EvaluateSubsetOf(IEnumerable<ITypedElement> focus, IReadOnlyList<Expression> arguments, EvaluationContext context)
    {
        if (arguments.Count == 0)
            throw new ArgumentException("subsetOf() requires an other argument");

        var focusList = focus.ToList();
        var other = EvaluateExpression(focus, arguments[0], context).ToList();

        // Empty collection is subset of any collection
        if (focusList.Count == 0)
            return new[] { CreateBoolean(true) };

        // Check if all focus items are in other
        var isSubset = focusList.All(f => other.Any(o => AreEqual(o.Value, f.Value)));
        // Per SQL on FHIR: boolean functions must return [true] or [false], never empty
        return new[] { CreateBoolean(isSubset) };
    }

    private IEnumerable<ITypedElement> EvaluateSupersetOf(IEnumerable<ITypedElement> focus, IReadOnlyList<Expression> arguments, EvaluationContext context)
    {
        if (arguments.Count == 0)
            throw new ArgumentException("supersetOf() requires an other argument");

        var focusList = focus.ToList();
        var other = EvaluateExpression(focus, arguments[0], context).ToList();

        // Any collection is superset of empty collection
        if (other.Count == 0)
            return new[] { CreateBoolean(true) };

        // Check if all other items are in focus
        var isSuperset = other.All(o => focusList.Any(f => AreEqual(f.Value, o.Value)));
        // Per SQL on FHIR: boolean functions must return [true] or [false], never empty
        return new[] { CreateBoolean(isSuperset) };
    }

    private IEnumerable<ITypedElement> EvaluateIsDistinct(IEnumerable<ITypedElement> focus)
    {
        var list = focus.ToList();
        var distinctCount = list.Select(e => e.Value).Distinct(new ObjectEqualityComparer()).Count();
        var isDistinct = distinctCount == list.Count;
        // Per SQL on FHIR: boolean functions must return [true] or [false], never empty
        return new[] { CreateBoolean(isDistinct) };
    }

    // Filtering and projection functions
    private IEnumerable<ITypedElement> EvaluateRepeat(IEnumerable<ITypedElement> focus, IReadOnlyList<Expression> arguments, EvaluationContext context)
    {
        if (arguments.Count == 0)
            throw new ArgumentException("repeat() requires a projection argument");

        var projection = arguments[0];
        var result = new HashSet<ITypedElement>(new TypedElementEqualityComparer());
        var queue = new Queue<ITypedElement>(focus);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (result.Add(current))
            {
                var projected = EvaluateExpression(new[] { current }, projection, context);
                foreach (var item in projected)
                {
                    if (!result.Contains(item))
                    {
                        queue.Enqueue(item);
                    }
                }
            }
        }

        return result;
    }

    private IEnumerable<ITypedElement> EvaluateOfType(IEnumerable<ITypedElement> focus, IReadOnlyList<Expression> arguments, EvaluationContext context)
    {
        if (arguments.Count == 0)
            throw new ArgumentException("ofType() requires a type argument");

        // Extract type name from identifier expression or evaluate the expression
        string? typeName = null;

        if (arguments[0] is IdentifierExpression idExpr)
        {
            // Type specifier: ofType(string)
            // Parser correctly handles this per FHIRPath spec
            typeName = idExpr.Name;
        }
        else
        {
            // Evaluate the expression to get the type name
            var result = EvaluateExpression(focus, arguments[0], context).ToList();
            if (result.Count > 0)
            {
                typeName = result[0].Value?.ToString();
            }
        }

        if (string.IsNullOrEmpty(typeName))
            return Enumerable.Empty<ITypedElement>();

        // Case-insensitive type name comparison
        // Filter elements where InstanceType matches the requested type name
        return focus.Where(e => !string.IsNullOrEmpty(e.InstanceType) &&
                               e.InstanceType.Equals(typeName, StringComparison.OrdinalIgnoreCase));
    }

    private IEnumerable<ITypedElement> EvaluateAs(IEnumerable<ITypedElement> focus, IReadOnlyList<Expression> arguments)
    {
        // as() is functionally identical to ofType() - it casts/filters by type
        // as(type) returns the input if it is of the specified type, otherwise returns empty
        if (arguments.Count == 0)
            throw new ArgumentException("as() requires a type argument");

        // Extract type name from identifier expression
        if (arguments[0] is not IdentifierExpression idExpr)
            return Enumerable.Empty<ITypedElement>();

        // FhirPath type names are case-insensitive
#pragma warning disable CA1308 // Normalize strings to uppercase
        var typeName = idExpr.Name.ToLowerInvariant();
        return focus.Where(e => e.InstanceType?.ToLowerInvariant() == typeName);
#pragma warning restore CA1308 // Normalize strings to uppercase
    }

    // Combining functions
    private IEnumerable<ITypedElement> EvaluateUnionFunction(IEnumerable<ITypedElement> focus, IReadOnlyList<Expression> arguments, EvaluationContext context)
    {
        if (arguments.Count == 0)
            throw new ArgumentException("union() requires an other argument");

        var other = EvaluateExpression(focus, arguments[0], context).ToList();
        return EvaluateUnion(focus.ToList(), other);
    }

    private IEnumerable<ITypedElement> EvaluateCombine(IEnumerable<ITypedElement> focus, IReadOnlyList<Expression> arguments, EvaluationContext context)
    {
        if (arguments.Count == 0)
            throw new ArgumentException("combine() requires an other argument");

        var other = EvaluateExpression(focus, arguments[0], context);
        return focus.Concat(other); // Combine does NOT eliminate duplicates
    }

    // Helper classes for equality comparison
    private class ObjectEqualityComparer : IEqualityComparer<object?>
    {
        public new bool Equals(object? x, object? y)
        {
            if (x == null && y == null) return true;
            if (x == null || y == null) return false;
            return x.Equals(y);
        }

        public int GetHashCode(object? obj)
        {
            return obj?.GetHashCode() ?? 0;
        }
    }

    private class TypedElementEqualityComparer : IEqualityComparer<ITypedElement>
    {
        public bool Equals(ITypedElement? x, ITypedElement? y)
        {
            if (x == null && y == null) return true;
            if (x == null || y == null) return false;
            if (x.Value == null && y.Value == null) return true;
            if (x.Value == null || y.Value == null) return false;
            return x.Value.Equals(y.Value);
        }

        public int GetHashCode(ITypedElement obj)
        {
            return obj.Value?.GetHashCode() ?? 0;
        }
    }

    // Phase 3: Conversion functions
    private IEnumerable<ITypedElement> EvaluateToInteger(IEnumerable<ITypedElement> focus)
    {
        var list = focus.ToList();
        if (list.Count != 1)
            return Enumerable.Empty<ITypedElement>();

        var value = list[0].Value;
        if (value is int i)
            return new[] { CreateInteger(i) };

        if (value is string s && int.TryParse(s, out var parsed))
            return new[] { CreateInteger(parsed) };

        if (value is decimal d && d == Math.Floor(d) && d >= int.MinValue && d <= int.MaxValue)
            return new[] { CreateInteger((int)d) };

        if (value is bool b)
            return new[] { CreateInteger(b ? 1 : 0) };

        return Enumerable.Empty<ITypedElement>();
    }

    private IEnumerable<ITypedElement> EvaluateToDecimal(IEnumerable<ITypedElement> focus)
    {
        var list = focus.ToList();
        if (list.Count != 1)
            return Enumerable.Empty<ITypedElement>();

        var value = list[0].Value;
        if (value is decimal d)
            return new[] { CreateDecimal(d) };

        if (value is int i)
            return new[] { CreateDecimal(i) };

        if (value is string s && decimal.TryParse(s, out var parsed))
            return new[] { CreateDecimal(parsed) };

        return Enumerable.Empty<ITypedElement>();
    }

    private IEnumerable<ITypedElement> EvaluateToString(IEnumerable<ITypedElement> focus)
    {
        var list = focus.ToList();
        if (list.Count != 1)
            return Enumerable.Empty<ITypedElement>();

        var value = list[0].Value;
        if (value == null)
            return Enumerable.Empty<ITypedElement>();

        return new[] { CreateString(value.ToString()!) };
    }

    private IEnumerable<ITypedElement> EvaluateToBoolean(IEnumerable<ITypedElement> focus)
    {
        var list = focus.ToList();
        if (list.Count != 1)
            return Enumerable.Empty<ITypedElement>();

        var value = list[0].Value;
        if (value is bool b)
            return new[] { CreateBoolean(b) };

        if (value is int i && (i == 0 || i == 1))
            return new[] { CreateBoolean(i == 1) };

        if (value is string s)
        {
            if (s.Equals("true", StringComparison.OrdinalIgnoreCase))
                return new[] { CreateBoolean(true) };
            if (s.Equals("false", StringComparison.OrdinalIgnoreCase))
                return new[] { CreateBoolean(false) };
        }

        return Enumerable.Empty<ITypedElement>();
    }

    private IEnumerable<ITypedElement> EvaluateToDate(IEnumerable<ITypedElement> focus)
    {
        var list = focus.ToList();
        if (list.Count != 1)
            return Enumerable.Empty<ITypedElement>();

        // Simplified: Just return the value if it's a date/datetime string
        var value = list[0].Value;
        if (value is string s)
        {
            // Basic validation for FHIR date format (YYYY-MM-DD)
            if (DateTime.TryParse(s, out _))
                return new[] { new PrimitiveElement(s, "date") };
        }

        return Enumerable.Empty<ITypedElement>();
    }

    private IEnumerable<ITypedElement> EvaluateToDateTime(IEnumerable<ITypedElement> focus)
    {
        var list = focus.ToList();
        if (list.Count != 1)
            return Enumerable.Empty<ITypedElement>();

        var value = list[0].Value;
        if (value is string s)
        {
            if (DateTime.TryParse(s, out _))
                return new[] { new PrimitiveElement(s, "dateTime") };
        }

        return Enumerable.Empty<ITypedElement>();
    }

    private IEnumerable<ITypedElement> EvaluateToTime(IEnumerable<ITypedElement> focus)
    {
        var list = focus.ToList();
        if (list.Count != 1)
            return Enumerable.Empty<ITypedElement>();

        var value = list[0].Value;
        if (value is string s)
        {
            // Basic validation for time format
            if (TimeSpan.TryParse(s, out _))
                return new[] { new PrimitiveElement(s, "time") };
        }

        return Enumerable.Empty<ITypedElement>();
    }

    private IEnumerable<ITypedElement> EvaluateToQuantity(IEnumerable<ITypedElement> focus, IReadOnlyList<Expression> arguments)
    {
        var list = focus.ToList();
        if (list.Count != 1)
            return Enumerable.Empty<ITypedElement>();

        // Simplified implementation - just pass through for now
        return list;
    }

    // Type filtering and checking functions
    private IEnumerable<ITypedElement> EvaluateConvertsToInteger(IEnumerable<ITypedElement> focus)
    {
        var result = EvaluateToInteger(focus);
        return ReturnBoolean(result.Any());
    }

    private IEnumerable<ITypedElement> EvaluateConvertsToDecimal(IEnumerable<ITypedElement> focus)
    {
        var result = EvaluateToDecimal(focus);
        return ReturnBoolean(result.Any());
    }

    private IEnumerable<ITypedElement> EvaluateConvertsToString(IEnumerable<ITypedElement> focus)
    {
        var result = EvaluateToString(focus);
        return ReturnBoolean(result.Any());
    }

    private IEnumerable<ITypedElement> EvaluateConvertsToBoolean(IEnumerable<ITypedElement> focus)
    {
        var result = EvaluateToBoolean(focus);
        return ReturnBoolean(result.Any());
    }

    private IEnumerable<ITypedElement> EvaluateConvertsToDate(IEnumerable<ITypedElement> focus)
    {
        var result = EvaluateToDate(focus);
        return ReturnBoolean(result.Any());
    }

    private IEnumerable<ITypedElement> EvaluateConvertsToDateTime(IEnumerable<ITypedElement> focus)
    {
        var result = EvaluateToDateTime(focus);
        return ReturnBoolean(result.Any());
    }

    private IEnumerable<ITypedElement> EvaluateConvertsToTime(IEnumerable<ITypedElement> focus)
    {
        var result = EvaluateToTime(focus);
        return ReturnBoolean(result.Any());
    }

    private IEnumerable<ITypedElement> EvaluateConvertsToQuantity(IEnumerable<ITypedElement> focus, IReadOnlyList<Expression> arguments)
    {
        var result = EvaluateToQuantity(focus, arguments);
        return ReturnBoolean(result.Any());
    }

    // Conditional function
    private IEnumerable<ITypedElement> EvaluateIif(IEnumerable<ITypedElement> focus, IReadOnlyList<Expression> arguments, EvaluationContext context)
    {
        if (arguments.Count < 2)
            throw new ArgumentException("iif() requires at least criterion and true-result arguments");

        var criterion = EvaluateExpression(focus, arguments[0], context).ToList();

        // Empty condition returns empty
        if (criterion.Count == 0)
            return Enumerable.Empty<ITypedElement>();

        // True condition returns true branch
        if (IsTrue(criterion))
        {
            return EvaluateExpression(focus, arguments[1], context);
        }

        // False condition returns false branch (if provided)
        if (arguments.Count > 2)
        {
            return EvaluateExpression(focus, arguments[2], context);
        }

        return Enumerable.Empty<ITypedElement>();
    }

    // String manipulation functions
    private IEnumerable<ITypedElement> EvaluateIndexOf(IEnumerable<ITypedElement> focus, IReadOnlyList<Expression> arguments, EvaluationContext context)
    {
        if (arguments.Count == 0)
            throw new ArgumentException("indexOf() requires a substring argument");

        var list = focus.ToList();
        if (list.Count != 1 || list[0].Value is not string str)
            return Enumerable.Empty<ITypedElement>();

        var substringResult = EvaluateExpression(focus, arguments[0], context).SingleOrDefault();
        if (substringResult?.Value is not string substring)
            return Enumerable.Empty<ITypedElement>();

        var index = str.IndexOf(substring, StringComparison.Ordinal);
        return new[] { CreateInteger(index) };
    }

    private IEnumerable<ITypedElement> EvaluateSubstring(IEnumerable<ITypedElement> focus, IReadOnlyList<Expression> arguments, EvaluationContext context)
    {
        if (arguments.Count == 0)
            throw new ArgumentException("substring() requires a start argument");

        var list = focus.ToList();
        if (list.Count != 1 || list[0].Value is not string str)
            return Enumerable.Empty<ITypedElement>();

        var startResult = EvaluateExpression(focus, arguments[0], context).SingleOrDefault();
        if (startResult?.Value is not int start)
            return Enumerable.Empty<ITypedElement>();

        if (start < 0 || start >= str.Length)
            return Enumerable.Empty<ITypedElement>();

        int? length = null;
        if (arguments.Count > 1)
        {
            var lengthResult = EvaluateExpression(focus, arguments[1], context).SingleOrDefault();
            if (lengthResult?.Value is int len)
                length = len;
        }

        var result = length.HasValue
            ? str.Substring(start, Math.Min(length.Value, str.Length - start))
            : str.Substring(start);

        return new[] { CreateString(result) };
    }

    private IEnumerable<ITypedElement> EvaluateStartsWith(IEnumerable<ITypedElement> focus, IReadOnlyList<Expression> arguments, EvaluationContext context)
    {
        if (arguments.Count == 0)
            throw new ArgumentException("startsWith() requires a prefix argument");

        var list = focus.ToList();
        if (list.Count != 1 || list[0].Value is not string str)
            return Enumerable.Empty<ITypedElement>();

        var prefixResult = EvaluateExpression(focus, arguments[0], context).SingleOrDefault();
        if (prefixResult?.Value is not string prefix)
            return Enumerable.Empty<ITypedElement>();

        return ReturnBoolean(str.StartsWith(prefix, StringComparison.Ordinal));
    }

    private IEnumerable<ITypedElement> EvaluateEndsWith(IEnumerable<ITypedElement> focus, IReadOnlyList<Expression> arguments, EvaluationContext context)
    {
        if (arguments.Count == 0)
            throw new ArgumentException("endsWith() requires a suffix argument");

        var list = focus.ToList();
        if (list.Count != 1 || list[0].Value is not string str)
            return Enumerable.Empty<ITypedElement>();

        var suffixResult = EvaluateExpression(focus, arguments[0], context).SingleOrDefault();
        if (suffixResult?.Value is not string suffix)
            return Enumerable.Empty<ITypedElement>();

        return ReturnBoolean(str.EndsWith(suffix, StringComparison.Ordinal));
    }

    private IEnumerable<ITypedElement> EvaluateUpper(IEnumerable<ITypedElement> focus)
    {
        var list = focus.ToList();
        if (list.Count != 1 || list[0].Value is not string str)
            return Enumerable.Empty<ITypedElement>();

        return new[] { CreateString(str.ToUpperInvariant()) };
    }

    private IEnumerable<ITypedElement> EvaluateLower(IEnumerable<ITypedElement> focus)
    {
        var list = focus.ToList();
        if (list.Count != 1 || list[0].Value is not string str)
            return Enumerable.Empty<ITypedElement>();

        // FhirPath lower() function explicitly requires lowercase, ToLowerInvariant is intentional
#pragma warning disable CA1308 // Normalize strings to uppercase
        return new[] { CreateString(str.ToLowerInvariant()) };
#pragma warning restore CA1308 // Normalize strings to uppercase
    }

    private IEnumerable<ITypedElement> EvaluateLength(IEnumerable<ITypedElement> focus)
    {
        var list = focus.ToList();
        if (list.Count != 1 || list[0].Value is not string str)
            return Enumerable.Empty<ITypedElement>();

        return new[] { CreateInteger(str.Length) };
    }

    private IEnumerable<ITypedElement> EvaluateReplace(IEnumerable<ITypedElement> focus, IReadOnlyList<Expression> arguments, EvaluationContext context)
    {
        if (arguments.Count < 2)
            throw new ArgumentException("replace() requires pattern and substitution arguments");

        var list = focus.ToList();
        if (list.Count != 1 || list[0].Value is not string str)
            return Enumerable.Empty<ITypedElement>();

        var patternResult = EvaluateExpression(focus, arguments[0], context).SingleOrDefault();
        var substitutionResult = EvaluateExpression(focus, arguments[1], context).SingleOrDefault();

        if (patternResult?.Value is not string pattern || substitutionResult?.Value is not string substitution)
            return Enumerable.Empty<ITypedElement>();

        var result = str.Replace(pattern, substitution, StringComparison.Ordinal);
        return new[] { CreateString(result) };
    }

    private IEnumerable<ITypedElement> EvaluateMatches(IEnumerable<ITypedElement> focus, IReadOnlyList<Expression> arguments, EvaluationContext context)
    {
        if (arguments.Count == 0)
            throw new ArgumentException("matches() requires a regex argument");

        var list = focus.ToList();
        if (list.Count != 1 || list[0].Value is not string str)
            return Enumerable.Empty<ITypedElement>();

        var regexResult = EvaluateExpression(focus, arguments[0], context).SingleOrDefault();
        if (regexResult?.Value is not string pattern)
            return Enumerable.Empty<ITypedElement>();

        try
        {
            var regex = new System.Text.RegularExpressions.Regex(pattern);
            return ReturnBoolean(regex.IsMatch(str));
        }
        catch
        {
            return Enumerable.Empty<ITypedElement>();
        }
    }

    private IEnumerable<ITypedElement> EvaluateReplaceMatches(IEnumerable<ITypedElement> focus, IReadOnlyList<Expression> arguments, EvaluationContext context)
    {
        if (arguments.Count < 2)
            throw new ArgumentException("replaceMatches() requires pattern and substitution arguments");

        var list = focus.ToList();
        if (list.Count != 1 || list[0].Value is not string str)
            return Enumerable.Empty<ITypedElement>();

        var patternResult = EvaluateExpression(focus, arguments[0], context).SingleOrDefault();
        var substitutionResult = EvaluateExpression(focus, arguments[1], context).SingleOrDefault();

        if (patternResult?.Value is not string pattern || substitutionResult?.Value is not string substitution)
            return Enumerable.Empty<ITypedElement>();

        try
        {
            var regex = new System.Text.RegularExpressions.Regex(pattern);
            var result = regex.Replace(str, substitution);
            return new[] { CreateString(result) };
        }
        catch
        {
            return Enumerable.Empty<ITypedElement>();
        }
    }

    private IEnumerable<ITypedElement> EvaluateToChars(IEnumerable<ITypedElement> focus)
    {
        var list = focus.ToList();
        if (list.Count != 1 || list[0].Value is not string str)
            return Enumerable.Empty<ITypedElement>();

        return str.Select(c => CreateString(c.ToString()));
    }

    private IEnumerable<ITypedElement> EvaluateJoin(
        IEnumerable<ITypedElement> focus,
        IReadOnlyList<Expression> arguments,
        EvaluationContext context)
    {
        // join() takes an optional separator parameter
        // If provided, concatenates focus collection with separator
        // If not provided, concatenates without separator
        var focusElements = focus.ToList();

        // Get the separator (default to empty string if not provided)
        var separator = string.Empty;
        if (arguments.Count > 0)
        {
            var sepResult = EvaluateExpression(focusElements, arguments[0], context).ToList();
            if (sepResult.Count > 0 && sepResult[0].Value is string sepStr)
            {
                separator = sepStr;
            }
        }

        // Concatenate all string values with the separator
        var strings = focusElements
            .Where(e => e.Value is string)
            .Select(e => (string)e.Value!)
            .ToList();

        // join() always returns a string, even if empty
        var result = string.Join(separator, strings);
        return new[] { CreateString(result) };
    }

    private IEnumerable<ITypedElement> EvaluateLowBoundary(IEnumerable<ITypedElement> focus)
    {
        // lowBoundary() calculates the low boundary of a value
        // For decimals: multiplies by 0.95 (5% lower)
        // For dates/times: returns the start of the period with UTC+14:00 offset
        foreach (var element in focus)
        {
            if (element.Value == null)
            {
                // Null values return no result (empty collection)
                continue;
            }

            var result = element.Value switch
            {
                // Decimal boundary: 5% lower
                decimal d => CreateDecimal(d * 0.95m),
                double d => CreateDecimal((decimal)d * 0.95m),
                int i => CreateDecimal(i * 0.95m),
                long l => CreateDecimal(l * 0.95m),

                // Date/DateTime boundary: start of period with UTC+14:00 offset
                DateTime dt => CreateString(GetDateTimeLowBoundary(dt)),
                DateTimeOffset dto => CreateString(GetDateTimeOffsetLowBoundary(dto)),

                // String dateTime (when element type is dateTime)
                string s when IsDateLike(s) && string.Equals(element.InstanceType, "dateTime", StringComparison.OrdinalIgnoreCase) => CreateString(GetStringDateTimeLowBoundary(s)),

                // String dates (partial dates, when element type is date)
                string s when IsDateLike(s) => CreateString(GetStringDateLowBoundary(s)),

                // String times (partial times)
                string s when IsTimeLike(s) => CreateString(GetStringTimeLowBoundary(s)),

                // Unsupported type: return no result
                _ => null
            };

            if (result != null)
            {
                yield return result;
            }
        }
    }

    private IEnumerable<ITypedElement> EvaluateHighBoundary(IEnumerable<ITypedElement> focus)
    {
        // highBoundary() calculates the high boundary of a value
        // For decimals: multiplies by 1.05 (5% higher)
        // For dates/times: returns the end of the period with UTC-12:00 offset
        foreach (var element in focus)
        {
            if (element.Value == null)
            {
                // Null values return no result (empty collection)
                continue;
            }

            var result = element.Value switch
            {
                // Decimal boundary: 5% higher
                decimal d => CreateDecimal(d * 1.05m),
                double d => CreateDecimal((decimal)d * 1.05m),
                int i => CreateDecimal(i * 1.05m),
                long l => CreateDecimal(l * 1.05m),

                // Date/DateTime boundary: end of period with UTC-12:00 offset
                DateTime dt => CreateString(GetDateTimeHighBoundary(dt)),
                DateTimeOffset dto => CreateString(GetDateTimeOffsetHighBoundary(dto)),

                // String dateTime (when element type is dateTime)
                string s when IsDateLike(s) && string.Equals(element.InstanceType, "dateTime", StringComparison.OrdinalIgnoreCase) => CreateString(GetStringDateTimeHighBoundary(s)),

                // String dates (partial dates, when element type is date)
                string s when IsDateLike(s) => CreateString(GetStringDateHighBoundary(s)),

                // String times (partial times)
                string s when IsTimeLike(s) => CreateString(GetStringTimeHighBoundary(s)),

                // Unsupported type: return no result
                _ => null
            };

            if (result != null)
            {
                yield return result;
            }
        }
    }

    private static string GetDateTimeLowBoundary(DateTime dt)
    {
        // For a partial date like "1970-06", expand to start with UTC+14:00 timezone
        // If the date is incomplete (day/time missing), use the first possible value
        // Example: "1970-06" becomes "1970-06-01T00:00:00.000+14:00"
        if (dt.Kind == DateTimeKind.Unspecified)
        {
            // Assume it's already the start of the period, just add UTC+14:00 offset
            return dt.ToString("yyyy-MM-ddTHH:mm:ss.fff+14:00");
        }

        // For fully specified DateTime, add UTC+14:00 offset
        return dt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fff+14:00");
    }

    private static string GetDateTimeHighBoundary(DateTime dt)
    {
        // For a partial date like "1970-06", expand to end with UTC-12:00 timezone
        // If the date is incomplete (day/time missing), use the last possible value
        // Example: "1970-06" becomes "1970-06-30T23:59:59.999-12:00"
        if (dt.Kind == DateTimeKind.Unspecified)
        {
            // For unspecified datetime, shift to end of period
            var endDate = new DateTime(dt.Year, dt.Month, DateTime.DaysInMonth(dt.Year, dt.Month), 23, 59, 59, 999);
            return endDate.ToString("yyyy-MM-ddTHH:mm:ss.fff-12:00");
        }

        // For fully specified DateTime, convert to end of period and add UTC-12:00 offset
        var utcDt = dt.ToUniversalTime();
        var endUtc = new DateTime(utcDt.Year, utcDt.Month, DateTime.DaysInMonth(utcDt.Year, utcDt.Month), 23, 59, 59, 999, DateTimeKind.Utc);
        return endUtc.ToString("yyyy-MM-ddTHH:mm:ss.fff-12:00");
    }

    private static string GetDateTimeOffsetLowBoundary(DateTimeOffset dto)
    {
        // Similar to DateTime low boundary, but accounts for the offset
        return dto.DateTime.ToString("yyyy-MM-ddTHH:mm:ss.fff+14:00");
    }

    private static string GetDateTimeOffsetHighBoundary(DateTimeOffset dto)
    {
        // Similar to DateTime high boundary, but accounts for the offset
        var endDate = new DateTime(dto.Year, dto.Month, DateTime.DaysInMonth(dto.Year, dto.Month), 23, 59, 59, 999, DateTimeKind.Unspecified);
        return endDate.ToString("yyyy-MM-ddTHH:mm:ss.fff-12:00");
    }

    private static string GetStringDateLowBoundary(string dateString)
    {
        // Parse partial date string and expand to the start of the period
        // Returns just a date (no time component) per SQL on FHIR v2 spec
        // Examples:
        // "1970" -> "1970-01-01"
        // "1970-06" -> "1970-06-01"
        // "1970-06-15" -> "1970-06-15" (already complete)
        var parts = dateString.Split('-');

        if (parts.Length < 1)
        {
            return dateString;
        }

        int year = int.Parse(parts[0]);
        int month = parts.Length > 1 ? int.Parse(parts[1]) : 1;
        int day = parts.Length > 2 ? int.Parse(parts[2]) : 1;

        // Return date only (yyyy-MM-dd format)
        return $"{year:D4}-{month:D2}-{day:D2}";
    }

    private static string GetStringDateHighBoundary(string dateString)
    {
        // Parse partial date string and expand to the end of the period
        // Returns just a date (no time component) per SQL on FHIR v2 spec
        // Examples:
        // "1970" -> "1970-12-31"
        // "1970-06" -> "1970-06-30"
        // "1970-06-15" -> "1970-06-15" (already complete)
        var parts = dateString.Split('-');

        if (parts.Length < 1)
        {
            return dateString;
        }

        int year = int.Parse(parts[0]);
        int month = parts.Length > 1 ? int.Parse(parts[1]) : 12;
        int day = parts.Length > 2 ? int.Parse(parts[2]) : DateTime.DaysInMonth(year, month);

        // Return date only (yyyy-MM-dd format)
        return $"{year:D4}-{month:D2}-{day:D2}";
    }

    private static string GetStringDateTimeLowBoundary(string dateString)
    {
        // Parse partial date string and expand to the start of the period with UTC+14:00 timezone
        // Returns dateTime format (with time and timezone) per SQL on FHIR v2 spec
        // Examples:
        // "2010-10-10" -> "2010-10-10T00:00:00.000+14:00"
        // "2010-10" -> "2010-10-01T00:00:00.000+14:00"
        // "2010" -> "2010-01-01T00:00:00.000+14:00"
        var parts = dateString.Split('-');

        if (parts.Length < 1)
        {
            return dateString;
        }

        int year = int.Parse(parts[0]);
        int month = parts.Length > 1 ? int.Parse(parts[1]) : 1;
        int day = parts.Length > 2 ? int.Parse(parts[2]) : 1;

        // Return dateTime with UTC+14:00 timezone (yyyy-MM-ddTHH:mm:ss.fff+14:00 format)
        return $"{year:D4}-{month:D2}-{day:D2}T00:00:00.000+14:00";
    }

    private static string GetStringDateTimeHighBoundary(string dateString)
    {
        // Parse partial date string and expand to the end of the period with UTC-12:00 timezone
        // Returns dateTime format (with time and timezone) per SQL on FHIR v2 spec
        // Examples:
        // "2010-10-10" -> "2010-10-10T23:59:59.999-12:00"
        // "2010-10" -> "2010-10-31T23:59:59.999-12:00"
        // "2010" -> "2010-12-31T23:59:59.999-12:00"
        var parts = dateString.Split('-');

        if (parts.Length < 1)
        {
            return dateString;
        }

        int year = int.Parse(parts[0]);
        int month = parts.Length > 1 ? int.Parse(parts[1]) : 12;
        int day = parts.Length > 2 ? int.Parse(parts[2]) : DateTime.DaysInMonth(year, month);

        // Return dateTime with UTC-12:00 timezone (yyyy-MM-ddTHH:mm:ss.fff-12:00 format)
        return $"{year:D4}-{month:D2}-{day:D2}T23:59:59.999-12:00";
    }

    private static string GetStringTimeLowBoundary(string timeString)
    {
        // Parse partial time string and expand to the start of the period
        // Examples:
        // "12" -> "12:00:00.000"
        // "12:34" -> "12:34:00.000"
        // "12:34:56" -> "12:34:56.000"
        // "12:34:56.789" -> "12:34:56.789" (already complete)
        var parts = timeString.Split(':', '.');

        if (parts.Length < 1)
        {
            return timeString;
        }

        int hour = int.Parse(parts[0]);
        int minute = parts.Length > 1 ? int.Parse(parts[1]) : 0;
        int second = parts.Length > 2 ? int.Parse(parts[2]) : 0;
        int millisecond = parts.Length > 3 ? int.Parse(parts[3].PadRight(3, '0').Substring(0, 3)) : 0;

        // Return time with milliseconds (HH:mm:ss.fff format)
        return $"{hour:D2}:{minute:D2}:{second:D2}.{millisecond:D3}";
    }

    private static string GetStringTimeHighBoundary(string timeString)
    {
        // Parse partial time string and expand to the end of the period
        // Examples:
        // "12" -> "12:59:59.999"
        // "12:34" -> "12:34:59.999"
        // "12:34:56" -> "12:34:56.999"
        // "12:34:56.789" -> "12:34:56.789" (already complete)
        var parts = timeString.Split(':', '.');

        if (parts.Length < 1)
        {
            return timeString;
        }

        int hour = int.Parse(parts[0]);
        int minute = parts.Length > 1 ? int.Parse(parts[1]) : 59;
        int second = parts.Length > 2 ? int.Parse(parts[2]) : 59;
        int millisecond = parts.Length > 3 ? int.Parse(parts[3].PadRight(3, '0').Substring(0, 3)) : 999;

        // Return time with milliseconds (HH:mm:ss.fff format)
        return $"{hour:D2}:{minute:D2}:{second:D2}.{millisecond:D3}";
    }

    private static bool IsTimeLike(string value)
    {
        // Check if string looks like a time (HH or HH:mm or HH:mm:ss or HH:mm:ss.fff)
        // Time format uses colons and optional dot for milliseconds
        if (value.Contains('-', StringComparison.Ordinal))
        {
            return false; // Has dashes, likely a date
        }

        var parts = value.Split(':', '.');
        if (parts.Length < 1 || parts.Length > 4)
        {
            return false;
        }

        // Check if all parts are numeric
        foreach (var part in parts)
        {
            if (!int.TryParse(part, out _))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsDateLike(string value)
    {
        // Check if string looks like a date (YYYY or YYYY-MM or YYYY-MM-DD)
        var parts = value.Split('-');
        if (parts.Length < 1 || parts.Length > 3)
        {
            return false;
        }

        foreach (var part in parts)
        {
            if (!int.TryParse(part, out _))
            {
                return false;
            }
        }

        return true;
    }

    // Tree navigation functions
    private IEnumerable<ITypedElement> EvaluateChildren(IEnumerable<ITypedElement> focus)
    {
        foreach (var element in focus)
        {
            foreach (var child in element.Children())
            {
                yield return child;
            }
        }
    }

    private IEnumerable<ITypedElement> EvaluateDescendants(IEnumerable<ITypedElement> focus)
    {
        var result = new List<ITypedElement>();
        var queue = new Queue<ITypedElement>(focus);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var child in current.Children())
            {
                result.Add(child);
                queue.Enqueue(child);
            }
        }

        return result;
    }

    // FHIR-specific functions
    private IEnumerable<ITypedElement> EvaluateExtension(IEnumerable<ITypedElement> focus, IReadOnlyList<Expression> arguments, EvaluationContext context)
    {
        // extension(url : string) : collection
        // Filters the input collection for items named "extension" with the given url
        // Equivalent to: .extension.where(url = <urlValue>)

        if (arguments.Count == 0)
            throw new ArgumentException("extension() requires a url argument");

        // Evaluate the url argument to get the string value
        var urlArgument = arguments[0];
        var urlResult = EvaluateExpression(focus, urlArgument, context).FirstOrDefault();

        if (urlResult == null)
            yield break;

        var urlValue = urlResult.Value?.ToString();
        if (string.IsNullOrEmpty(urlValue))
            yield break;

        // Navigate to "extension" children and filter by url
        foreach (var element in focus)
        {
            foreach (var extension in element.Children("extension"))
            {
                // Check if this extension has a url child with matching value
                var urlChild = extension.Children("url").FirstOrDefault();
                if (urlChild != null && urlChild.Value?.ToString() == urlValue)
                {
                    yield return extension;
                }
            }
        }
    }

    private IEnumerable<ITypedElement> EvaluateResolve(IEnumerable<ITypedElement> focus, EvaluationContext context)
    {
        // resolve() : collection
        // Takes a Reference element and resolves it to the actual resource.
        // Returns empty if the reference cannot be resolved or if ElementResolver is not configured.
        // Per FHIR spec: resolve() returns empty on failure (does not throw).

        var results = new List<ITypedElement>();

        if (context is not FhirEvaluationContext fhirContext || fhirContext.ElementResolver == null)
        {
            // No resolver available - return empty (this is expected during indexing)
            return results;
        }

        foreach (var element in focus)
        {
            // resolve() only works on Reference types
            if (element.InstanceType != "Reference" && element.InstanceType != "ResourceReference")
            {
                // Not a reference - skip
                continue;
            }

            // Extract the reference string (e.g., "Patient/123" or "http://example.org/fhir/Patient/123")
            var referenceValue = element.Scalar("reference") as string;
            if (string.IsNullOrEmpty(referenceValue))
            {
                // No reference value - skip
                continue;
            }

            // Call the ElementResolver to resolve the reference
            try
            {
                var resolved = fhirContext.ElementResolver(referenceValue);
                if (resolved != null)
                {
                    results.Add(resolved);
                }
                // If resolved is null, the reference couldn't be resolved - skip silently
            }
            catch
            {
                // If resolution fails, skip silently (FHIR spec: resolve() returns empty on failure)
                continue;
            }
        }

        return results;
    }

    // SQL on FHIR v2 Reference functions (Section 3.2.8)
    private IEnumerable<ITypedElement> EvaluateGetResourceKey(EvaluationContext context)
    {
        // Per SQL on FHIR v2 spec: getResourceKey() returns "{resourceType}/{id}" for the ROOT resource
        // This enables JOINs across resources and should always reference the root, not the current focus
        var rootResource = context.RootResource ?? context.Resource;
        if (rootResource == null)
        {
            yield break; // No root resource available
        }

        // Get resource type from InstanceType
        var resourceType = rootResource.InstanceType;
        if (string.IsNullOrEmpty(resourceType))
        {
            yield break; // No resource type
        }

        // Get id from the "id" child element
        var idElement = rootResource.Children("id").FirstOrDefault();
        if (idElement == null)
        {
            yield break; // No id
        }

        var id = idElement.Value?.ToString();
        if (string.IsNullOrEmpty(id))
        {
            yield break; // Empty id
        }

        // Return "{resourceType}/{id}"
        var resourceKey = $"{resourceType}/{id}";
        yield return new PrimitiveElement(resourceKey, "string");
    }

    private IEnumerable<ITypedElement> EvaluateGetReferenceKey(IEnumerable<ITypedElement> focus, IReadOnlyList<Expression> arguments, EvaluationContext context)
    {
        // Per SQL on FHIR v2 spec: getReferenceKey([type]) extracts reference from a Reference element
        // Returns the full reference string (e.g., "Patient/123")
        // Optional type parameter filters by resource type - returns empty if type doesn't match

        // Parse optional type argument (matches ofType() implementation pattern)
        string? filterType = null;
        if (arguments.Count > 0)
        {
            // Type argument should be a simple identifier (e.g., "Patient", "Observation")
            if (arguments[0] is IdentifierExpression identExpr)
            {
                filterType = identExpr.Name;
            }
            else if (arguments[0] is FunctionCallExpression funcExpr && funcExpr.Arguments.Count == 0)
            {
                // Sometimes bare identifiers are parsed as zero-argument function calls
                filterType = funcExpr.FunctionName;
            }
            else
            {
                // Fallback: evaluate the expression to get the type name
                var result = EvaluateExpression(focus, arguments[0], context).ToList();
                if (result.Count > 0)
                {
                    filterType = result[0].Value?.ToString();
                }
            }

            // If we couldn't determine the filter type, return empty
            if (string.IsNullOrEmpty(filterType))
            {
                yield break;
            }
        }

        foreach (var element in focus)
        {
            // Get the "reference" child element from the Reference datatype
            var referenceElement = element.Children("reference").FirstOrDefault();
            if (referenceElement == null)
            {
                continue; // Skip if no reference property
            }

            var reference = referenceElement.Value?.ToString();
            if (string.IsNullOrEmpty(reference))
            {
                continue; // Skip if reference is empty
            }

            // If type filter specified, check if reference matches the type
            if (filterType != null)
            {
                // Check if reference starts with "{type}/" (e.g., "Patient/123")
                var expectedPrefix = $"{filterType}/";
                if (!reference.StartsWith(expectedPrefix, StringComparison.Ordinal))
                {
                    // Type mismatch - skip this reference (don't yield anything)
                    continue;
                }
            }

            // Return the full reference string (e.g., "Patient/123")
            // This matches the format returned by getResourceKey()
            yield return new PrimitiveElement(reference, "string");
        }
    }

    // Utility functions
    private IEnumerable<ITypedElement> EvaluateTrace(IEnumerable<ITypedElement> focus, IReadOnlyList<Expression> arguments, EvaluationContext context)
    {
        // Simplified: Just return focus unchanged
        // In a real implementation, this would log to a trace output
        return focus;
    }

    private IEnumerable<ITypedElement> EvaluateNow()
    {
        var now = DateTime.UtcNow.ToString("o");
        return new[] { new PrimitiveElement(now, "dateTime") };
    }

    private IEnumerable<ITypedElement> EvaluateToday()
    {
        var today = DateTime.Today.ToString("yyyy-MM-dd");
        return new[] { new PrimitiveElement(today, "date") };
    }

    private IEnumerable<ITypedElement> EvaluateTimeOfDay()
    {
        var time = DateTime.Now.TimeOfDay.ToString(@"hh\:mm\:ss");
        return new[] { new PrimitiveElement(time, "time") };
    }

    private IEnumerable<ITypedElement> EvaluateBinaryExpression(IEnumerable<ITypedElement> focus, BinaryExpression binary, EvaluationContext context)
    {
        var left = EvaluateExpression(focus, binary.Left, context).ToList();
        var right = EvaluateExpression(focus, binary.Right, context).ToList();

        // FhirPath operators are case-insensitive, ToLowerInvariant is intentional
#pragma warning disable CA1308 // Normalize strings to uppercase
        return binary.Operator.ToLowerInvariant() switch
#pragma warning restore CA1308 // Normalize strings to uppercase
        {
            // Collection operators (return collections)
            "|" => EvaluateUnion(left, right),

            // Math operators (return numeric values)
            "+" => EvaluateAddition(left, right),
            "-" => EvaluateSubtraction(left, right),
            "*" => EvaluateMultiplication(left, right),
            "/" => EvaluateDivision(left, right),
            "div" => EvaluateIntegerDivision(left, right),
            "mod" => EvaluateModulo(left, right),

            // String concatenation (returns string)
            "&" => EvaluateStringConcatenation(left, right),

            // Type operators (special handling for identifiers)
            "is" => EvaluateTypeIs(left, binary.Right),
            "as" => EvaluateTypeAs(left, binary.Right),

            // Membership operators (return boolean)
            "in" => ReturnBoolean(EvaluateMembership(left, right, isIn: true)),
            "contains" => ReturnBoolean(EvaluateMembership(left, right, isIn: false)),

            // Comparison operators (return boolean)
            "=" => ReturnBoolean(CompareEquality(left, right, equals: true)),
            "!=" => ReturnBoolean(CompareEquality(left, right, equals: false)),
            "~" => ReturnBoolean(CompareEquivalence(left, right, equivalent: true)),
            "!~" => ReturnBoolean(CompareEquivalence(left, right, equivalent: false)),
            ">" => ReturnBoolean(CompareOrder(left, right, greater: true, orEqual: false)),
            ">=" => ReturnBoolean(CompareOrder(left, right, greater: true, orEqual: true)),
            "<" => ReturnBoolean(CompareOrder(left, right, greater: false, orEqual: false)),
            "<=" => ReturnBoolean(CompareOrder(left, right, greater: false, orEqual: true)),

            // Logical operators (return boolean)
            "and" => ReturnBoolean(IsTrue(left) && IsTrue(right)),
            "or" => ReturnBoolean(IsTrue(left) || IsTrue(right)),
            "xor" => ReturnBoolean(IsTrue(left) ^ IsTrue(right)),
            "implies" => ReturnBoolean(!IsTrue(left) || IsTrue(right)),

            _ => throw new NotSupportedException($"Binary operator '{binary.Operator}' is not yet implemented")
        };
    }

    // Helper: Convert boolean result to FhirPath collection
    private IEnumerable<ITypedElement> ReturnBoolean(bool? result)
    {
        // Per FHIRPath spec:
        // - true → collection with boolean true
        // - false → collection with boolean false
        // - null → empty collection
        return result.HasValue
            ? new[] { CreateBoolean(result.Value) }
            : Enumerable.Empty<ITypedElement>();
    }

    // Union operator: Merge collections, eliminate duplicates
    private IEnumerable<ITypedElement> EvaluateUnion(List<ITypedElement> left, List<ITypedElement> right)
    {
        var result = new List<ITypedElement>();

        // Add all left elements
        foreach (var leftItem in left)
        {
            if (!result.Any(r => AreEqual(r.Value, leftItem.Value)))
            {
                result.Add(leftItem);
            }
        }

        // Add right elements that aren't duplicates
        foreach (var rightItem in right)
        {
            if (!result.Any(r => AreEqual(r.Value, rightItem.Value)))
            {
                result.Add(rightItem);
            }
        }

        return result;
    }

    // Math operators
    private IEnumerable<ITypedElement> EvaluateAddition(List<ITypedElement> left, List<ITypedElement> right)
    {
        if (left.Count != 1 || right.Count != 1)
            return Enumerable.Empty<ITypedElement>();

        var leftValue = left[0].Value;
        var rightValue = right[0].Value;

        // Try numeric addition with implicit Integer->Decimal conversion
        if (TryConvertToDecimal(leftValue, out var leftDecimal) && TryConvertToDecimal(rightValue, out var rightDecimal))
        {
            var result = leftDecimal + rightDecimal;
            // Return Integer if both were Integer, otherwise Decimal
            return leftValue is int && rightValue is int && result == Math.Floor(result)
                ? new[] { CreateInteger((int)result) }
                : new[] { CreateDecimal(result) };
        }

        return Enumerable.Empty<ITypedElement>();
    }

    private IEnumerable<ITypedElement> EvaluateSubtraction(List<ITypedElement> left, List<ITypedElement> right)
    {
        if (left.Count != 1 || right.Count != 1)
            return Enumerable.Empty<ITypedElement>();

        if (TryConvertToDecimal(left[0].Value, out var leftDecimal) && TryConvertToDecimal(right[0].Value, out var rightDecimal))
        {
            var result = leftDecimal - rightDecimal;
            return left[0].Value is int && right[0].Value is int && result == Math.Floor(result)
                ? new[] { CreateInteger((int)result) }
                : new[] { CreateDecimal(result) };
        }

        return Enumerable.Empty<ITypedElement>();
    }

    private IEnumerable<ITypedElement> EvaluateMultiplication(List<ITypedElement> left, List<ITypedElement> right)
    {
        if (left.Count != 1 || right.Count != 1)
            return Enumerable.Empty<ITypedElement>();

        if (TryConvertToDecimal(left[0].Value, out var leftDecimal) && TryConvertToDecimal(right[0].Value, out var rightDecimal))
        {
            var result = leftDecimal * rightDecimal;
            return left[0].Value is int && right[0].Value is int && result == Math.Floor(result)
                ? new[] { CreateInteger((int)result) }
                : new[] { CreateDecimal(result) };
        }

        return Enumerable.Empty<ITypedElement>();
    }

    private IEnumerable<ITypedElement> EvaluateDivision(List<ITypedElement> left, List<ITypedElement> right)
    {
        if (left.Count != 1 || right.Count != 1)
            return Enumerable.Empty<ITypedElement>();

        if (TryConvertToDecimal(left[0].Value, out var leftDecimal) && TryConvertToDecimal(right[0].Value, out var rightDecimal))
        {
            if (rightDecimal == 0)
                return Enumerable.Empty<ITypedElement>(); // Division by zero returns empty

            return new[] { CreateDecimal(leftDecimal / rightDecimal) };
        }

        return Enumerable.Empty<ITypedElement>();
    }

    private IEnumerable<ITypedElement> EvaluateIntegerDivision(List<ITypedElement> left, List<ITypedElement> right)
    {
        if (left.Count != 1 || right.Count != 1)
            return Enumerable.Empty<ITypedElement>();

        if (TryConvertToDecimal(left[0].Value, out var leftDecimal) && TryConvertToDecimal(right[0].Value, out var rightDecimal))
        {
            if (rightDecimal == 0)
                return Enumerable.Empty<ITypedElement>();

            return new[] { CreateInteger((int)Math.Truncate(leftDecimal / rightDecimal)) };
        }

        return Enumerable.Empty<ITypedElement>();
    }

    private IEnumerable<ITypedElement> EvaluateModulo(List<ITypedElement> left, List<ITypedElement> right)
    {
        if (left.Count != 1 || right.Count != 1)
            return Enumerable.Empty<ITypedElement>();

        if (TryConvertToDecimal(left[0].Value, out var leftDecimal) && TryConvertToDecimal(right[0].Value, out var rightDecimal))
        {
            if (rightDecimal == 0)
                return Enumerable.Empty<ITypedElement>();

            return new[] { CreateDecimal(leftDecimal % rightDecimal) };
        }

        return Enumerable.Empty<ITypedElement>();
    }

    // String concatenation
    private IEnumerable<ITypedElement> EvaluateStringConcatenation(List<ITypedElement> left, List<ITypedElement> right)
    {
        if (left.Count != 1 || right.Count != 1)
            return Enumerable.Empty<ITypedElement>();

        var leftStr = left[0].Value?.ToString() ?? string.Empty;
        var rightStr = right[0].Value?.ToString() ?? string.Empty;

        return new[] { new PrimitiveElement(leftStr + rightStr, "string") };
    }

    // Type operators
    private IEnumerable<ITypedElement> EvaluateTypeIs(List<ITypedElement> left, Expression typeExpr)
    {
        if (left.Count != 1)
            return Enumerable.Empty<ITypedElement>();

        // Extract type name from identifier or function call expression
        // NOTE: Parser treats bare identifiers as function calls (e.g., "integer" = "integer()")
        string? typeName = null;
        if (typeExpr is IdentifierExpression idExpr)
        {
            typeName = idExpr.Name;
        }
        else if (typeExpr is FunctionCallExpression funcExpr)
        {
            typeName = funcExpr.FunctionName;
        }

        if (typeName == null)
            return Enumerable.Empty<ITypedElement>();

        // FhirPath type names are lowercase, ToLowerInvariant is intentional
#pragma warning disable CA1308 // Normalize strings to uppercase
        typeName = typeName.ToLowerInvariant();
        var elementType = left[0].InstanceType?.ToLowerInvariant() ?? string.Empty;
#pragma warning restore CA1308 // Normalize strings to uppercase

        // Simple type checking (can be enhanced for inheritance)
        return ReturnBoolean(elementType == typeName);
    }

    private IEnumerable<ITypedElement> EvaluateTypeAs(List<ITypedElement> left, Expression typeExpr)
    {
        if (left.Count != 1)
            return Enumerable.Empty<ITypedElement>();

        // Extract type name from identifier or function call expression
        // NOTE: Parser treats bare identifiers as function calls (e.g., "integer" = "integer()")
        string? typeName = null;
        if (typeExpr is IdentifierExpression idExpr)
        {
            typeName = idExpr.Name;
        }
        else if (typeExpr is FunctionCallExpression funcExpr)
        {
            typeName = funcExpr.FunctionName;
        }

        if (typeName == null)
            return Enumerable.Empty<ITypedElement>();

        // FhirPath type names are lowercase, ToLowerInvariant is intentional
#pragma warning disable CA1308 // Normalize strings to uppercase
        typeName = typeName.ToLowerInvariant();
        var elementType = left[0].InstanceType?.ToLowerInvariant() ?? string.Empty;
#pragma warning restore CA1308 // Normalize strings to uppercase

        // Return element if type matches, empty otherwise
        return elementType == typeName ? new[] { left[0] } : Enumerable.Empty<ITypedElement>();
    }

    // Membership operators
    private bool? EvaluateMembership(List<ITypedElement> left, List<ITypedElement> right, bool isIn)
    {
        // 'in' operator: left operand must be single item
        // 'contains' operator: right operand must be single item
        var singleItem = isIn ? left : right;
        var collection = isIn ? right : left;

        if (singleItem.Count == 0)
            return null; // Empty -> empty result

        if (singleItem.Count != 1)
            return null; // More than one item -> error (return null for now, should signal error)

        if (collection.Count == 0)
            return false; // Item not in empty collection

        var itemValue = singleItem[0].Value;
        return collection.Any(c => AreEqual(c.Value, itemValue));
    }

    // Equivalence comparison
    private bool? CompareEquivalence(List<ITypedElement> left, List<ITypedElement> right, bool equivalent)
    {
        // Empty collections are equivalent
        if (left.Count == 0 && right.Count == 0)
            return equivalent;

        // Different counts are not equivalent
        if (left.Count != right.Count)
            return !equivalent;

        // For single items, compare with normalization
        if (left.Count == 1 && right.Count == 1)
        {
            var isEquiv = AreEquivalent(left[0].Value, right[0].Value);
            return isEquiv == equivalent;
        }

        // For multiple items, order-independent comparison
        var leftSorted = left.OrderBy(e => e.Value?.ToString() ?? string.Empty).ToList();
        var rightSorted = right.OrderBy(e => e.Value?.ToString() ?? string.Empty).ToList();

        for (int i = 0; i < leftSorted.Count; i++)
        {
            if (!AreEquivalent(leftSorted[i].Value, rightSorted[i].Value))
                return !equivalent;
        }

        return equivalent;
    }

    private bool AreEquivalent(object? left, object? right)
    {
        if (left == null && right == null) return true;
        if (left == null || right == null) return false;

        // String equivalence: case-insensitive, whitespace-normalized
        if (left is string leftStr && right is string rightStr)
        {
            return string.Equals(
                NormalizeWhitespace(leftStr),
                NormalizeWhitespace(rightStr),
                StringComparison.OrdinalIgnoreCase);
        }

        // Numeric equivalence with rounding to least precise
        if (left is decimal || right is decimal || left is int || right is int)
        {
            if (TryConvertToDecimal(left, out var leftDec) && TryConvertToDecimal(right, out var rightDec))
                return leftDec == rightDec;
        }

        return left.Equals(right);
    }

    private string NormalizeWhitespace(string str)
    {
        // Normalize all whitespace characters to single space
        return System.Text.RegularExpressions.Regex.Replace(str.Trim(), @"\s+", " ");
    }

    // Helper: Try convert value to decimal (handles Integer -> Decimal implicit conversion)
    private bool TryConvertToDecimal(object? value, out decimal result)
    {
        result = 0;

        if (value is decimal d)
        {
            result = d;
            return true;
        }

        if (value is int i)
        {
            result = i;
            return true;
        }

        if (value is IConvertible convertible)
        {
            try
            {
                result = Convert.ToDecimal(convertible);
                return true;
            }
            catch
            {
                return false;
            }
        }

        return false;
    }

    private IEnumerable<ITypedElement> EvaluateAxis(IEnumerable<ITypedElement> focus, AxisExpression axis, EvaluationContext context)
    {
        // FhirPath axis names are case-insensitive, ToLowerInvariant is intentional
#pragma warning disable CA1308 // Normalize strings to uppercase
        return axis.AxisName.ToLowerInvariant() switch
#pragma warning restore CA1308 // Normalize strings to uppercase
        {
            "this" => context.GetEnvironmentVariable("this") is ITypedElement thisElement
                ? new[] { thisElement }
                : focus,
            "that" => focus,
            _ => throw new NotSupportedException($"Axis '${axis.AxisName}' is not yet implemented")
        };
    }

    private IEnumerable<ITypedElement> EvaluateVariable(VariableRefExpression var, EvaluationContext context)
    {
        var value = context.GetEnvironmentVariable(var.Name);

        // Special handling for predefined variables that may not exist
        if (var.Name is "this" or "index")
        {
            // These variables are optional and may not be defined
            if (value == null)
                return Enumerable.Empty<ITypedElement>();
            if (value is ITypedElement element)
                return new[] { element };
            if (value is IEnumerable<ITypedElement> elements)
                return elements;
            return Enumerable.Empty<ITypedElement>();
        }

        // Per FHIRPath specification, accessing undefined variables returns empty collection
        // (not an error) - allows for defensive expressions like %resource.where(...)
        if (value == null)
        {
            return Enumerable.Empty<ITypedElement>();
        }

        // Handle both single element and collection returns
        if (value is ITypedElement element2)
            return new[] { element2 };
        if (value is IEnumerable<ITypedElement> elements2)
            return elements2;

        // If it's neither, return empty
        return Enumerable.Empty<ITypedElement>();
    }

    private IEnumerable<ITypedElement> EvaluateConstant(ConstantExpression constant)
    {
        return constant.Value switch
        {
            int i => new[] { CreateInteger(i) },
            decimal d => new[] { CreateDecimal(d) },
            bool b => new[] { CreateBoolean(b) },
            string s => new[] { CreateString(s) },
            _ => new[] { CreateConstant(constant.Value) }
        };
    }

    private IEnumerable<ITypedElement> EvaluateIndexer(IEnumerable<ITypedElement> focus, IndexerExpression indexer, EvaluationContext context)
    {
        var collection = EvaluateExpression(focus, indexer.Collection, context).ToList();
        var indexResults = EvaluateExpression(focus, indexer.Index, context).ToList();

        if (indexResults.Count == 1 && indexResults[0].Value is int index)
        {
            if (index >= 0 && index < collection.Count)
            {
                return new[] { collection[index] };
            }
        }

        return Enumerable.Empty<ITypedElement>();
    }

    private IEnumerable<ITypedElement> EvaluateUnary(IEnumerable<ITypedElement> focus, UnaryExpression unary, EvaluationContext context)
    {
        var operand = EvaluateExpression(focus, unary.Operand, context).ToList();

        if (unary.Operator == "-" && operand.Count == 1 && operand[0].Value is IConvertible value)
        {
            try
            {
                var numeric = Convert.ToDecimal(value);
                return new[] { CreateDecimal(-numeric) };
            }
            catch
            {
                // Ignore conversion errors
            }
        }

        return operand;
    }

    // Helper methods for type conversions and comparisons
    private bool IsTrue(IEnumerable<ITypedElement> elements)
    {
        var list = elements.ToList();
        return list.Count == 1 && list[0].Value is bool b && b;
    }

    private bool? CompareEquality(List<ITypedElement> left, List<ITypedElement> right, bool equals)
    {
        // Empty collections: return empty (null means empty result)
        if (left.Count == 0 || right.Count == 0)
            return null;

        if (left.Count != right.Count)
            return !equals;

        for (int i = 0; i < left.Count; i++)
        {
            var isEqual = AreEqual(left[i].Value, right[i].Value);
            if (isEqual != equals) return false;
        }

        return true;
    }

    private bool? CompareOrder(List<ITypedElement> left, List<ITypedElement> right, bool greater, bool orEqual)
    {
        // Empty collections: return empty (null means empty result)
        if (left.Count == 0 || right.Count == 0)
            return null;

        if (left.Count != 1 || right.Count != 1)
            return null; // Multiple items: undefined

        var leftValue = left[0].Value;
        var rightValue = right[0].Value;

        if (leftValue is IComparable leftComparable && rightValue is IComparable rightComparable)
        {
            try
            {
                var comparison = leftComparable.CompareTo(rightComparable);
                return greater
                    ? (orEqual ? comparison >= 0 : comparison > 0)
                    : (orEqual ? comparison <= 0 : comparison < 0);
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    private bool AreEqual(object? left, object? right)
    {
        if (left == null && right == null) return true;
        if (left == null || right == null) return false;
        return left.Equals(right);
    }

    // Factory methods for creating primitive ITypedElement instances
    private ITypedElement CreateBoolean(bool value) => new PrimitiveElement(value, "boolean");
    private ITypedElement CreateInteger(int value) => new PrimitiveElement(value, "integer");
    private ITypedElement CreateDecimal(decimal value) => new PrimitiveElement(value, "decimal");
    private ITypedElement CreateString(string value) => new PrimitiveElement(value, "string");
    private ITypedElement CreateConstant(object value) => new PrimitiveElement(value, GetFhirPathTypeName(value));

    /// <summary>
    /// Converts a .NET primitive value to its FHIRPath type name.
    /// Centralized logic for type name conversion.
    /// </summary>
    internal static string GetFhirPathTypeName(object value)
    {
        return value switch
        {
            string => "string",
            int or long => "integer",
            decimal => "decimal",
            bool => "boolean",
            DateTime or DateTimeOffset => "dateTime",
            _ => "string" // Default fallback
        };
    }

    /// <summary>
    /// Simple implementation of ITypedElement for primitive values.
    /// </summary>
    private class PrimitiveElement : ITypedElement
    {
        public PrimitiveElement(object value, string type)
        {
            Value = value;
            InstanceType = type;
        }

        public string Name => string.Empty;
        public string InstanceType { get; }
        public object Value { get; }
        public string Location => string.Empty;
        public IElementDefinitionSummary? Definition => null;

        public IEnumerable<ITypedElement> Children(string? name = null) => Enumerable.Empty<ITypedElement>();
    }
}
