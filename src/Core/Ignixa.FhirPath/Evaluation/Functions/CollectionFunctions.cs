/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * FhirPath collection function implementations.
 * Implements exists(), empty(), count(), distinct(), isDistinct(),
 * first(), last(), single(), tail(), skip(), take(),
 * where(), select(), all(), any(), repeat(), ofType(), as(),
 * intersect(), exclude(), union(), combine(), subsetOf(), supersetOf().
 *
 * Uses immutable EvaluationContext pattern - no save/restore needed for $this binding.
 */

using Ignixa.Abstractions;
using Ignixa.FhirPath.Attributes;
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
    [FhirPathFunction("exists",
        SupportedContexts = "any-boolean",
        ReturnType = "boolean",
        SupportsCollections = true,
        SupportedAtRoot = true,
        MinArguments = 0,
        MaxArguments = 1,
        TakesExpressionArguments = true,
        Category = "Collection",
        Description = "Returns true if collection is not empty, or if any element matches criteria")]
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
            exists = focus.Any(element =>
            {
                var innerContext = context.PushThis(element);
                var result = evaluateExpression([element], arguments[0], innerContext);
                return result.Any() && FunctionHelpers.IsTrue(result);
            });
        }
        else
        {
            exists = focus.Any();
        }

        return [(IElement)FunctionHelpers.CreateBoolean(exists)];
    }

    /// <summary>
    /// empty() - Returns true if collection is empty.
    /// </summary>
    [FhirPathFunction("empty",
        SupportedContexts = "any-boolean",
        ReturnType = "boolean",
        SupportsCollections = true,
        SupportedAtRoot = true,
        MinArguments = 0,
        MaxArguments = 0,
        Category = "Collection",
        Description = "Returns true if collection is empty")]
    public static IEnumerable<IElement> Empty(IEnumerable<IElement> focus)
    {
        var isEmpty = !focus.Any();
        return [(IElement)FunctionHelpers.CreateBoolean(isEmpty)];
    }

    /// <summary>
    /// count() - Returns the number of elements in the collection.
    /// </summary>
    [FhirPathFunction("count",
        SupportedContexts = "any-integer",
        ReturnType = "integer",
        SupportsCollections = true,
        SupportedAtRoot = true,
        MinArguments = 0,
        MaxArguments = 0,
        Category = "Collection",
        Description = "Returns the number of elements in the collection")]
    public static IEnumerable<IElement> Count(IEnumerable<IElement> focus)
    {
        var count = focus.Count();
        return [(IElement)FunctionHelpers.CreateInteger(count)];
    }

    /// <summary>
    /// distinct() - Returns a collection containing only the distinct elements from the input.
    /// Uses value-based equality comparison.
    /// </summary>
    [FhirPathFunction("distinct",
        SupportedContexts = "any-any",
        ReturnType = "context",
        SupportsCollections = true,
        SupportedAtRoot = true,
        MinArguments = 0,
        MaxArguments = 0,
        Category = "Collection",
        Description = "Returns a collection containing only the distinct elements")]
    public static IEnumerable<IElement> Distinct(IEnumerable<IElement> focus)
    {
        return focus.Distinct(new FunctionHelpers.ElementEqualityComparer());
    }

    /// <summary>
    /// isDistinct() - Returns true if all elements in the collection are distinct.
    /// </summary>
    [FhirPathFunction("isDistinct",
        SupportedContexts = "any-boolean",
        ReturnType = "boolean",
        SupportsCollections = true,
        SupportedAtRoot = true,
        MinArguments = 0,
        MaxArguments = 0,
        Category = "Collection",
        Description = "Returns true if all elements in the collection are distinct")]
    public static IEnumerable<IElement> IsDistinct(IEnumerable<IElement> focus)
    {
        var list = focus.ToList();
        var distinctCount = list.Select(e => e.Value).Distinct(new FunctionHelpers.ObjectEqualityComparer()).Count();
        var isDistinct = distinctCount == list.Count;
        return [(IElement)FunctionHelpers.CreateBoolean(isDistinct)];
    }

    /// <summary>
    /// first() - Returns the first element in the collection, or empty if collection is empty.
    /// </summary>
    [FhirPathFunction("first",
        SupportedContexts = "any-any",
        ReturnType = "context",
        SupportsCollections = true,
        MinArguments = 0,
        MaxArguments = 0,
        Category = "Collection",
        Description = "Returns the first element in the collection")]
    public static IEnumerable<IElement> First(IEnumerable<IElement> focus)
    {
        var first = focus.FirstOrDefault();
        return first != null ? [first] : [];
    }

    /// <summary>
    /// last() - Returns the last element in the collection, or empty if collection is empty.
    /// </summary>
    [FhirPathFunction("last",
        SupportedContexts = "any-any",
        ReturnType = "context",
        SupportsCollections = true,
        MinArguments = 0,
        MaxArguments = 0,
        Category = "Collection",
        Description = "Returns the last element in the collection")]
    public static IEnumerable<IElement> Last(IEnumerable<IElement> focus)
    {
        var last = focus.LastOrDefault();
        return last != null ? [last] : [];
    }

    /// <summary>
    /// single() - Returns the single element in the collection, throws if collection has more than one element.
    /// </summary>
    [FhirPathFunction("single",
        SupportedContexts = "any-any",
        ReturnType = "context",
        SupportsCollections = true,
        MinArguments = 0,
        MaxArguments = 0,
        Category = "Collection",
        Description = "Returns the single element in the collection")]
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
    [FhirPathFunction("tail",
        SupportedContexts = "any-any",
        ReturnType = "context",
        SupportsCollections = true,
        MinArguments = 0,
        MaxArguments = 0,
        Category = "Collection",
        Description = "Returns all elements except the first")]
    public static IEnumerable<IElement> Tail(IEnumerable<IElement> focus)
    {
        return focus.Skip(1);
    }

    /// <summary>
    /// skip() - Skips the first n elements in the collection.
    /// </summary>
    [FhirPathFunction("skip",
        SupportedContexts = "any-any",
        ReturnType = "context",
        SupportsCollections = true,
        MinArguments = 1,
        MaxArguments = 1,
        Category = "Collection",
        Description = "Skips the first n elements in the collection")]
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
    [FhirPathFunction("take",
        SupportedContexts = "any-any",
        ReturnType = "context",
        SupportsCollections = true,
        MinArguments = 1,
        MaxArguments = 1,
        Category = "Collection",
        Description = "Takes the first n elements in the collection")]
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
    /// Uses immutable context pattern - creates new context with $this binding for each element.
    /// </summary>
    [FhirPathFunction("where",
        SupportedContexts = "any-any",
        ReturnType = "context",
        SupportsCollections = true,
        MinArguments = 1,
        MaxArguments = 1,
        TakesExpressionArguments = true,
        Category = "Collection",
        Description = "Filters elements based on a criteria expression")]
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
            var innerContext = context.PushThis(element);
            var result = evaluateExpression([element], criteria, innerContext);
            if (result.Any() && FunctionHelpers.IsTrue(result))
            {
                yield return element;
            }
        }
    }

    /// <summary>
    /// select() - Projects elements based on a projection expression.
    /// </summary>
    [FhirPathFunction("select",
        SupportedContexts = "any-any",
        ReturnType = "fromArgument",
        SupportsCollections = true,
        MinArguments = 1,
        MaxArguments = 1,
        TakesExpressionArguments = true,
        Category = "Collection",
        Description = "Projects elements based on a projection expression")]
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
            var innerContext = context.PushThis(element);
            foreach (var result in evaluateExpression([element], projection, innerContext))
            {
                yield return result;
            }
        }
    }

    /// <summary>
    /// all() - Returns true if all elements match the criteria.
    /// </summary>
    [FhirPathFunction("all",
        SupportedContexts = "any-boolean",
        ReturnType = "boolean",
        SupportsCollections = true,
        MinArguments = 1,
        MaxArguments = 1,
        TakesExpressionArguments = true,
        Category = "Collection",
        Description = "Returns true if all elements match the criteria")]
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
            var innerContext = context.PushThis(element);
            var result = evaluateExpression([element], criteria, innerContext);
            return result.Any() && FunctionHelpers.IsTrue(result);
        });

        return [(IElement)FunctionHelpers.CreateBoolean(allMatch)];
    }

    /// <summary>
    /// any() - Returns true if any element matches the criteria, or if collection is not empty (no criteria).
    /// </summary>
    [FhirPathFunction("any",
        SupportedContexts = "any-boolean",
        ReturnType = "boolean",
        SupportsCollections = true,
        MinArguments = 0,
        MaxArguments = 1,
        Category = "Collection",
        Description = "Returns true if any element matches the criteria")]
    public static IEnumerable<IElement> Any(
        IEnumerable<IElement> focus,
        IReadOnlyList<Expression> arguments,
        EvaluationContext context,
        Func<IEnumerable<IElement>, Expression, EvaluationContext, IEnumerable<IElement>> evaluateExpression)
    {
        if (arguments.Count == 0)
        {
            return [(IElement)FunctionHelpers.CreateBoolean(focus.Any())];
        }

        var criteria = arguments[0];
        var anyMatch = focus.Any(element =>
        {
            var innerContext = context.PushThis(element);
            var result = evaluateExpression([element], criteria, innerContext);
            return result.Any() && FunctionHelpers.IsTrue(result);
        });

        return [(IElement)FunctionHelpers.CreateBoolean(anyMatch)];
    }

    /// <summary>
    /// repeat() - Recursively applies a projection expression until no new elements are found.
    /// </summary>
    [FhirPathFunction("repeat",
        SupportedContexts = "any-any",
        ReturnType = "context",
        SupportsCollections = true,
        MinArguments = 1,
        MaxArguments = 1,
        TakesExpressionArguments = true,
        Category = "Collection",
        Description = "Recursively applies a projection expression until no new elements are found")]
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
                var innerContext = context.PushThis(current);
                var projected = evaluateExpression([current], projection, innerContext);
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
    [FhirPathFunction("ofType",
        SupportedContexts = "any-any",
        ReturnType = "context",
        SupportsCollections = true,
        MinArguments = 1,
        MaxArguments = 1,
        Category = "Collection",
        Description = "Filters elements by instance type")]
    public static IEnumerable<IElement> OfType(
        IEnumerable<IElement> focus,
        IReadOnlyList<Expression> arguments,
        EvaluationContext context,
        Func<IEnumerable<IElement>, Expression, EvaluationContext, IEnumerable<IElement>> evaluateExpression)
    {
        if (arguments.Count == 0)
            throw new ArgumentException("ofType() requires a type argument");

        string? typeName = null;

        if (arguments[0] is IdentifierExpression idExpr)
        {
            typeName = idExpr.Name;
        }
        else
        {
            var result = evaluateExpression(focus, arguments[0], context).ToList();
            if (result.Count > 0)
            {
                typeName = result[0].Value?.ToString();
            }
        }

        if (string.IsNullOrEmpty(typeName))
            return [];

        return focus.Where(e => !string.IsNullOrEmpty(e.InstanceType) &&
                               e.InstanceType.Equals(typeName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// as() - Type coercion operator (filters by type).
    /// </summary>
    [FhirPathFunction("as",
        SupportedContexts = "any-any",
        ReturnType = "context",
        SupportsCollections = true,
        MinArguments = 1,
        MaxArguments = 1,
        Category = "Collection",
        Description = "Type coercion operator (filters by type)")]
    public static IEnumerable<IElement> As(
        IEnumerable<IElement> focus,
        IReadOnlyList<Expression> arguments)
    {
        if (arguments.Count == 0)
            throw new ArgumentException("as() requires a type argument");

        if (arguments[0] is not IdentifierExpression idExpr)
            return [];

#pragma warning disable CA1308 // Normalize strings to uppercase
        var typeName = idExpr.Name.ToLowerInvariant();
        return focus.Where(e => e.InstanceType?.ToLowerInvariant() == typeName);
#pragma warning restore CA1308 // Normalize strings to uppercase
    }

    /// <summary>
    /// intersect() - Returns elements that appear in both collections.
    /// </summary>
    [FhirPathFunction("intersect",
        SupportedContexts = "any-any",
        ReturnType = "context",
        SupportsCollections = true,
        MinArguments = 1,
        MaxArguments = 1,
        Category = "Collection",
        Description = "Returns elements that appear in both collections")]
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
    [FhirPathFunction("exclude",
        SupportedContexts = "any-any",
        ReturnType = "context",
        SupportsCollections = true,
        MinArguments = 1,
        MaxArguments = 1,
        Category = "Collection",
        Description = "Returns elements from focus that do not appear in other collection")]
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
    [FhirPathFunction("union",
        SupportedContexts = "any-any",
        ReturnType = "context",
        SupportsCollections = true,
        MinArguments = 1,
        MaxArguments = 1,
        Category = "Collection",
        Description = "Combines two collections, eliminating duplicates")]
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
    [FhirPathFunction("combine",
        SupportedContexts = "any-any",
        ReturnType = "context",
        SupportsCollections = true,
        MinArguments = 1,
        MaxArguments = 1,
        Category = "Collection",
        Description = "Combines two collections without eliminating duplicates")]
    public static IEnumerable<IElement> Combine(
        IEnumerable<IElement> focus,
        IReadOnlyList<Expression> arguments,
        EvaluationContext context,
        Func<IEnumerable<IElement>, Expression, EvaluationContext, IEnumerable<IElement>> evaluateExpression)
    {
        if (arguments.Count == 0)
            throw new ArgumentException("combine() requires an other argument");

        var other = evaluateExpression(focus, arguments[0], context);
        return focus.Concat(other);
    }

    /// <summary>
    /// subsetOf() - Returns true if focus collection is a subset of other collection.
    /// </summary>
    [FhirPathFunction("subsetOf",
        SupportedContexts = "any-boolean",
        ReturnType = "boolean",
        SupportsCollections = true,
        MinArguments = 1,
        MaxArguments = 1,
        Category = "Collection",
        Description = "Returns true if focus collection is a subset of other collection")]
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

        if (focusList.Count == 0)
            return [(IElement)FunctionHelpers.CreateBoolean(true)];

        var isSubset = focusList.All(f => other.Any(o => FunctionHelpers.AreEqual(o.Value, f.Value)));
        return [(IElement)FunctionHelpers.CreateBoolean(isSubset)];
    }

    /// <summary>
    /// supersetOf() - Returns true if focus collection is a superset of other collection.
    /// </summary>
    [FhirPathFunction("supersetOf",
        SupportedContexts = "any-boolean",
        ReturnType = "boolean",
        SupportsCollections = true,
        MinArguments = 1,
        MaxArguments = 1,
        Category = "Collection",
        Description = "Returns true if focus collection is a superset of other collection")]
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

        if (other.Count == 0)
            return [(IElement)FunctionHelpers.CreateBoolean(true)];

        var isSuperset = other.All(o => focusList.Any(f => FunctionHelpers.AreEqual(f.Value, o.Value)));
        return [(IElement)FunctionHelpers.CreateBoolean(isSuperset)];
    }
}
