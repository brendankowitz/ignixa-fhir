// <copyright file="CSharpInvariantLanguage.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using System.Text;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.CodeGen.Language;
using Microsoft.Health.Fhir.CodeGen.Models;
using Microsoft.Health.Fhir.CodeGenCommon.Packaging;
using Microsoft.Health.Fhir.CodeGen.FhirExtensions;

namespace Ignixa.Specification.Generators;

/// <summary>
/// Custom ILanguage implementation for generating C# invariant providers.
/// Extracts FHIRPath constraint expressions from StructureDefinitions.
/// </summary>
public sealed class CSharpInvariantLanguage : ILanguage
{
    private const string LanguageName = "CSharpInvariant";

    /// <summary>Gets the language name.</summary>
    public string Name => LanguageName;

    /// <summary>Gets the configuration type.</summary>
    public Type ConfigType => typeof(CSharpInvariantConfig);

    /// <summary>Gets the FHIR primitive type map (not used for this generator).</summary>
    public Dictionary<string, string> FhirPrimitiveTypeMap => new();

    /// <summary>Gets a value indicating whether this language is idempotent.</summary>
    public bool IsIdempotent => true;

    /// <summary>Exports the invariant provider code.</summary>
    /// <param name="config">The configuration.</param>
    /// <param name="definitions">The definitions to export.</param>
    public void Export(object config, DefinitionCollection definitions)
    {
        if (config is not CSharpInvariantConfig invariantConfig)
        {
            throw new ArgumentException($"Configuration must be of type {nameof(CSharpInvariantConfig)}", nameof(config));
        }

        // Get the FHIR version
        string fhirVersion = definitions.FhirSequence switch
        {
            FhirReleases.FhirSequenceCodes.R4 => "R4",
            FhirReleases.FhirSequenceCodes.R4B => "R4B",
            FhirReleases.FhirSequenceCodes.R5 => "R5",
            FhirReleases.FhirSequenceCodes.STU3 => "Stu3",
            _ => throw new ArgumentException($"Unsupported FHIR version: {definitions.FhirSequence}")
        };

        // Create output directory if it doesn't exist
        string outputDir = Path.GetFullPath(invariantConfig.OutputDirectory);
        Directory.CreateDirectory(outputDir);

        // Extract constraints from all StructureDefinitions
        var constraints = ExtractConstraints(definitions);

        Console.WriteLine($"Extracted {constraints.Count} unique constraints from {definitions.ResourcesByName.Count} resources");

        // Generate the provider class
        string outputPath = Path.Combine(outputDir, $"{fhirVersion}InvariantProvider.g.cs");
        string code = GenerateProviderCode(fhirVersion, constraints, invariantConfig);

        File.WriteAllText(outputPath, code, Encoding.UTF8);

        Console.WriteLine($"Generated {outputPath}");
    }

    private Dictionary<string, ConstraintInfo> ExtractConstraints(DefinitionCollection definitions)
    {
        var constraints = new Dictionary<string, ConstraintInfo>();

        // Process all StructureDefinitions (resources, complex types, primitives)
        var allStructureDefinitions = new List<(string TypeName, StructureDefinition SD)>();

        foreach (var kvp in definitions.ResourcesByName)
        {
            allStructureDefinitions.Add((kvp.Key, kvp.Value));
        }

        foreach (var kvp in definitions.ComplexTypesByName)
        {
            allStructureDefinitions.Add((kvp.Key, kvp.Value));
        }

        foreach (var kvp in definitions.PrimitiveTypesByName)
        {
            allStructureDefinitions.Add((kvp.Key, kvp.Value));
        }

        // Extract constraints from each StructureDefinition
        foreach (var (typeName, sd) in allStructureDefinitions)
        {
            // Check for snapshot elements
            if (sd.Snapshot?.Element == null || !sd.Snapshot.Element.Any())
            {
                continue;
            }

            foreach (var element in sd.Snapshot.Element)
            {
                // Check if element has constraints
                if (element.Constraint == null || !element.Constraint.Any())
                {
                    continue;
                }

                foreach (var constraint in element.Constraint)
                {
                    string key = constraint.Key ?? string.Empty;
                    if (string.IsNullOrEmpty(key))
                    {
                        continue;
                    }

                    // Add or update constraint
                    if (!constraints.ContainsKey(key))
                    {
                        constraints[key] = new ConstraintInfo
                        {
                            Key = key,
                            Severity = constraint.Severity?.ToString() ?? "error",
                            Human = constraint.Human ?? string.Empty,
                            Expression = constraint.Expression ?? string.Empty,
                            Xpath = constraint.Xpath,
                            AppliesTo = new List<string> { typeName }
                        };
                    }
                    else
                    {
                        // Constraint already exists, add this type to AppliesTo list
                        if (!constraints[key].AppliesTo.Contains(typeName))
                        {
                            constraints[key].AppliesTo.Add(typeName);
                        }
                    }
                }
            }
        }

        return constraints;
    }

