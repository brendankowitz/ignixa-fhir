/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * FhirPath utility function implementations (Phase 23, Week 4).
 * Implements trace(), now(), today(), timeOfDay() according to FHIRPath 3.0.0 spec.
 */

using Ignixa.Abstractions;
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
    /// In a real implementation, this would log to a trace output.
    /// </summary>
    /// <param name="focus">Input collection</param>
    /// <param name="arguments">Optional trace name/message arguments</param>
    /// <param name="context">Evaluation context</param>
    /// <returns>Focus collection unchanged</returns>
    public static IEnumerable<ITypedElement> Trace(
        IEnumerable<ITypedElement> focus,
        IReadOnlyList<Expression> arguments,
        EvaluationContext context)
    {
        // Simplified: Just return focus unchanged
        // In a real implementation, this would log to a trace output
        return focus;
    }

    /// <summary>
    /// now() - Returns current UTC date and time.
    /// Returns dateTime in ISO 8601 format.
    /// </summary>
    /// <param name="focus">Input collection (unused)</param>
    /// <returns>Current UTC dateTime</returns>
    public static IEnumerable<ITypedElement> Now(IEnumerable<ITypedElement> focus)
    {
        var now = DateTime.UtcNow.ToString("o");
        return new[] { FunctionHelpers.CreateDateTime(now) };
    }

    /// <summary>
    /// today() - Returns current date (without time component).
    /// Returns date in YYYY-MM-DD format.
    /// </summary>
    /// <param name="focus">Input collection (unused)</param>
    /// <returns>Current date</returns>
    public static IEnumerable<ITypedElement> Today(IEnumerable<ITypedElement> focus)
    {
        var today = DateTime.Today.ToString("yyyy-MM-dd");
        return new[] { FunctionHelpers.CreateDate(today) };
    }

    /// <summary>
    /// timeOfDay() - Returns current time of day (without date component).
    /// Returns time in HH:mm:ss format.
    /// </summary>
    /// <param name="focus">Input collection (unused)</param>
    /// <returns>Current time of day</returns>
    public static IEnumerable<ITypedElement> TimeOfDay(IEnumerable<ITypedElement> focus)
    {
        var time = DateTime.Now.TimeOfDay.ToString(@"hh\:mm\:ss");
        return new[] { FunctionHelpers.CreateTime(time) };
    }
}
