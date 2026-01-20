/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * FhirPath collection function implementations.
 * Implements exists(), empty(), count(), distinct(), isDistinct(),
 * first(), last(), single(), tail(), skip(), take(),
 * where(), select(), all(), any(), repeat(), repeatAll(), coalesce(), ofType(), as(),
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
            var index = 0;
            exists = focus.Any(element =>
            {
                var innerContext = context.PushThis(element).PushIndex(index++);
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
        var index = 0;

        foreach (var element in focus)
        {
            var innerContext = context.PushThis(element).PushIndex(index++);
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
        var focusList = focus.ToList();

        for (int i = 0; i < focusList.Count; i++)
        {
            var element = focusList[i];
            var innerContext = context
                .PushThis(element)
                .PushIndex(i);
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
        var foundEmpty = false;
        var index = 0;

        foreach (var element in focus)
        {
            var innerContext = context.PushThis(element).PushIndex(index++);
            var result = evaluateExpression([element], criteria, innerContext);

            if (!result.Any())
            {
                foundEmpty = true;
                continue;
            }

            if (!FunctionHelpers.IsTrue(result))
            {
                return [(IElement)FunctionHelpers.CreateBoolean(false)];
            }
        }

        if (foundEmpty)
            return [];

        return [(IElement)FunctionHelpers.CreateBoolean(true)];
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
        var foundEmpty = false;
        var index = 0;

        foreach (var element in focus)
        {
            var innerContext = context.PushThis(element).PushIndex(index++);
            var result = evaluateExpression([element], criteria, innerContext);

            if (!result.Any())
            {
                foundEmpty = true;
                continue;
            }

            if (FunctionHelpers.IsTrue(result))
            {
                return [(IElement)FunctionHelpers.CreateBoolean(true)];
            }
        }

        if (foundEmpty)
            return [];

        return [(IElement)FunctionHelpers.CreateBoolean(false)];
    }

    /// <summary>
    /// repeat() - Recursively applies a projection expression until no new elements are found.
    /// Per FHIRPath spec: Returns only the results of the projection, not the original focus items.
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
        var result = new List<IElement>();
        var processed = new List<IElement>();
        var queue = new Queue<IElement>(focus);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            
            // Check if we've already processed this element using deep equality comparison
            if (!processed.Any(p => FunctionHelpers.AreElementsEqual(p, current)))
            {
                processed.Add(current);
                
                var innerContext = context.PushThis(current);
                var projected = evaluateExpression([current], projection, innerContext);
                
                foreach (var item in projected)
                {
                    // Add projection results to the output result set (avoiding duplicates)
                    if (!result.Any(r => FunctionHelpers.AreElementsEqual(r, item)))
                    {
                        result.Add(item);
                    }

                    // If this is a new item, add it to queue for further processing
                    if (!processed.Any(p => FunctionHelpers.AreElementsEqual(p, item)))
                    {
                        queue.Enqueue(item);
                    }
                }
            }
        }

        return result;
    }

    /// <summary>
    /// repeatAll() - Recursively applies a projection expression, allowing duplicates in output.
    /// Unlike repeat(), does NOT check for duplicates before adding - better performance but allows duplicates.
    /// Per FHIRPath spec: $this is set for each item but $index is undefined.
    /// </summary>
    [FhirPathFunction("repeatAll",
        SupportedContexts = "any-any",
        ReturnType = "context",
        SupportsCollections = true,
        MinArguments = 1,
        MaxArguments = 1,
        TakesExpressionArguments = true,
        Category = "Collection",
        Description = "Recursively applies a projection expression, allowing duplicates in output")]
    public static IEnumerable<IElement> RepeatAll(
        IEnumerable<IElement> focus,
        IReadOnlyList<Expression> arguments,
        EvaluationContext context,
        Func<IEnumerable<IElement>, Expression, EvaluationContext, IEnumerable<IElement>> evaluateExpression)
    {
        if (arguments.Count == 0)
            throw new ArgumentException("repeatAll() requires a projection argument");

        var projection = arguments[0];
        var result = new List<IElement>();
        var queue = new Queue<IElement>(focus);

        const int maxIterations = 100_000;
        var iterations = 0;

        while (queue.Count > 0)
        {
            if (++iterations > maxIterations)
                throw new InvalidOperationException($"repeatAll() exceeded maximum iteration limit ({maxIterations}) - possible infinite loop detected");

            var current = queue.Dequeue();

            var innerContext = context.PushThis(current);
            var projected = evaluateExpression([current], projection, innerContext);

            foreach (var item in projected)
            {
                result.Add(item);
                queue.Enqueue(item);
            }
        }

        return result;
    }

    /// <summary>
    /// coalesce() - Returns the first non-empty collection from the arguments.
    /// Uses short-circuit evaluation: arguments after the first non-empty are NOT evaluated.
    /// </summary>
    [FhirPathFunction("coalesce",
        SupportedContexts = "any-any",
        ReturnType = "fromArgument",
        SupportsCollections = true,
        SupportedAtRoot = true,
        MinArguments = 1,
        MaxArguments = int.MaxValue,
        TakesExpressionArguments = true,
        Category = "Collection",
        Description = "Returns the first non-empty collection from the arguments (short-circuit evaluation)")]
    public static IEnumerable<IElement> Coalesce(
        IEnumerable<IElement> focus,
        IReadOnlyList<Expression> arguments,
        EvaluationContext context,
        Func<IEnumerable<IElement>, Expression, EvaluationContext, IEnumerable<IElement>> evaluateExpression)
    {
        if (arguments.Count == 0)
            throw new ArgumentException("coalesce() requires at least one argument");

        foreach (var arg in arguments)
        {
            var result = evaluateExpression(focus, arg, context).ToList();
            if (result.Count > 0)
                return result;
        }

        return [];
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

        return TypeMatcher.FilterByType(focus, typeName, useInheritance: false);
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

        var typeName = TypeMatcher.ExtractTypeName(arguments[0]);
        if (string.IsNullOrEmpty(typeName))
            return [];

        return TypeMatcher.FilterByType(focus, typeName, useInheritance: false);
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

        // Evaluate the argument from $this context if available (e.g., inside select())
        // Otherwise fall back to focus
        var thisElement = context.GetThis();
        var argFocus = thisElement != null ? [thisElement] : focus;
        var other = evaluateExpression(argFocus, arguments[0], context).ToList();
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

        // Evaluate the argument from $this context if available (e.g., inside select())
        // Otherwise use the original evaluation context Focus (not the current result collection)
        var thisElement = context.GetThis();
        var argFocus = thisElement != null ? [thisElement] : context.Focus.AsEnumerable();
        var other = evaluateExpression(argFocus, arguments[0], context);
        return focus.Concat(other);
    }

    /// <summary>
    /// aggregate() - Aggregates elements using an accumulator expression.
    /// </summary>
    [FhirPathFunction("aggregate",
        SupportedContexts = "any-any",
        ReturnType = "fromArgument",
        SupportsCollections = true,
        MinArguments = 1,
        MaxArguments = 2,
        TakesExpressionArguments = true,
        Category = "Collection",
        Description = "Aggregates elements using an accumulator expression")]
    public static IEnumerable<IElement> Aggregate(
        IEnumerable<IElement> focus,
        IReadOnlyList<Expression> arguments,
        EvaluationContext context,
        Func<IEnumerable<IElement>, Expression, EvaluationContext, IEnumerable<IElement>> evaluateExpression)
    {
        if (arguments.Count == 0)
            throw new ArgumentException("aggregate() requires an aggregator expression");

        // Initialize $total: initial-value if provided, otherwise empty
        List<IElement> total =
            arguments.Count > 1
                ? evaluateExpression(focus, arguments[1], context).ToList()
                : [];

        var index = 0;
        foreach (var element in focus)
        {
            var innerContext = context
                .PushThis(element)
                .PushIndex(index++)
                .WithEnvironmentVariable("total", total);

            total = evaluateExpression(
                [element],
                arguments[0],
                innerContext
            ).ToList();
        }

        return total;
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

        // Check if every element in focus exists in other (using structural comparison for complex types)
        var isSubset = focusList.All(f => other.Any(o => AreElementsEqual(o, f)));
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

        // For complex types (where Value is null), use reference equality
        // For primitive types, use value equality
        var isSuperset = other.All(o => focusList.Any(f => AreElementsEqual(f, o)));
        return [(IElement)FunctionHelpers.CreateBoolean(isSuperset)];
    }

    /// <summary>
    /// type() - Returns the type information of each element in the collection.
    /// Returns a ClassInfo or SimpleTypeInfo with name and namespace properties.
    /// </summary>
    [FhirPathFunction("type",
        SupportedContexts = "any-any",
        ReturnType = "ClassInfo",
        SupportsCollections = true,
        MinArguments = 0,
        MaxArguments = 0,
        Category = "Collection",
        Description = "Returns the type information of each element")]
    public static IEnumerable<IElement> Type(IEnumerable<IElement> focus)
    {
        foreach (var element in focus)
        {
            var typeName = element.InstanceType ?? "unknown";
            string ns = "FHIR";
            string name = typeName;

            // Distinguish between System literals (PrimitiveElement) and FHIR elements (e.g. ElementNode, PocoElement)
            // This is a heuristic based on the implementing class name.
            var implType = element.GetType().Name;
            bool isSystemLiteral = implType.Contains("Primitive", StringComparison.OrdinalIgnoreCase);

            if (isSystemLiteral)
            {
                // Map primitives to System namespace and PascalCase
#pragma warning disable CA1308 // Normalize strings to uppercase
                switch (typeName.ToLowerInvariant())
#pragma warning restore CA1308 // Normalize strings to uppercase
                {
                    case "boolean":
                        ns = "System";
                        name = "Boolean";
                        break;
                    case "string":
                        ns = "System";
                        name = "String";
                        break;
                    case "integer":
                        ns = "System";
                        name = "Integer";
                        break;
                    case "decimal":
                        ns = "System";
                        name = "Decimal";
                        break;
                    case "date":
                        ns = "System";
                        name = "Date";
                        break;
                    case "datetime":
                        ns = "System";
                        name = "DateTime";
                        break;
                    case "time":
                        ns = "System";
                        name = "Time";
                        break;
                    case "quantity":
                        ns = "FHIR";
                        name = "Quantity";
                        break;
                    default:
                        if (typeName.Length > 0 && char.IsLower(typeName[0]))
                        {
                            name = char.ToUpperInvariant(typeName[0]) + typeName.Substring(1);
                            ns = "System";
                        }
                        break;
                }
            }

            yield return new TypeInfoElement(name, ns);
        }
    }

    /// <summary>
    /// sort() - Sorts the collection in ascending order.
    /// Can optionally take an expression to determine sort key.
    /// </summary>
    [FhirPathFunction("sort",
        SupportedContexts = "any-any",
        ReturnType = "context",
        SupportsCollections = true,
        MinArguments = 0,
        MaxArguments = int.MaxValue, // Support multiple sort keys
        TakesExpressionArguments = true,
        Category = "Collection",
        Description = "Sorts the collection in ascending order")]
    public static IEnumerable<IElement> Sort(
        IEnumerable<IElement> focus,
        IReadOnlyList<Expression> arguments,
        EvaluationContext context,
        Func<IEnumerable<IElement>, Expression, EvaluationContext, IEnumerable<IElement>> evaluateExpression)
    {
        var list = focus.ToList();

        if (arguments.Count == 0)
        {
            return list.OrderBy(e => e.Value, new ObjectComparer());
        }

        // Extract sort key info (expression and direction) for all arguments
        var sortKeys = arguments.Select(arg =>
        {
            var isDescending = arg is UnaryExpression { Operator: "-" };
            var effectiveExpression = isDescending && arg is UnaryExpression u ? u.Operand : arg;
            return (Expression: effectiveExpression, IsDescending: isDescending);
        }).ToList();

        // Build key selectors
        Func<IElement, object?> createKeySelector(Expression expr) => element =>
        {
            var innerContext = context.PushThis(element);
            var result = evaluateExpression([element], expr, innerContext);
            return result.FirstOrDefault()?.Value;
        };

        // Apply first sort key
        var firstKey = sortKeys[0];
        IComparer<object?> firstComparer = firstKey.IsDescending ? new ObjectComparerNullsFirst() : new ObjectComparer();
        IOrderedEnumerable<IElement> orderedList = firstKey.IsDescending
            ? list.OrderByDescending(createKeySelector(firstKey.Expression), firstComparer)
            : list.OrderBy(createKeySelector(firstKey.Expression), firstComparer);

        // Apply subsequent sort keys with ThenBy/ThenByDescending
        for (int i = 1; i < sortKeys.Count; i++)
        {
            var key = sortKeys[i];
            var keySelector = createKeySelector(key.Expression);
            IComparer<object?> keyComparer = key.IsDescending ? new ObjectComparerNullsFirst() : new ObjectComparer();
            orderedList = key.IsDescending
                ? orderedList.ThenByDescending(keySelector, keyComparer)
                : orderedList.ThenBy(keySelector, keyComparer);
        }

        return orderedList;
    }

    /// <summary>
    /// Standard comparer where null is less than any value (nulls last in descending).
    /// </summary>
    private class ObjectComparer : IComparer<object?>
    {
        public int Compare(object? x, object? y)
        {
            if (x is null && y is null) return 0;
            if (x is null) return -1;
            if (y is null) return 1;

            if (x is IComparable comparableX && y is IComparable)
            {
                try
                {
                    return comparableX.CompareTo(y);
                }
                catch
                {
                    return 0;
                }
            }

            return string.Compare(x.ToString(), y.ToString(), StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Comparer where null is greater than any value (nulls first in descending).
    /// Used for prototype descending sort with - prefix.
    /// </summary>
    private class ObjectComparerNullsFirst : IComparer<object?>
    {
        public int Compare(object? x, object? y)
        {
            if (x is null && y is null) return 0;
            if (x is null) return 1;  // null > any value
            if (y is null) return -1; // any value < null

            if (x is IComparable comparableX && y is IComparable)
            {
                try
                {
                    return comparableX.CompareTo(y);
                }
                catch
                {
                    return 0;
                }
            }

            return string.Compare(x.ToString(), y.ToString(), StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Implementation of TypeInfo/ClassInfo for the type() function.
    /// </summary>
    private class TypeInfoElement : IElement
    {
        private readonly string _name;
        private readonly string _namespace;

        public TypeInfoElement(string name, string ns)
        {
            _name = name;
            _namespace = ns;
            // Value is not strictly defined, but useful for debugging
            Value = $"{ns}.{name}";
            InstanceType = "ClassInfo";
        }

        public string Name => string.Empty;
        public string InstanceType { get; }
        public object Value { get; }
        public string Location => string.Empty;
        public IType? Type => null;

        public T? Meta<T>() where T : class => null;

        public IReadOnlyList<IElement> Children(string? name = null)
        {
            if (string.Equals(name, "name", StringComparison.OrdinalIgnoreCase))
                return [FunctionHelpers.CreateString(_name)];
            
            if (string.Equals(name, "namespace", StringComparison.OrdinalIgnoreCase))
                return [FunctionHelpers.CreateString(_namespace)];
            
            return [];
        }
    }

    /// <summary>
    /// Compares two IElement instances for equality using structural comparison.
    /// For primitive types, uses value equality.
    /// For complex types, performs deep structural comparison of children.
    /// </summary>
    private static bool AreElementsEqual(IElement left, IElement right)
    {
        // If they're the same reference, they're equal
        if (ReferenceEquals(left, right))
            return true;

        // Check instance type match first - different types can't be equal
        if (left.InstanceType != right.InstanceType)
            return false;

        // For complex types (both Values are null), use structural comparison
        if (left.Value == null && right.Value == null)
        {
            return AreElementsStructurallyEqual(left, right);
        }

        // For primitive types, use value comparison
        return FunctionHelpers.AreEqual(left.Value, right.Value);
    }

    /// <summary>
    /// Performs deep structural comparison of two complex elements by recursively comparing all children.
    /// </summary>
    private static bool AreElementsStructurallyEqual(IElement left, IElement right)
    {
        // Get all named children
        var leftChildren = left.Children().Where(c => !string.IsNullOrEmpty(c.Name)).ToList();
        var rightChildren = right.Children().Where(c => !string.IsNullOrEmpty(c.Name)).ToList();

        // Group by name
        var leftByName = leftChildren.GroupBy(c => c.Name).ToDictionary(g => g.Key, g => g.ToList());
        var rightByName = rightChildren.GroupBy(c => c.Name).ToDictionary(g => g.Key, g => g.ToList());

        // Must have same set of child names
        if (leftByName.Count != rightByName.Count)
            return false;

        foreach (var kvp in leftByName)
        {
            if (!rightByName.TryGetValue(kvp.Key, out var rightList))
                return false;

            var leftList = kvp.Value;

            // Must have same number of children with this name
            if (leftList.Count != rightList.Count)
                return false;

            // Order matters for repeating elements - compare positionally
            for (var i = 0; i < leftList.Count; i++)
            {
                if (!AreElementsEqual(leftList[i], rightList[i]))
                    return false;
            }
        }

        return true;
    }
}
