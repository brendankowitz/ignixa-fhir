/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * FhirPath collection function implementations.
 * Implements exists(), empty(), count(), distinct(), isDistinct(),
 * first(), last(), single(), tail(), skip(), take(),
 * where(), select(), all(), any(), repeat(), ofType(), as(),
 * intersect(), exclude(), union(), combine(), subsetOf(), supersetOf().
 */

using Ignixa.Abstractions;
using Ignixa.FhirPath.Expressions;

namespace Ignixa.FhirPath.Evaluation.Functions;

/// <summary>
/// Collection function implementations for FhirPath expressions.
/// </summary>
internal static class CollectionFunctions
{
    /// <summary>
    /// exists() - Returns true if collection is not empty, or if any element matches criteria.
    /// </summary>
    public static IEnumerable<IElement> Exists(
        IEnumerable<IElement> focus,
        IReadOnlyList<Expression> arguments,
        EvaluationContext context,
        Func<IEnumerable<IElement>, Expression, EvaluationContext, IEnumerable<IElement>> evaluateExpression)
    {
        var hasCriteria = arguments.Count > 0;
        bool exists;

        if (hasCriteria)
        {
            // exists(criteria): returns true if any element matches the criteria
            exists = focus.Any(element =>
            {
                var result = evaluateExpression([element], arguments[0], context);
                return result.Any() && FunctionHelpers.IsTrue(result);
            });
        }
        else
        {
            // exists(): returns true if collection is not empty
            exists = focus.Any();
        }

        // Per SQL on FHIR: boolean functions must return [true] or [false], never empty
        // This ensures columns with type: "boolean" get true/false/null, not true/null
        return [(IElement)FunctionHelpers.CreateBoolean(exists)];
    }

    /// <summary>
    /// empty() - Returns true if collection is empty.
    /// </summary>
    public static IEnumerable<IElement> Empty(IEnumerable<IElement> focus)
    {
        var isEmpty = !focus.Any();
        // Per SQL on FHIR: boolean functions must return [true] or [false], never empty
        return [(IElement)FunctionHelpers.CreateBoolean(isEmpty)];
    }

    /// <summary>
    /// count() - Returns the number of elements in the collection.
    /// </summary>
    public static IEnumerable<IElement> Count(IEnumerable<IElement> focus)
    {
        var count = focus.Count();
        return [(IElement)FunctionHelpers.CreateInteger(count)];
    }

    /// <summary>
    /// isDistinct() - Returns true if all elements in the collection are distinct.
    /// </summary>
    public static IEnumerable<IElement> IsDistinct(IEnumerable<IElement> focus)
    {
        var list = focus.ToList();
        var distinctCount = list.Select(e => e.Value).Distinct(new FunctionHelpers.ObjectEqualityComparer()).Count();
        var isDistinct = distinctCount == list.Count;
        // Per SQL on FHIR: boolean functions must return [true] or [false], never empty
        return [(IElement)FunctionHelpers.CreateBoolean(isDistinct)];
    }

    /// <summary>
    /// first() - Returns the first element in the collection, or empty if collection is empty.
    /// </summary>
    public static IEnumerable<IElement> First(IEnumerable<IElement> focus)
    {
        var first = focus.FirstOrDefault();
        return first != null ? [first] : [];
    }

    /// <summary>
    /// last() - Returns the last element in the collection, or empty if collection is empty.
    /// </summary>
    public static IEnumerable<IElement> Last(IEnumerable<IElement> focus)
    {
        var last = focus.LastOrDefault();
        return last != null ? [last] : [];
    }

    /// <summary>
    /// single() - Returns the single element in the collection, throws if collection has more than one element.
    /// </summary>
    public static IEnumerable<IElement> Single(IEnumerable<IElement> focus)
    {
        var list = focus.ToList();
        if (list.Count == 0)
            return [];

        if (list.Count > 1)
            throw new InvalidOperationException("single() called on collection with multiple items");

        return [list[0]];
    }

