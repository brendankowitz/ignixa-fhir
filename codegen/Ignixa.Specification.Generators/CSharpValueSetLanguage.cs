// <copyright file="CSharpValueSetLanguage.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using System.Text;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.CodeGen.Language;
using Microsoft.Health.Fhir.CodeGen.Models;
using Microsoft.Health.Fhir.CodeGenCommon.Packaging;

namespace Ignixa.Specification.Generators;

/// <summary>
/// Custom ILanguage implementation for generating C# ValueSet enums from FHIR ValueSet definitions.
/// Generates normative ValueSets as single-version files, version-specific for trial-use ValueSets.
/// </summary>
public sealed class CSharpValueSetLanguage : ILanguage
{
    /// <summary>Gets the language name.</summary>
    public string Name => "CSharpValueSet";

    /// <summary>Gets the configuration type.</summary>
    public Type ConfigType => typeof(CSharpValueSetConfig);

    /// <summary>Gets the FHIR primitive type map (not used for this generator).</summary>
    public Dictionary<string, string> FhirPrimitiveTypeMap => new();

    /// <summary>Gets a value indicating whether this language is idempotent.</summary>
    public bool IsIdempotent => true;

    /// <summary>
    /// ValueSets to generate (URL → C# enum name).
    /// These are the ValueSets currently used in the codebase.
    /// Note: audit-event-type and audit-event-sub-type are excluded because they include
    /// codes from multiple systems (DICOM + FHIR). Use TypeRestfulInteraction and
    /// SystemRestfulInteraction instead for FHIR audit event subtypes.
    /// </summary>
    private static readonly Dictionary<string, string> TargetValueSets = new()
    {
        ["http://hl7.org/fhir/ValueSet/search-param-type"] = "SearchParamType",
        ["http://hl7.org/fhir/ValueSet/compartment-type"] = "CompartmentType",
        ["http://hl7.org/fhir/ValueSet/search-modifier-code"] = "SearchModifierCode",
        ["http://hl7.org/fhir/ValueSet/search-comparator"] = "SearchComparator",
        ["http://hl7.org/fhir/ValueSet/search-entry-mode"] = "SearchEntryMode",
        ["http://hl7.org/fhir/ValueSet/operation-kind"] = "OperationTypes",
        ["http://hl7.org/fhir/ValueSet/resource-version-policy"] = "ResourceVersionPolicy",
        ["http://hl7.org/fhir/ValueSet/conditional-delete-status"] = "ConditionalDeleteStatus",
        ["http://hl7.org/fhir/ValueSet/special-values"] = "SpecialValues",
        ["http://hl7.org/fhir/ValueSet/system-restful-interaction"] = "SystemRestfulInteraction",
        ["http://hl7.org/fhir/ValueSet/type-restful-interaction"] = "TypeRestfulInteraction",
        ["http://hl7.org/fhir/ValueSet/currencies"] = "CurrencyValueSet",
    };

    public void Export(object untypedConfig, DefinitionCollection definitions)
    {
        if (untypedConfig is not CSharpValueSetConfig config)
        {
            throw new ArgumentException("Invalid configuration type", nameof(untypedConfig));
        }

        string fhirVersion = definitions.FhirSequence switch
        {
            FhirReleases.FhirSequenceCodes.R4 => "R4",
            FhirReleases.FhirSequenceCodes.R4B => "R4B",
            FhirReleases.FhirSequenceCodes.R5 => "R5",
            FhirReleases.FhirSequenceCodes.STU3 => "Stu3",
            _ => throw new ArgumentException($"Unsupported FHIR version: {definitions.FhirSequence}")
        };

        Console.WriteLine($"Loaded {definitions.ValueSetsByVersionedUrl.Count} ValueSets");
        Console.WriteLine($"Loaded {definitions.CodeSystemsByUrl.Count} CodeSystems");
        Console.WriteLine();

        // Create output directory
        Directory.CreateDirectory(config.OutputDirectory);

        int generatedCount = 0;

        // Generate each target ValueSet
        foreach (var (valueSetUrl, enumName) in TargetValueSets)
        {
            // Try to find the ValueSet
            var valueSet = definitions.ValueSetsByVersionedUrl.Values
                .FirstOrDefault(vs => vs.Url?.StartsWith(valueSetUrl, StringComparison.Ordinal) == true);

            if (valueSet == null)
            {
                Console.WriteLine($"⚠ ValueSet not found: {valueSetUrl}");
                continue;
            }

            Console.WriteLine($"Generating {enumName} from {valueSet.Url}");

            // Generate the enum
            string code = GenerateValueSetEnum(valueSet, enumName, fhirVersion, config.Namespace);

            // Write to file
            string fileName = $"{enumName}.cs";
            string filePath = Path.Combine(config.OutputDirectory, fileName);
            File.WriteAllText(filePath, code);

            generatedCount++;
        }

        Console.WriteLine();
        Console.WriteLine($"✓ Generated {generatedCount} ValueSet enums");
    }

