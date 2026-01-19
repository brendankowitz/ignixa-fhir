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
            // Get trace name from first argument if provided
            string traceName = "trace";
            ISourcePositionInfo? location = null;

            if (arguments.Count > 0)
            {
                var nameExpr = arguments[0];
                location = nameExpr.Location;

                // Evaluate the name expression to get the trace name
                var nameResult = evaluateExpression(focusList, nameExpr, context).ToList();
                if (nameResult.Count == 1 && nameResult[0].Value is string str)
                {
                    traceName = str;
                }
            }

            // Create and emit trace entry
            var traceEntry = new TraceEntry(traceName, focusList, location);
            context.TraceHandler(traceEntry);
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
}