    /// <summary>
    /// tail() - Returns all elements except the first.
    /// </summary>
    public static IEnumerable<IElement> Tail(IEnumerable<IElement> focus)
    {
        return focus.Skip(1);
    }

    /// <summary>
    /// skip() - Skips the first n elements in the collection.
    /// </summary>
    public static IEnumerable<IElement> Skip(
        IEnumerable<IElement> focus,
        IReadOnlyList<Expression> arguments,
        EvaluationContext context,
        Func<IEnumerable<IElement>, Expression, EvaluationContext, IEnumerable<IElement>> evaluateExpression)
    {
        if (arguments.Count == 0)
            throw new ArgumentException("skip() requires a num argument");

        var numResult = evaluateExpression(focus, arguments[0], context).SingleOrDefault();
        if (numResult?.Value is not int num)
            return [];

        return num <= 0 ? focus : focus.Skip(num);
    }

    /// <summary>
    /// take() - Takes the first n elements in the collection.
    /// </summary>
    public static IEnumerable<IElement> Take(
        IEnumerable<IElement> focus,
        IReadOnlyList<Expression> arguments,
        EvaluationContext context,
        Func<IEnumerable<IElement>, Expression, EvaluationContext, IEnumerable<IElement>> evaluateExpression)
    {
        if (arguments.Count == 0)
            throw new ArgumentException("take() requires a num argument");

        var numResult = evaluateExpression(focus, arguments[0], context).SingleOrDefault();
        if (numResult?.Value is not int num)
            return [];

        return num <= 0 ? [] : focus.Take(num);
    }

    /// <summary>
    /// where() - Filters elements based on a criteria expression.
    /// </summary>
    public static IEnumerable<IElement> Where(
        IEnumerable<IElement> focus,
        IReadOnlyList<Expression> arguments,
        EvaluationContext context,
        Func<IEnumerable<IElement>, Expression, EvaluationContext, IEnumerable<IElement>> evaluateExpression)
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
                var result = evaluateExpression([element], criteria, context);
                if (result.Any() && FunctionHelpers.IsTrue(result))
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

    /// <summary>
    /// select() - Projects elements based on a projection expression.
    /// </summary>
    public static IEnumerable<IElement> Select(
        IEnumerable<IElement> focus,
        IReadOnlyList<Expression> arguments,
        EvaluationContext context,
        Func<IEnumerable<IElement>, Expression, EvaluationContext, IEnumerable<IElement>> evaluateExpression)
    {
        if (arguments.Count == 0)
            throw new ArgumentException("select() requires a projection argument");

        var projection = arguments[0];

        foreach (var element in focus)
        {
            foreach (var result in evaluateExpression([element], projection, context))
            {
                yield return result;
            }
        }
    }

    /// <summary>
    /// all() - Returns true if all elements match the criteria.
    /// </summary>
    public static IEnumerable<IElement> All(
        IEnumerable<IElement> focus,
        IReadOnlyList<Expression> arguments,
        EvaluationContext context,
        Func<IEnumerable<IElement>, Expression, EvaluationContext, IEnumerable<IElement>> evaluateExpression)
    {
        if (arguments.Count == 0)
            throw new ArgumentException("all() requires a criteria argument");

        var criteria = arguments[0];
        var allMatch = focus.All(element =>
        {
            var result = evaluateExpression([element], criteria, context);
            return result.Any() && FunctionHelpers.IsTrue(result);
        });

        // Per SQL on FHIR: boolean functions must return [true] or [false], never empty
        return [(IElement)FunctionHelpers.CreateBoolean(allMatch)];
    }

    /// <summary>
    /// any() - Returns true if any element matches the criteria, or if collection is not empty (no criteria).
    /// </summary>
    public static IEnumerable<IElement> Any(
        IEnumerable<IElement> focus,
        IReadOnlyList<Expression> arguments,
        EvaluationContext context,
        Func<IEnumerable<IElement>, Expression, EvaluationContext, IEnumerable<IElement>> evaluateExpression)
    {
        if (arguments.Count == 0)
        {
            // any() without criteria: returns true if collection is not empty
            // Per SQL on FHIR: boolean functions must return [true] or [false], never empty
            return [(IElement)FunctionHelpers.CreateBoolean(focus.Any())];
        }

        var criteria = arguments[0];
        var anyMatch = focus.Any(element =>
        {
            var result = evaluateExpression([element], criteria, context);
            return result.Any() && FunctionHelpers.IsTrue(result);
        });

        // Per SQL on FHIR: boolean functions must return [true] or [false], never empty
        return [(IElement)FunctionHelpers.CreateBoolean(anyMatch)];
    }

