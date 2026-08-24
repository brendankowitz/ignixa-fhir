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
        return FunctionHelpers.Distinct(focus);
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
        var isDistinct = FunctionHelpers.Distinct(list).Count == list.Count;
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
            throw new FhirPathEvaluationException("single() called on collection with multiple items");

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
            throw new FhirPathEvaluationException("skip() requires a num argument");

        // Non-scoped function: evaluate argument in outer context (don't change $this)
        var numResult = evaluateExpression(context.Focus, arguments[0], context).SingleOrDefault();
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
            throw new FhirPathEvaluationException("take() requires a num argument");

        // Non-scoped function: evaluate argument in outer context (don't change $this)
        var numResult = evaluateExpression(context.Focus, arguments[0], context).SingleOrDefault();
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
            throw new FhirPathEvaluationException("where() requires a criteria argument");

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
            throw new FhirPathEvaluationException("select() requires a projection argument");

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
            throw new FhirPathEvaluationException("all() requires a criteria argument");

        var criteria = arguments[0];
        var index = 0;

        foreach (var element in focus)
        {
            var innerContext = context.PushThis(element).PushIndex(index++);
            var result = evaluateExpression([element], criteria, innerContext);

            // Per FHIRPath spec: all() returns true only if criteria evaluates to true for every element.
            // If criteria returns empty or false for any element, all() returns false (not empty).
            if (!FunctionHelpers.IsTrue(result))
            {
                return [(IElement)FunctionHelpers.CreateBoolean(false)];
            }
        }

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
    /// <remarks>
    /// <c>ReturnType = "any"</c> rather than <c>"context"</c> because this returns the projection's
    /// results, never the focus items themselves, so passing the focus type through would name a type the
    /// evaluator cannot produce. It did: <c>(name.repeat(family)).ofType(string)</c> typed the result as
    /// <c>HumanName</c> and was reported as provably empty while the evaluator returned two strings
    /// (#423). Naming the projection's type instead would need a fixpoint over the recursion, which
    /// <c>descendants()</c> - the same shape of unbounded recursion - already declines to do for the same
    /// reason. Unknown fails open in the cast and provenance paths, and every site that raises an
    /// always-empty diagnostic is gated on the focus not being unknown, so widening the type here cannot
    /// manufacture a claim - it can only drop one. The cost is losing true always-empty diagnostics
    /// downstream of a <c>repeat()</c>, and downstream of one only: <c>repeat(</c> appears in no generated
    /// search parameter definition, and in three shipped invariant expressions (R5 and R6 only; two on
    /// PlanDefinition, one on QuestionnaireResponse), none of which navigates a cast off the result.
    /// </remarks>
    [FhirPathFunction("repeat",
        SupportedContexts = "any-any",
        ReturnType = "any",
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
            throw new FhirPathEvaluationException("repeat() requires a projection argument");

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
    /// <remarks>
    /// <c>ReturnType = "any"</c> for the same reason as <see cref="Repeat"/>: the result is the
    /// projection, not the focus.
    /// </remarks>
    [FhirPathFunction("repeatAll",
        SupportedContexts = "any-any",
        ReturnType = "any",
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
            throw new FhirPathEvaluationException("repeatAll() requires a projection argument");

        var projection = arguments[0];
        var result = new List<IElement>();
        var queue = new Queue<IElement>(focus);

        const int maxIterations = 100_000;
        var iterations = 0;

        while (queue.Count > 0)
        {
            if (++iterations > maxIterations)
                throw new FhirPathEvaluationException($"repeatAll() exceeded maximum iteration limit ({maxIterations}) - possible infinite loop detected");

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
            throw new FhirPathEvaluationException("coalesce() requires at least one argument");

        // Non-scoped function: evaluate arguments in outer context (don't change $this)
        foreach (var arg in arguments)
        {
            var result = evaluateExpression(context.Focus, arg, context).ToList();
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
            throw new FhirPathEvaluationException("ofType() requires a type argument");

        string? typeName = null;

        if (arguments[0] is IdentifierExpression idExpr)
        {
            typeName = idExpr.Name;
        }
        else
        {
            // Non-scoped function: evaluate argument in outer context (don't change $this)
            var result = evaluateExpression(context.Focus, arguments[0], context).ToList();
            if (result.Count > 0)
            {
                typeName = result[0].Value?.ToString();
            }
        }

        if (string.IsNullOrEmpty(typeName))
            return [];

        TypeMatcher.EnsureTypeIdentifierResolves(typeName, context.Schema, "ofType()");

        return TypeMatcher.FilterByType(focus, typeName, context.Schema);
    }

    /// <summary>
    /// as() - Type coercion. Returns the input if it is of the given type, otherwise empty; a multi-item
    /// input is an error. See <see cref="TypeMatcher.EnsureSingletonInput"/> for why, and for how that
    /// differs from <c>ofType()</c>.
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
        IReadOnlyList<Expression> arguments,
        EvaluationContext context)
    {
        if (arguments.Count == 0)
            throw new FhirPathEvaluationException("as() requires a type argument");

        var typeName = TypeMatcher.ExtractTypeName(arguments[0]);
        if (string.IsNullOrEmpty(typeName))
            return [];

        TypeMatcher.EnsureTypeIdentifierResolves(typeName, context.Schema, "as()");

        var input = focus as IReadOnlyCollection<IElement> ?? focus.ToList();
        TypeMatcher.EnsureSingletonInput(input.Count, context.Schema, "as()");

        return TypeMatcher.FilterByType(input, typeName, context.Schema);
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
            throw new FhirPathEvaluationException("intersect() requires an other argument");

        // Non-scoped function: evaluate argument in outer context (don't change $this)
        var other = evaluateExpression(context.Focus, arguments[0], context).ToList();
        var result = new List<IElement>();

        foreach (var item in focus)
        {
            if (other.Any(o => FunctionHelpers.AreElementsEqual(o, item)) && !result.Any(r => FunctionHelpers.AreElementsEqual(r, item)))
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
            throw new FhirPathEvaluationException("exclude() requires an other argument");

        // Non-scoped function: evaluate argument in outer context (don't change $this)
        var other = evaluateExpression(context.Focus, arguments[0], context).ToList();
        var result = new List<IElement>();

        foreach (var item in focus)
        {
            if (!other.Any(o => FunctionHelpers.AreElementsEqual(o, item)))
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
            throw new FhirPathEvaluationException("union() requires an other argument");

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
            throw new FhirPathEvaluationException("combine() requires an other argument");

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
            throw new FhirPathEvaluationException("aggregate() requires an aggregator expression");

        // Initialize $total: initial-value if provided, otherwise empty
        // Per spec: init argument is evaluated on the outer context (before $this/$index are set)
        List<IElement> total =
            arguments.Count > 1
                ? evaluateExpression(context.Focus, arguments[1], context).ToList()
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
            throw new FhirPathEvaluationException("subsetOf() requires an other argument");

        var focusList = focus.ToList();
        // Non-scoped function: evaluate argument in outer context (don't change $this)
        var other = evaluateExpression(context.Focus, arguments[0], context).ToList();

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
            throw new FhirPathEvaluationException("supersetOf() requires an other argument");

        var focusList = focus.ToList();
        // Non-scoped function: evaluate argument in outer context (don't change $this)
        var other = evaluateExpression(context.Focus, arguments[0], context).ToList();

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

            // System literals and engine-produced values declare themselves; FHIR elements (ElementNode,
            // SchemaAwareElement, PocoElement) do not. See ISystemValueElement for why this is declared
            // rather than inferred from the implementing class name.
            bool isSystemLiteral = element is ISystemValueElement;

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
            return RunSort(list.OrderBy(e => (IElement?)e, ValueOrdering.SortComparer.NullsLow));
        }

        // Extract sort key info (expression and direction) for all arguments
        var sortKeys = arguments.Select(arg =>
        {
            var isDescending = arg is UnaryExpression { Operator: "-" };
            var effectiveExpression = isDescending && arg is UnaryExpression u ? u.Operand : arg;
            return (Expression: effectiveExpression, IsDescending: isDescending);
        }).ToList();

        // The key is the element rather than its value: SortComparer needs the declared instance type to
        // tell a FHIRPath @-literal - still a plain string - from a string that is only a string.
        Func<IElement, IElement?> createKeySelector(Expression expr) => element =>
        {
            var innerContext = context.PushThis(element);
            var result = evaluateExpression([element], expr, innerContext);
            return result.FirstOrDefault();
        };

        // Apply first sort key
        var firstKey = sortKeys[0];
        var firstComparer = firstKey.IsDescending
            ? ValueOrdering.SortComparer.NullsHigh
            : ValueOrdering.SortComparer.NullsLow;
        IOrderedEnumerable<IElement> orderedList = firstKey.IsDescending
            ? list.OrderByDescending(createKeySelector(firstKey.Expression), firstComparer)
            : list.OrderBy(createKeySelector(firstKey.Expression), firstComparer);

        // Apply subsequent sort keys with ThenBy/ThenByDescending
        for (int i = 1; i < sortKeys.Count; i++)
        {
            var key = sortKeys[i];
            var keySelector = createKeySelector(key.Expression);
            var keyComparer = key.IsDescending
                ? ValueOrdering.SortComparer.NullsHigh
                : ValueOrdering.SortComparer.NullsLow;
            orderedList = key.IsDescending
                ? orderedList.ThenByDescending(keySelector, keyComparer)
                : orderedList.ThenBy(keySelector, keyComparer);
        }

        return RunSort(orderedList);
    }

    /// <summary>
    /// Runs the sort eagerly so that the comparer's error surfaces as itself.
    /// </summary>
    /// <remarks>
    /// <see cref="Array.Sort{T}(T[], IComparer{T})"/> catches anything an <see cref="IComparer{T}"/>
    /// throws and re-raises it as a bare <see cref="InvalidOperationException"/> whose message is
    /// "Failed to compare two elements in the array." That erases the one distinction
    /// <see cref="FhirPathEvaluationException"/> exists to draw - an ill-formed expression versus a defect
    /// in the engine - so <c>FhirPathInvariantCheck</c> and every other caller filtering on the type would
    /// classify a mixed-type <c>sort()</c> as an internal fault. Ordering is eager regardless of when it
    /// is enumerated, so materialising here costs nothing but brings the failure back inside a frame that
    /// can unwrap it.
    /// </remarks>
    private static IEnumerable<IElement> RunSort(IOrderedEnumerable<IElement> ordered)
    {
        try
        {
            return ordered.ToList();
        }
        catch (InvalidOperationException ex) when (ex.InnerException is FhirPathEvaluationException inner)
        {
            throw new FhirPathEvaluationException(inner.Message, ex);
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
        public bool HasPrimitiveValue => false; // ClassInfo is a complex type

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
        return FunctionHelpers.AreElementsEqual(left, right);
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