    private string GenerateProviderCode(string fhirVersion, Dictionary<string, ConstraintInfo> constraints, CSharpInvariantConfig config)
    {
        var sb = new StringBuilder();

        // File header
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("// <copyright file=\"" + $"{fhirVersion}InvariantProvider.g.cs" + "\" company=\"Microsoft Corporation\">");
        sb.AppendLine("//     Copyright (c) Microsoft Corporation. All rights reserved.");
        sb.AppendLine("//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.");
        sb.AppendLine("// </copyright>");
        sb.AppendLine();
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.Linq;");
        sb.AppendLine();
        sb.AppendLine($"namespace {config.Namespace};");
        sb.AppendLine();
        sb.AppendLine("/// <summary>");
        sb.AppendLine($"/// Provides FHIRPath constraint definitions from FHIR {fhirVersion} specification.");
        sb.AppendLine("/// Generated from StructureDefinition constraint elements.");
        sb.AppendLine("/// </summary>");
        sb.AppendLine($"public sealed class {fhirVersion}InvariantProvider");
        sb.AppendLine("{");

        // Generate constraints dictionary
        GenerateConstraintsDictionary(sb, constraints);

        // Generate GetConstraint method
        sb.AppendLine();
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// Gets a constraint by its key.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    public ConstraintDefinition? GetConstraint(string key)");
        sb.AppendLine("    {");
        sb.AppendLine("        return _constraints.TryGetValue(key, out var constraint) ? constraint : null;");
        sb.AppendLine("    }");

        // Generate GetConstraintsForType method
        sb.AppendLine();
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// Gets all constraints that apply to a specific resource type.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    public IReadOnlyList<ConstraintDefinition> GetConstraintsForType(string resourceType)");
        sb.AppendLine("    {");
        sb.AppendLine("        var result = new List<ConstraintDefinition>();");
        sb.AppendLine("        foreach (var constraint in _constraints.Values)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (constraint.AppliesTo.Contains(resourceType) ||");
        sb.AppendLine("                constraint.AppliesTo.Contains(\"Element\"))  // Element applies to all");
        sb.AppendLine("            {");
        sb.AppendLine("                result.Add(constraint);");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine("        return result;");
        sb.AppendLine("    }");

        // Generate GetAllConstraints method
        sb.AppendLine();
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// Gets all constraints.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    public IReadOnlyCollection<ConstraintDefinition> GetAllConstraints()");
        sb.AppendLine("    {");
        sb.AppendLine("        return _constraints.Values;");
        sb.AppendLine("    }");

        sb.AppendLine("}");
        sb.AppendLine();

        // Note: ConstraintDefinition and ConstraintSeverity types are now defined in
        // Ignixa.Specification/ConstraintDefinition.cs and ConstraintSeverity.cs (shared types)
        // They are no longer generated here to avoid duplication

        return sb.ToString();
    }