    /// <summary>
    /// repeat() - Recursively applies a projection expression until no new elements are found.
    /// </summary>
    public static IEnumerable<IElement> Repeat(
        IEnumerable<IElement> focus,
        IReadOnlyList<Expression> arguments,
        EvaluationContext context,
        Func<IEnumerable<IElement>, Expression, EvaluationContext, IEnumerable<IElement>> evaluateExpression)
    {
        if (arguments.Count == 0)
            throw new ArgumentException("repeat() requires a projection argument");

        var projection = arguments[0];
        var result = new HashSet<IElement>(new FunctionHelpers.ElementEqualityComparer());
        var queue = new Queue<IElement>(focus);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (result.Add(current))
            {
                var projected = evaluateExpression([current], projection, context);
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

    /// <summary>
    /// ofType() - Filters elements by instance type.
    /// </summary>
    public static IEnumerable<IElement> OfType(
        IEnumerable<IElement> focus,
        IReadOnlyList<Expression> arguments,
        EvaluationContext context,
        Func<IEnumerable<IElement>, Expression, EvaluationContext, IEnumerable<IElement>> evaluateExpression)
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
            var result = evaluateExpression(focus, arguments[0], context).ToList();
            if (result.Count > 0)
            {
                typeName = result[0].Value?.ToString();
            }
        }

        if (string.IsNullOrEmpty(typeName))
            return [];

        // Case-insensitive type name comparison
        // Filter elements where InstanceType matches the requested type name
        return focus.Where(e => !string.IsNullOrEmpty(e.InstanceType) &&
                               e.InstanceType.Equals(typeName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// as() - Type coercion operator (filters by type).
    /// </summary>
    public static IEnumerable<IElement> As(
        IEnumerable<IElement> focus,
        IReadOnlyList<Expression> arguments)
    {
        // as() is functionally identical to ofType() - it casts/filters by type
        // as(type) returns the input if it is of the specified type, otherwise returns empty
        if (arguments.Count == 0)
            throw new ArgumentException("as() requires a type argument");

        // Extract type name from identifier expression
        if (arguments[0] is not IdentifierExpression idExpr)
            return [];

        // FhirPath type names are case-insensitive
#pragma warning disable CA1308 // Normalize strings to uppercase
        var typeName = idExpr.Name.ToLowerInvariant();
        return focus.Where(e => e.InstanceType?.ToLowerInvariant() == typeName);
#pragma warning restore CA1308 // Normalize strings to uppercase
    }

    /// <summary>
    /// intersect() - Returns elements that appear in both collections.
    /// </summary>
    public static IEnumerable<IElement> Intersect(
        IEnumerable<IElement> focus,
        IReadOnlyList<Expression> arguments,
        EvaluationContext context,
        Func<IEnumerable<IElement>, Expression, EvaluationContext, IEnumerable<IElement>> evaluateExpression)
    {
        if (arguments.Count == 0)
            throw new ArgumentException("intersect() requires an other argument");

        var other = evaluateExpression(focus, arguments[0], context).ToList();
        var result = new List<IElement>();

        foreach (var item in focus)
        {
            if (other.Any(o => FunctionHelpers.AreEqual(o.Value, item.Value)) && !result.Any(r => FunctionHelpers.AreEqual(r.Value, item.Value)))
            {
                result.Add(item);
            }
        }

        return result;
    }

    /// <summary>
    /// exclude() - Returns elements from focus that do not appear in other collection.
    /// </summary>
    public static IEnumerable<IElement> Exclude(
        IEnumerable<IElement> focus,
        IReadOnlyList<Expression> arguments,
        EvaluationContext context,
        Func<IEnumerable<IElement>, Expression, EvaluationContext, IEnumerable<IElement>> evaluateExpression)
    {
        if (arguments.Count == 0)
            throw new ArgumentException("exclude() requires an other argument");

        var other = evaluateExpression(focus, arguments[0], context).ToList();
        var result = new List<IElement>();

        foreach (var item in focus)
        {
            if (!other.Any(o => FunctionHelpers.AreEqual(o.Value, item.Value)))
            {
                result.Add(item);
            }
        }

        return result;
    }

    /// <summary>
    /// union() - Combines two collections, eliminating duplicates.
    /// </summary>
    public static IEnumerable<IElement> Union(
        IEnumerable<IElement> focus,
        IReadOnlyList<Expression> arguments,
        EvaluationContext context,
        Func<IEnumerable<IElement>, Expression, EvaluationContext, IEnumerable<IElement>> evaluateExpression)
    {
        if (arguments.Count == 0)
            throw new ArgumentException("union() requires an other argument");

        var other = evaluateExpression(focus, arguments[0], context).ToList();
        return FunctionHelpers.EvaluateUnion(focus.ToList(), other);
    }

    /// <summary>
    /// combine() - Combines two collections without eliminating duplicates.
    /// </summary>
    public static IEnumerable<IElement> Combine(
        IEnumerable<IElement> focus,
        IReadOnlyList<Expression> arguments,
        EvaluationContext context,
        Func<IEnumerable<IElement>, Expression, EvaluationContext, IEnumerable<IElement>> evaluateExpression)
    {
        if (arguments.Count == 0)
            throw new ArgumentException("combine() requires an other argument");

        var other = evaluateExpression(focus, arguments[0], context);
        return focus.Concat(other); // Combine does NOT eliminate duplicates
    }

    /// <summary>
    /// subsetOf() - Returns true if focus collection is a subset of other collection.
    /// </summary>
    public static IEnumerable<IElement> SubsetOf(
        IEnumerable<IElement> focus,
        IReadOnlyList<Expression> arguments,
        EvaluationContext context,
        Func<IEnumerable<IElement>, Expression, EvaluationContext, IEnumerable<IElement>> evaluateExpression)
    {
        if (arguments.Count == 0)
            throw new ArgumentException("subsetOf() requires an other argument");

        var focusList = focus.ToList();
        var other = evaluateExpression(focus, arguments[0], context).ToList();

        // Empty collection is subset of any collection
        if (focusList.Count == 0)
            return [(IElement)FunctionHelpers.CreateBoolean(true)];

        // Check if all focus items are in other
        var isSubset = focusList.All(f => other.Any(o => FunctionHelpers.AreEqual(o.Value, f.Value)));
        // Per SQL on FHIR: boolean functions must return [true] or [false], never empty
        return [(IElement)FunctionHelpers.CreateBoolean(isSubset)];
    }

    /// <summary>
    /// supersetOf() - Returns true if focus collection is a superset of other collection.
    /// </summary>
    public static IEnumerable<IElement> SupersetOf(
        IEnumerable<IElement> focus,
        IReadOnlyList<Expression> arguments,
        EvaluationContext context,
        Func<IEnumerable<IElement>, Expression, EvaluationContext, IEnumerable<IElement>> evaluateExpression)
    {
        if (arguments.Count == 0)
            throw new ArgumentException("supersetOf() requires an other argument");

        var focusList = focus.ToList();
        var other = evaluateExpression(focus, arguments[0], context).ToList();

        // Any collection is superset of empty collection
        if (other.Count == 0)
            return [(IElement)FunctionHelpers.CreateBoolean(true)];

        // Check if all other items are in focus
        var isSuperset = other.All(o => focusList.Any(f => FunctionHelpers.AreEqual(f.Value, o.Value)));
        // Per SQL on FHIR: boolean functions must return [true] or [false], never empty
        return [(IElement)FunctionHelpers.CreateBoolean(isSuperset)];
    }
}
