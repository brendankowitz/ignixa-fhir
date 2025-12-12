using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Ignixa.Validation;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Schema;

namespace Ignixa.FhirFakes.Cli;

/// <summary>
/// Helper utility for validating generated FHIR resources.
/// </summary>
internal static class ValidationHelper
{
    /// <summary>
    /// Validates a FHIR resource against its schema.
    /// </summary>
    public static ValidationResult ValidateResource(
        JsonNode resourceNode,
        IFhirSchemaProvider schemaProvider,
        ValidationSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(resourceNode);
        ArgumentNullException.ThrowIfNull(schemaProvider);

        try
        {
            // Extract resource type
            var sourceNode = JsonNodeSourceNode.Create(resourceNode);
            var resourceType = sourceNode.ResourceType ?? sourceNode.Name;

            if (string.IsNullOrEmpty(resourceType))
            {
                return ValidationResult.Failure(
                    new ValidationIssue(
                        code: "resource-type-missing",
                        path: "$",
                        message: "Could not determine resource type from JSON",
                        severity: IssueSeverity.Error));
            }

            // Get validation schema
            var canonicalUrl = $"http://hl7.org/fhir/StructureDefinition/{resourceType}";
            var innerResolver = new StructureDefinitionSchemaResolver(schemaProvider);
            var schemaResolver = new CachedValidationSchemaResolver(innerResolver);
            var schema = schemaResolver.GetSchema(canonicalUrl);

            if (schema == null)
            {
                return ValidationResult.Failure(
                    new ValidationIssue(
                        code: "schema-not-found",
                        path: "$",
                        message: $"Validation schema not found for resource type '{resourceType}'",
                        severity: IssueSeverity.Error));
            }

            // Perform validation
            var validationSettings = settings ?? new ValidationSettings { Depth = ValidationDepth.Spec };
            var state = new ValidationState();
            var element = sourceNode.ToElement(schemaProvider);
            var result = schema.Validate(element, validationSettings, state);

            return result;
        }
        catch (Exception ex)
        {
            return ValidationResult.Failure(
                new ValidationIssue(
                    code: "validation-error",
                    path: "$",
                    message: $"Validation error: {ex.Message}",
                    severity: IssueSeverity.Fatal));
        }
    }

    /// <summary>
    /// Displays validation results to the console with formatting.
    /// </summary>
    public static void DisplayResults(ValidationResult result, string resourceType, string fhirVersion, bool verbose = false)
    {
        Console.WriteLine();
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine($"  FHIR Validation Results ({fhirVersion.ToUpperInvariant()})");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine($"  Resource Type: {resourceType}");
        Console.WriteLine($"  Status: {(result.IsValid ? "✓ VALID" : "✗ INVALID")}");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        // Count issues by severity
        var fatalCount = result.Issues.Count(i => i.Severity == IssueSeverity.Fatal);
        var errorCount = result.Issues.Count(i => i.Severity == IssueSeverity.Error);
        var warningCount = result.Issues.Count(i => i.Severity == IssueSeverity.Warning);
        var informationCount = result.Issues.Count(i => i.Severity == IssueSeverity.Information);

        Console.WriteLine("Issue Summary:");
        Console.WriteLine($"  Fatal:       {fatalCount,4}");
        Console.WriteLine($"  Error:       {errorCount,4}");
        Console.WriteLine($"  Warning:     {warningCount,4}");
        Console.WriteLine($"  Information: {informationCount,4}");
        Console.WriteLine($"  Total:       {result.Issues.Count,4}");
        Console.WriteLine();

        if (result.Issues.Any())
        {
            Console.WriteLine("───────────────────────────────────────────────────────────────");
            Console.WriteLine("Validation Issues:");
            Console.WriteLine("───────────────────────────────────────────────────────────────");
            Console.WriteLine();

            // Group by severity and optionally by path
            var issuesBySeverity = result.Issues
                .OrderBy(i => i.Severity)
                .ThenBy(i => i.Path);

            foreach (var issue in issuesBySeverity)
            {
                var severityIcon = issue.Severity switch
                {
                    IssueSeverity.Fatal => "💀",
                    IssueSeverity.Error => "❌",
                    IssueSeverity.Warning => "⚠️ ",
                    IssueSeverity.Information => "ℹ️ ",
                    _ => "  "
                };

                var severityText = issue.Severity.ToString().ToUpperInvariant().PadRight(11);

                Console.WriteLine($"{severityIcon} {severityText} @ {issue.Path}");
                Console.WriteLine($"   {issue.Message}");

                if (verbose && issue.Details?.Text != null && issue.Details.Text != issue.Message)
                {
                    Console.WriteLine($"   Details: {issue.Details.Text}");
                }

                Console.WriteLine();
            }
        }
        else
        {
            Console.WriteLine("✓ No validation issues found!");
            Console.WriteLine();
        }

        Console.WriteLine("═══════════════════════════════════════════════════════════════");
    }

    /// <summary>
    /// Gets a summary string of validation results suitable for inline display.
    /// </summary>
    public static string GetSummary(ValidationResult result)
    {
        if (result.IsValid)
        {
            return "✓ Valid";
        }

        var errorCount = result.Issues.Count(i => i.Severity == IssueSeverity.Error);
        var warningCount = result.Issues.Count(i => i.Severity == IssueSeverity.Warning);

        var parts = new List<string>();
        if (errorCount > 0)
            parts.Add($"{errorCount} error{(errorCount != 1 ? "s" : "")}");
        if (warningCount > 0)
            parts.Add($"{warningCount} warning{(warningCount != 1 ? "s" : "")}");

        return $"✗ Invalid: {string.Join(", ", parts)}";
    }
}
