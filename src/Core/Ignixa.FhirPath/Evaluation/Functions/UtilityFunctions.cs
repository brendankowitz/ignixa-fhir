/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * FhirPath utility function implementations (Phase 23, Week 4).
 * Implements trace(), now(), today(), timeOfDay() according to FHIRPath 3.0.0 spec.
 */

using System.Collections.Immutable;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Attributes;
using Ignixa.FhirPath.Expressions;

namespace Ignixa.FhirPath.Evaluation.Functions;

/// <summary>
/// Utility function implementations for FhirPath expressions.
/// Provides trace, current date/time access functions.
/// </summary>
internal static class UtilityFunctions
{
    /// <summary>
    /// trace() - Returns focus unchanged (for debugging/logging).
    /// When a TraceHandler is configured in the context, emits trace information.
    /// Accepts an optional name parameter to identify the trace point.
    /// </summary>
    /// <param name="focus">Input collection</param>
    /// <param name="arguments">Optional trace name/message arguments</param>
    /// <param name="context">Evaluation context</param>
    /// <param name="evaluateExpression">Function to evaluate expression arguments</param>
    /// <returns>Focus collection unchanged</returns>
    [FhirPathFunction("trace",
        SupportedContexts = "any-any",
        ReturnType = "context",
        MinArguments = 0,
        MaxArguments = 2,
        TakesExpressionArguments = true,
        Category = "Utility",
        Description = "Returns focus unchanged for debugging/logging")]
    public static IEnumerable<IElement> Trace(
        IEnumerable<IElement> focus,
        IReadOnlyList<Expression> arguments,
        EvaluationContext context,
        Func<IEnumerable<IElement>, Expression, EvaluationContext, IEnumerable<IElement>> evaluateExpression)
    {
        // Materialize focus to a list so we can both trace it and return it
        var focusList = focus.ToImmutableList();

        // If a trace handler is configured, invoke it
        if (context.TraceHandler != null)
        {
            // Per spec: If no projection argument is provided, the input collection is logged without scoping.
            // If the projection argument is provided (2nd arg), it is evaluated for each item with $this and $index.
            if (arguments.Count < 2)
            {
                // No projection - single trace with full focus
                string traceName = "trace";
                ISourcePositionInfo? location = null;

                if (arguments.Count > 0)
                {
                    var nameExpr = arguments[0];
                    location = nameExpr.Location;
                    var nameResult = evaluateExpression(focusList, nameExpr, context).ToList();
                    if (nameResult.Count == 1 && nameResult[0].Value is string str)
                    {
                        traceName = str;
                    }
                }

                var traceEntry = new TraceEntry(traceName, focusList, location);
                context.TraceHandler(traceEntry);
            }
            else
            {
                // Has projection - iterate and trace per item with $this and $index
                var index = 0;
                foreach (var element in focusList)
                {
                    string traceName = "trace";
                    ISourcePositionInfo? location = null;
                    var innerContext = context.PushThis(element).PushIndex(index++);

                    // First argument is the name
                    var nameExpr = arguments[0];
                    location = nameExpr.Location;
                    var nameResult = evaluateExpression([element], nameExpr, innerContext).ToList();
                    if (nameResult.Count == 1 && nameResult[0].Value is string str)
                    {
                        traceName = str;
                    }

                    // Second argument is the projection - evaluate it for the trace output
                    var projectionResult = evaluateExpression([element], arguments[1], innerContext).ToImmutableList();
                    var traceEntry = new TraceEntry(traceName, projectionResult, location);
                    context.TraceHandler(traceEntry);
                }
            }
        }

        // Always return focus unchanged (trace is for side-effect logging only)
        return focusList;
    }

    /// <summary>
    /// now() - Returns current UTC date and time.
    /// Returns dateTime in ISO 8601 format.
    /// </summary>
    /// <param name="focus">Input collection (unused)</param>
    /// <returns>Current UTC dateTime</returns>
    [FhirPathFunction("now",
        SupportedContexts = "any-any",
        ReturnType = "dateTime",
        SupportedAtRoot = true,
        MinArguments = 0,
        MaxArguments = 0,
        Category = "Utility",
        Description = "Returns current UTC date and time")]
    public static IEnumerable<IElement> Now(IEnumerable<IElement> focus)
    {
        var now = DateTime.UtcNow.ToString("o");
        return [FunctionHelpers.CreateDateTime(now)];
    }

