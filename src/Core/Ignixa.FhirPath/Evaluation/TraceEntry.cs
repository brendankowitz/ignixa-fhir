/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * TraceEntry for capturing FhirPath trace() function output.
 */

using System.Collections.Immutable;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Expressions;

namespace Ignixa.FhirPath.Evaluation;

/// <summary>
/// Represents a trace entry captured during FhirPath expression evaluation.
/// Contains the trace name, focus values, and optional expression position information.
/// </summary>
public sealed class TraceEntry
{
    /// <summary>
    /// Creates a new trace entry.
    /// </summary>
    /// <param name="name">The trace name/identifier (from trace() function parameter or auto-generated)</param>
    /// <param name="focus">The focus collection at the point of tracing</param>
    /// <param name="location">Optional source position information for the trace() call</param>
    public TraceEntry(string name, ImmutableList<IElement> focus, ISourcePositionInfo? location = null)
    {
        Name = name;
        Focus = focus;
        Location = location;
    }

    /// <summary>
    /// The trace name/identifier.
    /// If trace() is called with a name parameter, this is that name.
    /// Otherwise, this is auto-generated (e.g., "trace" or based on location).
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// The focus collection at the point where trace() was called.
    /// This is the input to trace() and also what trace() returns.
    /// </summary>
    public ImmutableList<IElement> Focus { get; }

    /// <summary>
    /// Optional source position information indicating where in the FhirPath expression
    /// the trace() call occurred.
    /// </summary>
    public ISourcePositionInfo? Location { get; }

    /// <summary>
    /// Returns a string representation of this trace entry for debugging.
    /// </summary>
    public override string ToString()
    {
        var locationStr = Location != null 
            ? $" at line {Location.LineNumber}, column {Location.LinePosition}" 
            : string.Empty;
        
        var focusCount = Focus.Count;
        var focusStr = focusCount switch
        {
            0 => "empty",
            1 => $"1 element: {FormatElement(Focus[0])}",
            _ => $"{focusCount} elements"
        };

        return $"Trace '{Name}'{locationStr}: {focusStr}";
    }

    private static string FormatElement(IElement element)
    {
        var value = element.Value;
        if (value == null) return $"{element.InstanceType}(null)";
        
        var valueStr = value.ToString() ?? "null";
        if (valueStr.Length > 50)
        {
            valueStr = string.Concat(valueStr.AsSpan(0, 47), "...");
        }
        
        return $"{element.InstanceType}({valueStr})";
    }
}
