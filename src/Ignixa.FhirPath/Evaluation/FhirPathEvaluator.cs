/*
 * Copyright (c) 2025, Sparky Contributors
 *
 * FhirPath expression evaluator.
 * Executes parsed FhirPath AST against ITypedElement trees.
 */

using Ignixa.FhirPath.Expressions;
using Ignixa.Serialization.Abstractions;

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
            "oftype" => EvaluateOfType(focusElements, func.Arguments),

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

            // Type checking functions
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

            // Tree navigation functions
            "children" => EvaluateChildren(focusElements),
            "descendants" => EvaluateDescendants(focusElements),

            // FHIR-specific functions
            "extension" => EvaluateExtension(focusElements, func.Arguments, context),

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

        return exists ? new[] { CreateBoolean(true) } : Enumerable.Empty<ITypedElement>();
    }

    private IEnumerable<ITypedElement> EvaluateEmpty(IEnumerable<ITypedElement> focus)
    {
        var isEmpty = !focus.Any();
        return isEmpty ? new[] { CreateBoolean(true) } : Enumerable.Empty<ITypedElement>();
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

        return allMatch ? new[] { CreateBoolean(true) } : Enumerable.Empty<ITypedElement>();
    }

    private IEnumerable<ITypedElement> EvaluateAny(IEnumerable<ITypedElement> focus, IReadOnlyList<Expression> arguments, EvaluationContext context)
    {
        if (arguments.Count == 0)
        {
            // any() without criteria: returns true if collection is not empty
            return focus.Any() ? new[] { CreateBoolean(true) } : Enumerable.Empty<ITypedElement>();
        }

        var criteria = arguments[0];
        var anyMatch = focus.Any(element =>
        {
            var result = EvaluateExpression(new[] { element }, criteria, context);
            return result.Any() && IsTrue(result);
        });

        return anyMatch ? new[] { CreateBoolean(true) } : Enumerable.Empty<ITypedElement>();
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
        return allTrue ? new[] { CreateBoolean(true) } : Enumerable.Empty<ITypedElement>();
    }

    private IEnumerable<ITypedElement> EvaluateAnyTrue(IEnumerable<ITypedElement> focus)
    {
        var list = focus.ToList();
        if (list.Count == 0)
            return Enumerable.Empty<ITypedElement>(); // Empty collection returns false

        var anyTrue = list.Any(e => e.Value is bool b && b);
        return anyTrue ? new[] { CreateBoolean(true) } : Enumerable.Empty<ITypedElement>();
    }

    private IEnumerable<ITypedElement> EvaluateAllFalse(IEnumerable<ITypedElement> focus)
    {
        var list = focus.ToList();
        if (list.Count == 0)
            return new[] { CreateBoolean(true) }; // Empty collection returns true

        var allFalse = list.All(e => e.Value is bool b && !b);
        return allFalse ? new[] { CreateBoolean(true) } : Enumerable.Empty<ITypedElement>();
    }

    private IEnumerable<ITypedElement> EvaluateAnyFalse(IEnumerable<ITypedElement> focus)
    {
        var list = focus.ToList();
        if (list.Count == 0)
            return Enumerable.Empty<ITypedElement>(); // Empty collection returns false

        var anyFalse = list.Any(e => e.Value is bool b && !b);
        return anyFalse ? new[] { CreateBoolean(true) } : Enumerable.Empty<ITypedElement>();
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
        return isSubset ? new[] { CreateBoolean(true) } : Enumerable.Empty<ITypedElement>();
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
        return isSuperset ? new[] { CreateBoolean(true) } : Enumerable.Empty<ITypedElement>();
    }

    private IEnumerable<ITypedElement> EvaluateIsDistinct(IEnumerable<ITypedElement> focus)
    {
        var list = focus.ToList();
        var distinctCount = list.Select(e => e.Value).Distinct(new ObjectEqualityComparer()).Count();
        var isDistinct = distinctCount == list.Count;
        return isDistinct ? new[] { CreateBoolean(true) } : Enumerable.Empty<ITypedElement>();
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

    private IEnumerable<ITypedElement> EvaluateOfType(IEnumerable<ITypedElement> focus, IReadOnlyList<Expression> arguments)
    {
        if (arguments.Count == 0)
            throw new ArgumentException("ofType() requires a type argument");

        // Extract type name from identifier expression
        if (arguments[0] is not IdentifierExpression idExpr)
            return Enumerable.Empty<ITypedElement>();

        // FhirPath type names are lowercase, ToLowerInvariant is intentional
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

    // Type checking functions
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
        return result.HasValue
            ? (result.Value ? new[] { CreateBoolean(true) } : Enumerable.Empty<ITypedElement>())
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
        return value is ITypedElement element ? new[] { element } : Enumerable.Empty<ITypedElement>();
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
    // FHIR type names are lowercase in FhirPath, ToLowerInvariant is intentional
#pragma warning disable CA1308 // Normalize strings to uppercase
    private ITypedElement CreateConstant(object value) => new PrimitiveElement(value, value.GetType().Name.ToLowerInvariant());
#pragma warning restore CA1308 // Normalize strings to uppercase

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
