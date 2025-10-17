// <copyright file="ValidationIssue.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

namespace Ignixa.Validation;

/// <summary>
/// Represents a single validation issue found during resource validation.
/// </summary>
public sealed record ValidationIssue
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ValidationIssue"/> class.
    /// </summary>
    /// <param name="severity">The severity of the issue.</param>
    /// <param name="path">The element path where the issue was found (e.g., "Patient.name[0]").</param>
    /// <param name="message">A human-readable description of the issue.</param>
    public ValidationIssue(IssueSeverity severity, string path, string message)
    {
        Severity = severity;
        Path = path ?? throw new ArgumentNullException(nameof(path));
        Message = message ?? throw new ArgumentNullException(nameof(message));
    }

    /// <summary>
    /// Gets the severity of the validation issue.
    /// </summary>
    public IssueSeverity Severity { get; }

    /// <summary>
    /// Gets the element path where the issue was found.
    /// </summary>
    /// <example>
    /// "Patient.name[0]", "Observation.value", "Bundle.entry[2].resource"
    /// </example>
    public string Path { get; }

    /// <summary>
    /// Gets the human-readable description of the issue.
    /// </summary>
    public string Message { get; }
}
