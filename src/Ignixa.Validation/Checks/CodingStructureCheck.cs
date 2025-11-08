// <copyright file="CodingStructureCheck.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using Ignixa.Abstractions;
using Ignixa.Validation.Abstractions;

namespace Ignixa.Validation.Checks;

/// <summary>
/// Validates FHIR Coding and CodeableConcept structure.
/// Ensures Coding has at least system or code.
/// Tier 1 (Fast) validator.
/// </summary>
public class CodingStructureCheck : IValidationCheck
{
    private readonly string _fieldName;

    /// <summary>
    /// Initializes a new instance of the <see cref="CodingStructureCheck"/> class.
    /// </summary>
    /// <param name="fieldName">The name of the Coding or CodeableConcept field (e.g., "code", "category").</param>
    public CodingStructureCheck(string fieldName)
    {
        _fieldName = fieldName;
    }

    /// <summary>
    /// Validates Coding/CodeableConcept structure.
    /// </summary>
    /// <param name="node">The source node to validate.</param>
    /// <param name="settings">Validation settings.</param>
    /// <param name="state">Current validation state.</param>
    /// <returns>A validation result indicating success or failure.</returns>
    public ValidationResult Validate(ISourceNode node, ValidationSettings settings, ValidationState state)
    {
        var issues = new List<ValidationIssue>();

        var fieldNodes = node.Children(_fieldName);
        foreach (var fieldNode in fieldNodes)
        {
            // Check if this is a CodeableConcept (has 'coding' child) or a Coding directly
            var codingChildren = fieldNode.Children("coding").ToList();
            if (codingChildren.Count > 0)
            {
                // This is a CodeableConcept - validate each Coding in the array
                foreach (var coding in codingChildren)
                {
                    ValidateSingleCoding($"{_fieldName}.coding", coding, issues);
                }
            }
            else
            {
                // This is a Coding directly
                ValidateSingleCoding(_fieldName, fieldNode, issues);
            }
        }

        if (issues.Count > 0)
        {
            return ValidationResult.Failure(issues);
        }

        return ValidationResult.Success();
    }

    private static void ValidateSingleCoding(
        string path,
        ISourceNode codingNode,
        List<ValidationIssue> issues)
    {
        bool hasSystem = codingNode.Children("system").Any();
        bool hasCode = codingNode.Children("code").Any();

        if (!hasSystem && !hasCode)
        {
            issues.Add(new ValidationIssue(
                IssueSeverity.Warning,
                "coding-1",
                path,
                "Coding should have at least a system or code"));
        }

        // Validate that Coding.system is an absolute URI if present
        var systemNode = codingNode.Children("system").FirstOrDefault();
        if (systemNode != null)
        {
            string? systemValue = systemNode.Text;
            if (!string.IsNullOrEmpty(systemValue) && !IsAbsoluteUri(systemValue))
            {
                issues.Add(ValidationIssue.InvariantFailure(
                    "coding-system-absolute",
                    "Coding.system must be an absolute reference, not a local reference",
                    systemNode.Location ?? $"{path}.system"));
            }
        }
    }

    /// <summary>
    /// Determines if a URI string is an absolute URI.
    /// Absolute URIs have a scheme (http://, https://, urn:, etc.) and are globally resolvable.
    /// </summary>
    /// <param name="uri">The URI string to validate.</param>
    /// <returns>True if the URI is absolute; false if it's relative or null/empty.</returns>
    private static bool IsAbsoluteUri(string? uri) =>
        !string.IsNullOrEmpty(uri) && Uri.TryCreate(uri, UriKind.Absolute, out _);
}
