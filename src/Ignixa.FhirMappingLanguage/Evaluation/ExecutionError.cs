/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Represents an error that occurred during mapping execution.
 */

namespace Ignixa.FhirMappingLanguage.Evaluation;

/// <summary>
/// Represents an error that occurred during mapping execution.
/// </summary>
public class ExecutionError
{
    public ExecutionError(
        string message,
        string? location = null,
        string? code = null,
        Exception? exception = null,
        string? ruleName = null,
        string? elementPath = null,
        IReadOnlyList<string>? availableElements = null,
        string? groupName = null,
        int? ruleIndex = null)
    {
        Message = message;
        Location = location;
        Code = code;
        Exception = exception;
        RuleName = ruleName;
        ElementPath = elementPath;
        AvailableElements = availableElements;
        GroupName = groupName;
        RuleIndex = ruleIndex;
    }

    /// <summary>
    /// Gets the error message.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// Gets the location where the error occurred (e.g., "Group: Transform, Rule: MapName").
    /// </summary>
    public string? Location { get; }

    /// <summary>
    /// Gets the error code (e.g., "TRANSFORM_FAILED", "FHIRPATH_ERROR").
    /// </summary>
    public string? Code { get; }

    /// <summary>
    /// Gets the underlying exception if available.
    /// </summary>
    public Exception? Exception { get; }

    /// <summary>
    /// Gets the name of the rule where the error occurred.
    /// </summary>
    public string? RuleName { get; }

    /// <summary>
    /// Gets the element path that caused the error (e.g., "src.birthDate").
    /// </summary>
    public string? ElementPath { get; }

    /// <summary>
    /// Gets the list of available elements when an element was not found.
    /// </summary>
    public IReadOnlyList<string>? AvailableElements { get; }

    /// <summary>
    /// Gets the name of the group where the error occurred.
    /// </summary>
    public string? GroupName { get; }

    /// <summary>
    /// Gets the zero-based index of the rule within the group where the error occurred.
    /// </summary>
    public int? RuleIndex { get; }

    public override string ToString()
    {
        var parts = new List<string>();

        if (RuleName is not null)
        {
            parts.Add($"Rule '{RuleName}'");
        }

        parts.Add(Message);

        if (ElementPath is not null && AvailableElements?.Count > 0)
        {
            parts.Add($"Available elements: [{string.Join(", ", AvailableElements)}]");
        }

        if (GroupName is not null && RuleIndex.HasValue)
        {
            parts.Add($"Location: StructureMap.group[{GroupName}].rule[{RuleIndex}]");
        }

        if (Code is not null)
        {
            parts.Add($"[{Code}]");
        }

        return string.Join(". ", parts);
    }
}
