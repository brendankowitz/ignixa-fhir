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
/// <remarks>
/// Constructed with the element's declared type (<c>Coding</c> or <c>CodeableConcept</c>), the check knows which it is
/// looking at. A CodeableConcept is checked through its <c>coding</c> entries only, so a text-only concept, which is valid
/// FHIR, raises nothing. Constructed without one (the original constructor) it guesses from the instance, treating an
/// element with no <c>coding</c> child as a Coding; that guess flags a text-only CodeableConcept, so the schema builder
/// always passes the declared type.
/// </remarks>
public class CodingStructureCheck : IValidationCheck
{
    private readonly string _fieldName;
    private readonly string? _shadowName;

    private readonly string? _declaredType;

    /// <summary>
    /// Gets a value indicating whether the element name is exact - not a choice element's base name (<c>value</c>), whose
    /// children are its concrete variants - so an absent element can be passed from the raw JSON without a child lookup.
    /// Set by the schema builder, which knows; false for any other caller.
    /// </summary>
    internal bool ExactName
    {
        get => _shadowName is not null;
        init => _shadowName = value ? $"_{_fieldName}" : null;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CodingStructureCheck"/> class that infers Coding or CodeableConcept from the instance.
    /// </summary>
    /// <param name="fieldName">The name of the Coding or CodeableConcept field (e.g., "code", "category").</param>
    public CodingStructureCheck(string fieldName)
    {
        _fieldName = fieldName;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CodingStructureCheck"/> class for an element of a declared type.
    /// </summary>
    /// <param name="fieldName">The name of the Coding or CodeableConcept field (e.g., "code", "valueCodeableConcept").</param>
    /// <param name="declaredType">The element's declared type: <c>Coding</c> or <c>CodeableConcept</c>.</param>
    /// <exception cref="ArgumentException"><paramref name="declaredType"/> is neither.</exception>
    public CodingStructureCheck(string fieldName, string declaredType)
    {
        if (declaredType is not ("Coding" or "CodeableConcept"))
        {
            throw new ArgumentException($"A coding structure check applies to Coding or CodeableConcept, not '{declaredType}'.", nameof(declaredType));
        }

        _fieldName = fieldName;
        _declaredType = declaredType;
    }

    /// <summary>
    /// Validates Coding/CodeableConcept structure.
    /// </summary>
    /// <param name="element">The element to validate.</param>
    /// <param name="settings">Validation settings.</param>
    /// <param name="state">Current validation state.</param>
    /// <returns>A validation result indicating success or failure.</returns>
    public ValidationResult Validate(IElement element, ValidationSettings settings, ValidationState state)
    {
        var issues = new List<ValidationIssue>();

        if (_shadowName is not null && RawMembers.CertainlyAbsent(element, _fieldName, _shadowName))
        {
            return ValidationResult.Success();
        }

        var fieldNodes = element.Children(_fieldName);
        foreach (var fieldNode in fieldNodes)
        {
            var codingChildren = fieldNode.Children("coding");
            if (IsCodeableConcept(codingChildren))
            {
                foreach (var coding in codingChildren)
                {
                    ValidateSingleCoding(PathOf(coding, $"{_fieldName}.coding"), coding, issues, settings);
                }
            }
            else
            {
                ValidateSingleCoding(PathOf(fieldNode, _fieldName), fieldNode, issues, settings);
            }
        }

        if (issues.Count > 0)
        {
            return ValidationResult.Failure(issues);
        }

        return ValidationResult.Success();
    }

    private bool IsCodeableConcept(IReadOnlyList<IElement> codingChildren) =>
        _declaredType is { } declared ? declared == "CodeableConcept" : codingChildren.Count > 0;

    // The declared-type form reports the node's own location (resource-rooted, indexed); the inferring form keeps its
    // original field-relative path.
    private string PathOf(IElement node, string fieldRelative) =>
        _declaredType is not null && !string.IsNullOrEmpty(node.Location) ? node.Location : fieldRelative;

    private static void ValidateSingleCoding(
        string path,
        IElement codingNode,
        List<ValidationIssue> issues,
        ValidationSettings settings)
    {
        bool hasSystem = codingNode.Children("system").Any();
        bool hasCode = codingNode.Children("code").Any();

        if (!hasSystem && !hasCode)
        {
            issues.Add(ValidationIssue.FromTemplate(IssueSeverity.Warning, "coding-1", path, IssueTemplates.CodingWithoutSystemOrCode));
        }

        // Validate that Coding.system is an absolute URI if present
        // Skip this check in Compatibility mode (matches Microsoft FHIR Server behavior)
        if (settings.Depth != ValidationDepth.Compatibility)
        {
            var systemChildren = codingNode.Children("system");
            if (systemChildren.Count > 0)
            {
                var systemNode = systemChildren[0];
                string? systemValue = systemNode.Value?.ToString();
                if (!string.IsNullOrEmpty(systemValue) && !IsAbsoluteUri(systemValue))
                {
                    issues.Add(ValidationIssue.FromTemplate(
                        IssueSeverity.Error,
                        "coding-system-absolute",
                        systemNode.Location ?? $"{path}.system",
                        IssueTemplates.CodingSystemNotAbsolute));
                }
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