    private string GenerateValueSetEnum(ValueSet valueSet, string enumName, string fhirVersion, string namespaceName)
    {
        var sb = new StringBuilder();

        // File header
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("// <copyright file=\"" + $"{enumName}.cs" + "\" company=\"Microsoft Corporation\">");
        sb.AppendLine("//     Copyright (c) Microsoft Corporation. All rights reserved.");
        sb.AppendLine("//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.");
        sb.AppendLine("// </copyright>");
        sb.AppendLine();
        sb.AppendLine($"// Generated from FHIR {fhirVersion} ValueSet: {valueSet.Url}");
        sb.AppendLine();

        sb.AppendLine("using Ignixa.SourceNodeSerialization.Utility;");
        sb.AppendLine();
        sb.AppendLine($"namespace {namespaceName};");
        sb.AppendLine();

        // XML documentation
        string? description = valueSet.Description;
        if (!string.IsNullOrEmpty(description))
        {
            sb.AppendLine("/// <summary>");
            sb.AppendLine($"/// {EscapeXmlComment(description)}");
            sb.AppendLine("/// </summary>");
        }

        // Check if we need suppressions
        if (enumName == "SearchParamType")
        {
            sb.AppendLine("[System.Diagnostics.CodeAnalysis.SuppressMessage(\"Naming\", \"CA1720:Identifiers should not contain type names\", Justification = \"Specified in the FHIR spec\")]");
        }

        sb.AppendLine($"public enum {enumName}");
        sb.AppendLine("{");

        // Get concepts from the ValueSet
        var concepts = GetValueSetConcepts(valueSet);

        bool isFirst = true;
        foreach (var (code, display, system) in concepts)
        {
            if (!isFirst)
            {
                sb.AppendLine();
            }

            isFirst = false;

            // Add display as XML comment if available and different from code
            if (!string.IsNullOrEmpty(display) && display != code)
            {
                sb.AppendLine($"    /// <summary>{EscapeXmlComment(display)}</summary>");
            }

            // Generate EnumLiteral attribute
            string systemPart = !string.IsNullOrEmpty(system) ? $", \"{system}\"" : string.Empty;
            sb.AppendLine($"    [EnumLiteral(\"{code}\"{systemPart})]");

            // Generate enum member name
            string memberName = CodeToEnumMember(code);
            sb.Append($"    {memberName},");
        }

        sb.AppendLine();
        sb.AppendLine("}");

        return sb.ToString();
    }

    private List<(string Code, string? Display, string? System)> GetValueSetConcepts(ValueSet valueSet)
    {
        var concepts = new List<(string, string?, string?)>();

        // Try to get from compose.include
        if (valueSet.Compose?.Include != null)
        {
            foreach (var include in valueSet.Compose.Include)
            {
                string? system = include.System;

                if (include.Concept != null)
                {
                    foreach (var concept in include.Concept)
                    {
                        concepts.Add((concept.Code, concept.Display, system));
                    }
                }
            }
        }

        // If no concepts from compose, try expansion
        if (concepts.Count == 0 && valueSet.Expansion?.Contains != null)
        {
            foreach (var contains in valueSet.Expansion.Contains)
            {
                concepts.Add((contains.Code, contains.Display, contains.System));
            }
        }

        return concepts.OrderBy(c => c.Item1).ToList();
    }

    private string CodeToEnumMember(string code)
    {
        // Convert FHIR code to valid C# enum member name
        if (string.IsNullOrEmpty(code))
        {
            return "Unknown";
        }

        // Handle special cases
        return code switch
        {
            "not-in" => "NotIn",
            "of-type" or "ofType" => "OfType",
            _ => ToPascalCase(code)
        };
    }

    private string ToPascalCase(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        // Split on hyphens and underscores
        var parts = input.Split(new[] { '-', '_' }, StringSplitOptions.RemoveEmptyEntries);

        var sb = new StringBuilder();
        foreach (var part in parts)
        {
            if (part.Length > 0)
            {
                sb.Append(char.ToUpperInvariant(part[0]));
                if (part.Length > 1)
                {
                    sb.Append(part.Substring(1));
                }
            }
        }

        return sb.ToString();
    }

    private string EscapeXmlComment(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        return text
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("\n", " ")
            .Replace("\r", " ");
    }
}

/// <summary>
/// Configuration for C# ValueSet generation.
/// </summary>
public sealed class CSharpValueSetConfig
{
    /// <summary>Gets or sets the output directory for generated files.</summary>
    public string OutputDirectory { get; set; } = string.Empty;

    /// <summary>Gets or sets the namespace for generated classes.</summary>
    public string Namespace { get; set; } = string.Empty;
}