    private void GenerateConstraintsDictionary(StringBuilder sb, Dictionary<string, ConstraintInfo> constraints)
    {
        sb.AppendLine("    private static readonly Dictionary<string, ConstraintDefinition> _constraints = new()");
        sb.AppendLine("    {");

        // Sort constraints by key for consistent output
        var sortedConstraints = constraints.OrderBy(c => c.Key).ToList();

        foreach (var kvp in sortedConstraints)
        {
            var constraint = kvp.Value;

            // Parse severity
            string severityValue = constraint.Severity.ToLowerInvariant() switch
            {
                "error" => "ConstraintSeverity.Error",
                "warning" => "ConstraintSeverity.Warning",
                _ => "ConstraintSeverity.Error" // Default to error
            };

            // Sort AppliesTo for consistent output
            var appliesTo = constraint.AppliesTo.OrderBy(t => t).ToArray();

            sb.AppendLine($"        [\"{EscapeString(constraint.Key)}\"] = new ConstraintDefinition");
            sb.AppendLine("        {");
            sb.AppendLine($"            Key = \"{EscapeString(constraint.Key)}\",");
            sb.AppendLine($"            Severity = {severityValue},");
            sb.AppendLine($"            Human = \"{EscapeString(constraint.Human)}\",");
            sb.AppendLine($"            Expression = @\"{EscapeVerbatimString(constraint.Expression)}\",");

            if (!string.IsNullOrEmpty(constraint.Xpath))
            {
                sb.AppendLine($"            Xpath = @\"{EscapeVerbatimString(constraint.Xpath)}\",");
            }
            else
            {
                sb.AppendLine("            Xpath = null,");
            }

            // Generate AppliesTo array
            sb.Append("            AppliesTo = new[] { ");
            sb.Append(string.Join(", ", appliesTo.Select(t => $"\"{EscapeString(t)}\"")));
            sb.AppendLine(" }");

            sb.AppendLine("        },");
        }

        sb.AppendLine("    };");
    }

    private void GenerateConstraintDefinitionRecord(StringBuilder sb)
    {
        sb.AppendLine("/// <summary>");
        sb.AppendLine("/// Represents a FHIR constraint definition.");
        sb.AppendLine("/// </summary>");
        sb.AppendLine("public sealed record ConstraintDefinition");
        sb.AppendLine("{");
        sb.AppendLine("    public required string Key { get; init; }");
        sb.AppendLine("    public required ConstraintSeverity Severity { get; init; }");
        sb.AppendLine("    public required string Human { get; init; }");
        sb.AppendLine("    public required string Expression { get; init; }");
        sb.AppendLine("    public string? Xpath { get; init; }");
        sb.AppendLine("    public required IReadOnlyList<string> AppliesTo { get; init; }");
        sb.AppendLine("}");
        sb.AppendLine();
    }

    private void GenerateConstraintSeverityEnum(StringBuilder sb)
    {
        sb.AppendLine("/// <summary>");
        sb.AppendLine("/// Constraint severity levels.");
        sb.AppendLine("/// </summary>");
        sb.AppendLine("public enum ConstraintSeverity");
        sb.AppendLine("{");
        sb.AppendLine("    Error,");
        sb.AppendLine("    Warning");
        sb.AppendLine("}");
    }

    private string EscapeString(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "\\n");
    }

    private string EscapeVerbatimString(string value)
    {
        // For verbatim strings (@"..."), we only need to escape double quotes by doubling them
        return value.Replace("\"", "\"\"");
    }

    private record ConstraintInfo
    {
        public required string Key { get; init; }
        public required string Severity { get; init; }
        public required string Human { get; init; }
        public required string Expression { get; init; }
        public string? Xpath { get; init; }
        public required List<string> AppliesTo { get; init; }
    }
}

/// <summary>
/// Configuration for the C# Invariant Provider generator.
/// </summary>
public sealed class CSharpInvariantConfig
{
    /// <summary>Gets or sets the output directory for generated files.</summary>
    public string OutputDirectory { get; set; } = string.Empty;

    /// <summary>Gets or sets the namespace for generated classes.</summary>
    public string Namespace { get; set; } = "Ignixa.Specification.Generated";
}