    /// <summary>
    /// today() - Returns current date (without time component).
    /// Returns date in YYYY-MM-DD format.
    /// </summary>
    /// <param name="focus">Input collection (unused)</param>
    /// <returns>Current date</returns>
    [FhirPathFunction("today",
        SupportedContexts = "any-any",
        ReturnType = "date",
        SupportedAtRoot = true,
        MinArguments = 0,
        MaxArguments = 0,
        Category = "Utility",
        Description = "Returns current date without time component")]
    public static IEnumerable<IElement> Today(IEnumerable<IElement> focus)
    {
        // Use UTC date to match now() which returns UTC datetime
        // This ensures consistent comparison behavior (now() > today() returns uncertain, not true/false based on timezone)
        var today = DateTime.UtcNow.Date.ToString("yyyy-MM-dd");
        return [FunctionHelpers.CreateDate(today)];
    }

    /// <summary>
    /// timeOfDay() - Returns current time of day (without date component).
    /// Returns time in HH:mm:ss format.
    /// </summary>
    /// <param name="focus">Input collection (unused)</param>
    /// <returns>Current time of day</returns>
    [FhirPathFunction("timeOfDay",
        SupportedContexts = "any-any",
        ReturnType = "time",
        SupportedAtRoot = true,
        MinArguments = 0,
        MaxArguments = 0,
        Category = "Utility",
        Description = "Returns current time of day without date component")]
    public static IEnumerable<IElement> TimeOfDay(IEnumerable<IElement> focus)
    {
        var time = DateTime.Now.TimeOfDay.ToString(@"hh\:mm\:ss");
        return [FunctionHelpers.CreateTime(time)];
    }

    /// <summary>
    /// defineVariable(name, expr) - Evaluates expr and stores result in a variable.
    /// The variable can be accessed later in the expression using %name.
    /// Returns the input collection unchanged (pass-through).
    /// </summary>
    /// <param name="focus">Input collection (returned unchanged)</param>
    /// <param name="arguments">Variable name and optional value expression</param>
    /// <param name="context">Evaluation context (used to store the variable)</param>
    /// <param name="evaluateExpression">Function to evaluate expression arguments</param>
    /// <returns>Focus collection unchanged</returns>
    /// <remarks>
    /// Note: This function is registered via [FhirPathFunction] but is handled specially
    /// in FhirPathEvaluator.EvaluateDefineVariable() because it needs to mutate the context.
    /// The attribute registration enables function discovery and validation.
    /// </remarks>
    [FhirPathFunction("defineVariable",
        SupportedContexts = "any-any",
        ReturnType = "context",
        MinArguments = 1,
        MaxArguments = 2,
        TakesExpressionArguments = true,
        Category = "Utility",
        Description = "Stores expression result in a named variable accessible via %name")]
    public static IEnumerable<IElement> DefineVariable(
        IEnumerable<IElement> focus,
        IReadOnlyList<Expression> arguments,
        EvaluationContext context,
        Func<IEnumerable<IElement>, Expression, EvaluationContext, IEnumerable<IElement>> evaluateExpression)
    {
        // Note: Actual implementation is in FhirPathEvaluator.EvaluateDefineVariable()
        // because it requires mutable context access. This method exists for attribute-based
        // function discovery. If this method is called directly, fall through to the evaluator.
        var focusList = focus.ToImmutableList();

        if (arguments.Count < 1 || arguments.Count > 2)
        {
            throw new InvalidOperationException("defineVariable requires 1 or 2 arguments: variable name and optional value expression");
        }

        // Evaluate the name argument
        var nameResult = evaluateExpression(focusList, arguments[0], context).ToList();
        if (nameResult.Count != 1 || nameResult[0].Value is not string variableName)
        {
            throw new InvalidOperationException("defineVariable requires a string as the first argument (literal, identifier, or expression that evaluates to a string)");
        }

        // Evaluate the value expression (or use focus if not provided)
        ImmutableList<IElement> valueResult;
        if (arguments.Count == 2)
        {
            valueResult = evaluateExpression(focusList, arguments[1], context).ToImmutableList();
        }
        else
        {
            valueResult = focusList;
        }

        // Store in context's DefinedVariables dictionary
        context.DefinedVariables[variableName] = valueResult;

        // Return focus unchanged
        return focusList;
    }
}
