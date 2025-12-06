// <copyright file="CSharpInMemoryTerminologyLanguage.cs" company="Microsoft Corporation">
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
/// Custom ILanguage implementation for generating C# InMemoryTerminologyService
/// implementations for each FHIR version with built-in ValueSets.
/// </summary>
public sealed class CSharpInMemoryTerminologyLanguage : ILanguage
{
    /// <summary>Gets the language name.</summary>
    public string Name => "CSharpInMemoryTerminology";

    /// <summary>Gets the configuration type.</summary>
    public Type ConfigType => typeof(CSharpInMemoryTerminologyConfig);

    /// <summary>Gets the FHIR primitive type map (not used for this generator).</summary>
    public Dictionary<string, string> FhirPrimitiveTypeMap => new();

    /// <summary>Gets a value indicating whether this language is idempotent.</summary>
    public bool IsIdempotent => true;

    public void Export(object untypedConfig, DefinitionCollection definitions)
    {
        if (untypedConfig is not CSharpInMemoryTerminologyConfig config)
        {
            throw new ArgumentException("Invalid configuration type", nameof(untypedConfig));
        }

        string fhirVersion = definitions.FhirSequence switch
        {
            FhirReleases.FhirSequenceCodes.R4 => "R4",
            FhirReleases.FhirSequenceCodes.R4B => "R4B",
            FhirReleases.FhirSequenceCodes.R5 => "R5",
            FhirReleases.FhirSequenceCodes.R6 => "R6",
            FhirReleases.FhirSequenceCodes.STU3 => "STU3",
            _ => throw new ArgumentException($"Unsupported FHIR version: {definitions.FhirSequence}")
        };

        Console.WriteLine($"Loaded {definitions.ValueSetsByVersionedUrl.Count} ValueSets");
        Console.WriteLine($"Loaded {definitions.CodeSystemsByUrl.Count} CodeSystems");
        Console.WriteLine();

        // Create output directory
        Directory.CreateDirectory(config.OutputDirectory);

        // Extract ALL ValueSets from the FHIR package for this version
        var valueSets = ExtractValueSets(definitions);

        Console.WriteLine($"Found {valueSets.Count} ValueSets in {fhirVersion} package");

        // Generate the version-specific service class
        string className = $"{fhirVersion}InMemoryTerminologyService";
        string classFile = Path.Combine(config.OutputDirectory, $"{className}.g.cs");

        var code = GenerateServiceClass(className, fhirVersion, config.Namespace, valueSets);

        File.WriteAllText(classFile, code);
        Console.WriteLine($"Generated: {className}.cs");
        Console.WriteLine();
    }

    /// <summary>
    /// Extracts ALL ValueSets from the FHIR package definitions.
    /// Each FHIR version package contains different ValueSets, so the output will be version-specific.
    /// </summary>
    private static Dictionary<string, HashSet<string>> ExtractValueSets(DefinitionCollection definitions)
    {
        var result = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        // Get all ValueSet definitions from the package
        foreach (var vsEntry in definitions.ValueSetsByVersionedUrl)
        {
            var vs = vsEntry.Value;
            if (vs?.Url == null)
            {
                continue;
            }

            // Extract codes from the ValueSet
            var codes = ExtractCodesFromValueSet(vs);
            if (codes.Count > 0)
            {
                result[vs.Url] = codes;
            }
        }

        return result;
    }

    /// <summary>
    /// Extracts individual codes from a ValueSet expansion or compose.
    /// </summary>
    private static HashSet<string> ExtractCodesFromValueSet(ValueSet vs)
    {
        var codes = new HashSet<string>(StringComparer.Ordinal);

        // Try to get codes from expansion
        if (vs.Expansion?.Contains != null)
        {
            foreach (var component in vs.Expansion.Contains)
            {
                if (!string.IsNullOrEmpty(component.Code))
                {
                    codes.Add(component.Code);
                }
            }
        }

        // If no expansion, try compose
        if (codes.Count == 0 && vs.Compose?.Include != null)
        {
            foreach (var include in vs.Compose.Include)
            {
                if (include.Concept != null)
                {
                    foreach (var concept in include.Concept)
                    {
                        if (!string.IsNullOrEmpty(concept.Code))
                        {
                            codes.Add(concept.Code);
                        }
                    }
                }
            }
        }

        return codes;
    }

    /// <summary>
    /// Generates the C# code for the version-specific InMemoryTerminologyService.
    /// </summary>
    private static string GenerateServiceClass(
        string className,
        string fhirVersion,
        string namespaceName,
        Dictionary<string, HashSet<string>> valueSets)
    {
        var sb = new StringBuilder();

        // Header
        sb.AppendLine("// <copyright file=\"" + className + ".g.cs\" company=\"Ignixa Contributors\">");
        sb.AppendLine("//     Copyright (c) Ignixa Contributors. All rights reserved.");
        sb.AppendLine("//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.");
        sb.AppendLine("// </copyright>");
        sb.AppendLine();
        sb.AppendLine("using Ignixa.Validation.Services;");
        sb.AppendLine();
        sb.AppendLine("namespace Ignixa.Validation.Services;");
        sb.AppendLine();
        sb.AppendLine("/// <summary>");
        sb.AppendLine("/// In-memory terminology service for FHIR " + fhirVersion + " with hardcoded common ValueSets.");
        sb.AppendLine("/// Auto-generated from FHIR " + fhirVersion + " package.");
        sb.AppendLine("/// </summary>");
        sb.AppendLine("public partial class InMemoryTerminologyService");
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// Adds " + fhirVersion + "-specific ValueSets to the in-memory service.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    private static void AddFhir" + fhirVersion + "ValueSets(Dictionary<string, HashSet<string>> valueSets)");
        sb.AppendLine("    {");

        // Add each ValueSet
        foreach (var (url, codes) in valueSets)
        {
            sb.AppendLine("        // " + url);
            sb.Append("        valueSets[\"" + url + "\"] = new HashSet<string>([");

            var codeList = codes.OrderBy(c => c).ToList();
            for (int i = 0; i < codeList.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(", ");
                }

                sb.Append("\"" + EscapeString(codeList[i]) + "\"");
            }

            sb.AppendLine("], StringComparer.Ordinal);");
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    /// <summary>
    /// Escapes a string for inclusion in C# code.
    /// </summary>
    private static string EscapeString(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }
}

/// <summary>
/// Configuration for CSharpInMemoryTerminologyLanguage.
/// </summary>
public sealed class CSharpInMemoryTerminologyConfig
{
    /// <summary>Gets or sets the output directory for generated files.</summary>
    public string OutputDirectory { get; set; } = string.Empty;

    /// <summary>Gets or sets the namespace for generated code.</summary>
    public string Namespace { get; set; } = string.Empty;
}
