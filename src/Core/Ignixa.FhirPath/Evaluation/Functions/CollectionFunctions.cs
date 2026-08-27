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
    /// <para>
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
    /// </para>
    /// <para>
    /// <b>Iteration guard (#433):</b> the dedup check below only stops a projection that <em>navigates</em>
    /// an existing finite tree - it never terminates for one that <em>constructs</em> a fresh value each
    /// round, e.g. <c>repeat($this &amp; 'x')</c>, whose output is never deep-equal to anything already
    /// processed. <see cref="RepeatAll"/> caps at 100,000 iterations; this cap is 10,000. <b>This is a data-headroom
    /// choice, not a cost-shape choice.</b> The cap guarantees <em>termination</em> - a constructing projection
    /// can no longer loop forever - but it does <em>not</em> bound wall-clock cost. Per-iteration cost grows
    /// with the projection's fan-out (number of <c>|</c> branches): <c>Repeat</c> runs <em>three</em> O(n) scans
    /// per item - <c>processed.Any(...)</c> twice and <c>result.Any(...)</c> once - each via
    /// <see cref="FunctionHelpers.AreElementsEqual"/>, which recurses over subtrees. Measured wall time to reach
    /// the 10,000 cap: <c>repeat($this &amp; 'x')</c> (branching factor 1) reaches 33.4 seconds;
    /// <c>repeat(($this &amp; 'x') | ($this &amp; 'y'))</c> (branching factor 2) reaches 63.4 seconds.
    /// One additional <c>|</c> branch roughly doubles the time to reach the same cap.
    /// </para>
    /// <para>
    /// <b>Why 10,000 and not lower:</b> this is a data-headroom budget, not a cost budget. The control test
    /// (<c>RemainingCoverageTests</c>, nested-Questionnaire case) uses a 1,092-item tree and genuinely dequeues
    /// roughly 1,093 times; a 1,000 iteration cap would reject that legitimate data. 10,000 gives approximately
    /// 9× headroom over that observed real shape. The residual gap - bounding actual per-projection cost, which
    /// depends on fan-out - is closed by the comparison-count budget below.
    /// </para>
    /// <para>
    /// <b>Comparison-count budget (#435):</b> <see cref="RepeatGuardLimits.MaxComparisons"/> caps the total
    /// number of <see cref="FunctionHelpers.AreElementsEqual"/> calls across the whole run, tripping whichever
    /// of it or <see cref="RepeatGuardLimits.MaxIterations"/> is reached first, with the same exception type and
    /// log tier either way. Unlike the iteration cap, this is a <em>cost</em> budget: it bounds wall-clock work
    /// directly, so it catches the fan-out hazard the iteration cap cannot - a wider projection reaches the same
    /// comparison total in fewer iterations, so it is stopped at roughly the same cost regardless of branching.
    /// <b>The value is measured, not assumed.</b> Instrumenting this method and running the control case above
    /// (the same 1,092-item nested-Questionnaire tree, 1,093 iterations) measured 1,391,754 total comparisons.
    /// Instrumenting the <c>repeat($this &amp; 'x')</c> constructing-projection hazard (branching factor 1) to its
    /// full 10,000-iteration cap measured 149,995,000 - roughly 108× the control's cost for roughly 9× its
    /// iteration count, confirming the per-iteration cost genuinely grows faster than linearly as
    /// <c>processed</c>/<c>result</c> grow. 15,000,000 was chosen as the budget: about 10.8× headroom over the
    /// measured 1,391,754 control cost, while still stopping the branching-factor-1 hazard at 3,163 of the 10,000
    /// iterations it used to run - 3.3 seconds instead of 32.4 - and, because this is a cost bound rather than an
    /// iteration bound, stopping a wider-fan-out hazard at roughly the *same* cost rather than at proportionally
    /// more wall-clock time the way the iteration-only cap did. Measured in a single run with the comparison
    /// budget lifted so only the iteration cap applies, branching factor 1 takes 38.97s and branching factor 2
    /// takes 76.32s - one extra <c>|</c> branch does roughly double it. In the same shapes at production
    /// thresholds, branching 1 / 2 / 3 take 4.75s / 5.35s / 4.50s, all three tripping the 15,000,000-comparison
    /// budget rather than the iteration cap: flat in branching factor, which is what #435 was for.
    /// </para>
    /// <para>
    /// <b>What that costs in data, stated in nodes (#435 review).</b> 10.8× is headroom in <em>comparisons</em>,
    /// and comparisons grow as roughly the <em>square</em> of the node count, so it is <em>not</em> the same order
    /// of headroom the 9× iteration cap gives - that equivalence was claimed here originally and is false; it has
    /// been removed. Measured over the control's breadth-3 <c>Questionnaire.item</c> shape, total comparisons fit
    /// ~1.167·N² closely (16,860 at N=120; 153,912 at N=363; 1,391,754 at N=1,092; 12,545,454 at N=3,279 - the
    /// ratio C/N² stays within 0.4% across that range), so 10.8× in comparisons is only √10.8 ≈ 3.3× in nodes.
    /// <b>Bounding a quadratic cost necessarily bounds data. That is the trade this guard is, not a side effect
    /// of it: the accepted-data envelope narrows, deliberately.</b> Measured on that shape with
    /// <c>repeat(item)</c>, at production thresholds, in trees <c>RemainingCoverageTests.CreateDeeplyNestedQuestionnaireItems(breadth, depth)</c>
    /// builds - so a reader can regenerate every row below rather than take it on faith:
    /// <list type="table">
    /// <listheader><term>nodes</term><description>outcome, and the comparison count reached</description></listheader>
    /// <item><term>1,092 (breadth 3, depth 6 - the control)</term><description>OK - 1,391,754</description></item>
    /// <item><term>3,279 (breadth 3, depth 7)</term><description>OK - 12,545,454</description></item>
    /// <item><term>5,460 (breadth 4, depth 6)</term><description><b>throws</b> - the comparison-count budget (15,000,000)</description></item>
    /// </list>
    /// and with the comparison budget lifted, so that only the 10,000-iteration cap applies - i.e. the behaviour
    /// before #435 - the same 5,460-node tree runs to completion at 33,540,780 comparisons, and a 9,840-node tree
    /// (breadth 3, depth 8) reaches 112,968,120, both OK. So the envelope for a navigating <c>repeat()</c> falls
    /// from about 10,000 nodes, where one dequeue per node made the iteration cap the binding constraint, to
    /// somewhere between 3,279 (OK) and 5,460 (throws) <em>for the breadth-3 and breadth-4 shapes the control
    /// uses</em>. That bracket is not a cutover: because the guard bounds <em>cost</em>, not node count, no
    /// single node count is the boundary - a tree's shape decides how many comparisons its nodes cost. Measured
    /// on the same generator at depth 1, where every item is a sibling: 3,872 nodes completes and 3,873 throws
    /// the comparison budget. So a wide shallow tree is refused at 3,873 nodes while a breadth-3 tree of 3,279
    /// passes, and the bracket above describes those two shapes rather than the envelope's edge. Combined
    /// with the all-or-nothing behaviour documented below, an indexer over a tenant expression that
    /// exceeds this drops the whole search parameter rather than truncating it. The exposure is narrow rather
    /// than absent: <c>descendants()</c> has its own non-deduping implementation and does not route through this
    /// method, and <c>repeat(</c> appears in no generated SearchParameter and in only three shipped invariants, so
    /// what is at risk is a tenant-authored <c>repeat()</c> over a resource of several thousand nodes.
    /// <b>Do not raise the budget to widen this.</b> The point of #435 was to bound cost; the narrower data
    /// envelope is the price of that, not a defect in the number.
    /// </para>
    /// <para>
    /// <b>Test seam (#435):</b> both thresholds live on <see cref="RepeatGuardLimits"/>, not as local <c>const</c>s,
    /// specifically so <c>Ignixa.FhirPath.Tests</c> can substitute a small cap via
    /// <see cref="RepeatGuardLimits.Scope"/> and prove either guard trips - same exception type, same message
    /// shape, same log tier - in milliseconds instead of paying the real threshold's full wall-clock cost on every
    /// CI run. Both are read once into locals at the top of this method rather than per comparison: see
    /// <see cref="RepeatGuardLimits"/>'s remarks for why the seam is <see cref="AsyncLocal{T}"/>-backed (a
    /// process-wide static was demonstrated to bleed into a concurrently running test class) and why hoisting is
    /// both necessary and sound.
    /// </para>
    /// <para>
    /// <b>Harmonisation with <see cref="RepeatAll"/>'s 100,000:</b> do not raise this cap toward that figure. The
    /// two functions do not cost the same per iteration: <c>repeatAll</c> is dequeue-evaluate-append, O(1) of
    /// bookkeeping per item. <c>Repeat</c> does three O(n) deep-equality scans per item with an expensive recursing
    /// comparator. Raising 10,000 toward 100,000 would multiply an already-33-second worst case by roughly a hundred,
    /// turning a guard into a hang. This difference is structural and deliberate, not an oversight to be "harmonised"
    /// away later.
    /// </para>
    /// <para>
    /// <b>Exception type and log tier:</b> the guard throws <see cref="FhirPathEvaluationException"/>,
    /// matching <see cref="RepeatAll"/>'s guard, so <c>ElementSearchIndexer.IsExpectedEvaluationFailure</c>
    /// classifies both the same way - expected containment (Warning) rather than an indexer defect (Error).
    /// That tier was affirmed and pinned for <c>repeatAll</c>'s guard in #428 on the rationale
    /// that a guard a tenant can trip on demand, against tenant-supplied data, is data, not an indexer bug.
    /// Two iteration-limit guards reporting at different severities would be worse than either choice, so
    /// both land in the same one.
    /// </para>
    /// <para>
    /// <b>The guard fails all-or-nothing, deliberately.</b> This method is eager - results accumulate
    /// into a local list that the throw abandons - so tripping the cap yields no partial collection, and
    /// a caller indexing a search parameter over this expression drops the whole parameter rather than
    /// storing a truncated one. That is the intended behaviour: a prefix of a non-terminating projection
    /// is an arbitrary cut with no meaning, and half an index that reports itself as complete is worse
    /// than an absent one. Making the method lazy would change this silently, which is why it is written
    /// down here rather than left to the reader to infer from the absence of a <c>yield</c>.
    /// </para>
    /// <para>
    /// <b>Dedup semantics vs. the spec (considered, not changed):</b> the FHIRPath spec defines
    /// <c>repeat()</c>'s dedup via the <c>=</c> operator - "only if they are not already in the output
    /// collection as determined by the equals (<c>=</c>) operator returning <c>true</c> (i.e. <c>false</c>
    /// and empty both indicate that the values are not equal and thus added)" (continuous build,
    /// index.md:1016) - while this method dedups via <see cref="FunctionHelpers.AreElementsEqual"/>, a deep
    /// equality helper. For primitives these coincide. For temporals of mismatched precision, <c>=</c> is
    /// indeterminate (empty), which per the spec passage above means "not equal, so add" - the same outcome
    /// a naive deep-equality check might get wrong by treating "can't decide" as "equal enough to drop". In
    /// this codebase they do not diverge in practice: <see cref="FunctionHelpers.AreElementsEqual"/> routes
    /// temporal comparisons through <see cref="TemporalOperand.AreSameItem"/>, which was itself built to
    /// collapse an indeterminate <see cref="TemporalOperand.AreEqual"/> to <see langword="false"/> (see its
    /// remarks) - i.e. "not the same item" - which is exactly the spec's "not equal, so add". So the
    /// divergence the spec wording invites is already closed for the case that matters here; this is a
    /// documented decision, not an unexamined gap, and no dedup behavior changes in this fix.
    /// </para>
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

        // Read each threshold once per call, not once per iteration and once per comparison:
        // RepeatGuardLimits backs them with AsyncLocal so a lowered test scope cannot bleed into a
        // concurrently running test class, and an AsyncLocal read is not a static field read. Hoisting is
        // sound because this method is eager - the whole run happens inside this call - so a threshold
        // cannot legitimately change mid-run, and a snapshot also guarantees the number named in the
        // exception message is the one the guard actually compared against.
        int maxIterations = RepeatGuardLimits.MaxIterations;
        long maxComparisons = RepeatGuardLimits.MaxComparisons;

        int iterations = 0;
        long comparisons = 0;

        while (queue.Count > 0)
        {
            if (++iterations > maxIterations)
                throw new FhirPathEvaluationException($"repeat() exceeded maximum iteration limit ({maxIterations}) - possible infinite loop detected");

            var current = queue.Dequeue();

            // Check if we've already processed this element using deep equality comparison
            if (!ContainsElement(processed, current))
            {
                processed.Add(current);

                var innerContext = context.PushThis(current);
                var projected = evaluateExpression([current], projection, innerContext);

                foreach (var item in projected)
                {
                    // Add projection results to the output result set (avoiding duplicates)
                    if (!ContainsElement(result, item))
                    {
                        result.Add(item);
                    }

                    // If this is a new item, add it to queue for further processing
                    if (!ContainsElement(processed, item))
                    {
                        queue.Enqueue(item);
                    }
                }
            }
        }

        return result;

        // Counts every deep-equality scan against the comparison-count budget as it goes, rather than
        // after each O(n) scan completes - see the "comparison-count budget" remarks paragraph above for
        // why the check has to live inside the scan rather than once per iteration.
        bool ContainsElement(List<IElement> list, IElement candidate)
        {
            foreach (var existing in list)
            {
                if (++comparisons > maxComparisons)
                    throw new FhirPathEvaluationException($"repeat() exceeded maximum comparison-count budget ({maxComparisons}) - possible expensive projection detected");

                if (FunctionHelpers.AreElementsEqual(existing, candidate))
                    return true;
            }

            return false;
        }
    }

    /// <summary>
    /// repeatAll() - Recursively applies a projection expression, allowing duplicates in output.
    /// Unlike repeat(), does NOT check for duplicates before adding - better performance but allows duplicates.
    /// Per FHIRPath spec: $this is set for each item but $index is undefined.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>ReturnType = "any"</c> for the same reason as <see cref="Repeat"/>: the result is the
    /// projection, not the focus.
    /// </para>
    /// <para>
    /// <b>The iteration guard fails all-or-nothing, deliberately.</b> This method is eager - results
    /// accumulate into a local list that the throw abandons - so tripping the cap yields no partial
    /// collection, and a caller indexing a search parameter over this expression drops the whole
    /// parameter rather than storing a truncated one. That is the intended behaviour: a prefix of a
    /// non-terminating projection is an arbitrary cut with no meaning, and half an index that reports
    /// itself as complete is worse than an absent one. Making the method lazy would change this
    /// silently, which is why it is written down here rather than left to the reader to infer from the
    /// absence of a <c>yield</c>.
    /// </para>
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
