// <copyright file="CSharpValueSetProviderLanguage.cs" company="Ignixa Contributors">
//     Copyright (c) Ignixa Contributors. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using System.Resources;
using System.Text;
using System.Text.Json;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.CodeGen.Language;
using Microsoft.Health.Fhir.CodeGen.Models;
using Microsoft.Health.Fhir.CodeGenCommon.Packaging;

namespace Ignixa.Specification.Generators;

/// <summary>
/// Custom ILanguage implementation for generating C# IValueSetProvider
/// implementations for each FHIR version with built-in ValueSets.
/// This generator extracts full code information (system, code, display) from ValueSet definitions.
/// </summary>
public sealed class CSharpValueSetProviderLanguage : ILanguage
{
    /// <summary>Gets the language name.</summary>
    public string Name => "CSharpValueSetProvider";

    /// <summary>Gets the configuration type.</summary>
    public Type ConfigType => typeof(CSharpValueSetProviderConfig);

    /// <summary>Gets the FHIR primitive type map (not used for this generator).</summary>
    public Dictionary<string, string> FhirPrimitiveTypeMap => new();

    /// <summary>Gets a value indicating whether this language is idempotent.</summary>
    public bool IsIdempotent => true;

    public void Export(object untypedConfig, DefinitionCollection definitions)
    {
        if (untypedConfig is not CSharpValueSetProviderConfig config)
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

        // Class name prefix: STU3 uses "Stu3" for consistency with existing patterns
        string classPrefix = fhirVersion == "STU3" ? "Stu3" : fhirVersion;

        Console.WriteLine($"Loaded {definitions.ValueSetsByVersionedUrl.Count} ValueSets");
        Console.WriteLine($"Loaded {definitions.CodeSystemsByUrl.Count} CodeSystems");
        Console.WriteLine();

        // Create output directory
        Directory.CreateDirectory(config.OutputDirectory);

        // Extract ALL ValueSets from the FHIR package for this version
        var valueSets = ExtractValueSetsWithFullCodes(definitions);

        Console.WriteLine($"Found {valueSets.Count} ValueSets with codes in {fhirVersion} package");

        // Generate the version-specific ValueSetProvider class
        string className = $"{classPrefix}ValueSetProvider";

        // Generate .resx file with valueset data
        GenerateResxFile(config.OutputDirectory, fhirVersion, className, valueSets);

        // Generate thin wrapper class that uses ResourceManager
        string classFile = Path.Combine(config.OutputDirectory, $"{className}.g.cs");
        var code = GenerateValueSetProviderThinWrapper(className, fhirVersion, config.Namespace, valueSets);
        File.WriteAllText(classFile, code, Encoding.UTF8);
        Console.WriteLine($"Generated: {className}.g.cs");

        // NOTE: The ValueSetProvider property should be added to the existing
        // {Version}CoreSchemaProvider.Partial.cs files manually, not generated.
        // This follows the same pattern as ReferenceMetadataProvider.

        Console.WriteLine();
    }

    /// <summary>
    /// Represents a FHIR code with full metadata.
    /// </summary>
    private sealed class CodeInfo
    {
        public required string System { get; init; }
        public required string Code { get; init; }
        public required string Display { get; init; }
    }

    /// <summary>
    /// DTO for JSON serialization of codes (compact property names to reduce size).
    /// </summary>
    private sealed record CodeDto(string s, string c, string d); // system, code, display

    /// <summary>
    /// Extracts ALL ValueSets from the FHIR package definitions with full code information.
    /// Each FHIR version package contains different ValueSets, so the output will be version-specific.
    /// </summary>
    private static Dictionary<string, List<CodeInfo>> ExtractValueSetsWithFullCodes(DefinitionCollection definitions)
    {
        var result = new Dictionary<string, List<CodeInfo>>(StringComparer.Ordinal);

        // Build a lookup for code systems by URL
        var codeSystemLookup = new Dictionary<string, CodeSystem>(StringComparer.Ordinal);
        foreach (var csEntry in definitions.CodeSystemsByUrl)
        {
            if (csEntry.Value?.Url is not null)
            {
                codeSystemLookup[csEntry.Value.Url] = csEntry.Value;
            }
        }

        // Get all ValueSet definitions from the package
        foreach (var vsEntry in definitions.ValueSetsByVersionedUrl)
        {
            var vs = vsEntry.Value;
            if (vs?.Url is null)
            {
                continue;
            }

            // Extract codes from the ValueSet
            var codes = ExtractCodesFromValueSet(vs, codeSystemLookup);
            if (codes.Count > 0)
            {
                result[vs.Url] = codes;
            }
        }

        return result;
    }

    /// <summary>
    /// Extracts individual codes with full metadata from a ValueSet expansion or compose.
    /// </summary>
    private static List<CodeInfo> ExtractCodesFromValueSet(ValueSet vs, Dictionary<string, CodeSystem> codeSystemLookup)
    {
        var codes = new List<CodeInfo>();
        var seenCodes = new HashSet<string>(StringComparer.Ordinal); // Track code+system to avoid duplicates

        // Try to get codes from expansion first (most complete)
        if (vs.Expansion?.Contains is not null)
        {
            foreach (var component in vs.Expansion.Contains)
            {
                if (string.IsNullOrEmpty(component.Code))
                {
                    continue;
                }

                string system = component.System ?? string.Empty;
                string display = component.Display ?? component.Code;
                string key = $"{system}|{component.Code}";

                if (seenCodes.Add(key))
                {
                    codes.Add(new CodeInfo
                    {
                        System = system,
                        Code = component.Code,
                        Display = display
                    });
                }
            }
        }

        // If no expansion, try compose
        if (codes.Count == 0 && vs.Compose?.Include is not null)
        {
            foreach (var include in vs.Compose.Include)
            {
                string system = include.System ?? string.Empty;

                // If explicit concepts are included
                if (include.Concept is not null)
                {
                    foreach (var concept in include.Concept)
                    {
                        if (string.IsNullOrEmpty(concept.Code))
                        {
                            continue;
                        }

                        string display = concept.Display ?? concept.Code;
                        string key = $"{system}|{concept.Code}";

                        if (seenCodes.Add(key))
                        {
                            codes.Add(new CodeInfo
                            {
                                System = system,
                                Code = concept.Code,
                                Display = display
                            });
                        }
                    }
                }
                // If no explicit concepts, try to get all codes from the CodeSystem
                else if (!string.IsNullOrEmpty(system) && codeSystemLookup.TryGetValue(system, out var cs))
                {
                    var csCodes = ExtractCodesFromCodeSystem(cs, system);
                    foreach (var code in csCodes)
                    {
                        string key = $"{code.System}|{code.Code}";
                        if (seenCodes.Add(key))
                        {
                            codes.Add(code);
                        }
                    }
                }
            }
        }

        return codes;
    }

    /// <summary>
    /// Extracts all codes from a CodeSystem definition.
    /// </summary>
    private static List<CodeInfo> ExtractCodesFromCodeSystem(CodeSystem cs, string system)
    {
        var codes = new List<CodeInfo>();

        if (cs.Concept is null)
        {
            return codes;
        }

        void ExtractConceptsRecursively(IEnumerable<CodeSystem.ConceptDefinitionComponent> concepts)
        {
            foreach (var concept in concepts)
            {
                if (!string.IsNullOrEmpty(concept.Code))
                {
                    codes.Add(new CodeInfo
                    {
                        System = system,
                        Code = concept.Code,
                        Display = concept.Display ?? concept.Code
                    });
                }

                // Recursively extract nested concepts
                if (concept.Concept is not null)
                {
                    ExtractConceptsRecursively(concept.Concept);
                }
            }
        }

        ExtractConceptsRecursively(cs.Concept);
        return codes;
    }

    /// <summary>
    /// Escapes a ValueSet URL for use as a .resx resource key.
    /// Replaces special characters that are invalid in resource names.
    /// </summary>
    private static string EscapeUrlForResourceKey(string url)
    {
        return url
            .Replace("://", "___")
            .Replace("/", "_")
            .Replace(":", "_")
            .Replace("|", "_")
            .Replace("?", "_")
            .Replace("#", "_")
            .Replace("[", "_")
            .Replace("]", "_")
            .Replace("@", "_")
            .Replace("!", "_")
            .Replace("$", "_")
            .Replace("&", "_")
            .Replace("'", "_")
            .Replace("(", "_")
            .Replace(")", "_")
            .Replace("*", "_")
            .Replace("+", "_")
            .Replace(",", "_")
            .Replace(";", "_")
            .Replace("=", "_")
            .Replace(" ", "_")
            .Replace("%", "_");
    }

    /// <summary>
    /// Generates a .resx file containing all valuesets as JSON strings.
    /// We manually generate the XML format to avoid dependencies on System.Resources.Extensions.
    /// </summary>
    private static void GenerateResxFile(
        string outputDir,
        string fhirVersion,
        string className,
        Dictionary<string, List<CodeInfo>> valueSets)
    {
        var resourcesDir = Path.Combine(outputDir, "Resources");
        Directory.CreateDirectory(resourcesDir);

        var resxPath = Path.Combine(resourcesDir, $"{className}Resources.resx");

        Console.WriteLine($"Generating .resx file: {resxPath}");

        using var writer = new StreamWriter(resxPath, false, Encoding.UTF8);

        // Write .resx XML header
        writer.WriteLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        writer.WriteLine("<root>");
        writer.WriteLine("  <resheader name=\"resmimetype\">");
        writer.WriteLine("    <value>text/microsoft-resx</value>");
        writer.WriteLine("  </resheader>");
        writer.WriteLine("  <resheader name=\"version\">");
        writer.WriteLine("    <value>2.0</value>");
        writer.WriteLine("  </resheader>");
        writer.WriteLine("  <resheader name=\"reader\">");
        writer.WriteLine("    <value>System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value>");
        writer.WriteLine("  </resheader>");
        writer.WriteLine("  <resheader name=\"writer\">");
        writer.WriteLine("    <value>System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value>");
        writer.WriteLine("  </resheader>");

        foreach (var (url, codes) in valueSets.OrderBy(kv => kv.Key))
        {
            // Convert codes to DTO format (compact JSON)
            var codeDtos = codes.Select(c => new CodeDto(c.System, c.Code, c.Display)).ToArray();

            // Serialize to compact JSON (no indentation)
            var json = JsonSerializer.Serialize(codeDtos, new JsonSerializerOptions
            {
                WriteIndented = false
            });

            // Escape URL for use as resource key
            var key = EscapeUrlForResourceKey(url);

            // XML-escape the JSON value
            var xmlEscapedJson = System.Security.SecurityElement.Escape(json);

            // Write data element
            writer.WriteLine($"  <data name=\"{key}\" xml:space=\"preserve\">");
            writer.WriteLine($"    <value>{xmlEscapedJson}</value>");
            writer.WriteLine("  </data>");
        }

        writer.WriteLine("</root>");

        Console.WriteLine($"Added {valueSets.Count} valuesets to {className}Resources.resx");
    }

    /// <summary>
    /// Generates a thin wrapper ValueSetProvider that loads data from .resx resources.
    /// </summary>
    private static string GenerateValueSetProviderThinWrapper(
        string className,
        string fhirVersion,
        string namespaceName,
        Dictionary<string, List<CodeInfo>> valueSets)
    {
        var sb = new StringBuilder();

        // Header
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine($"// <copyright file=\"{className}.g.cs\" company=\"Ignixa Contributors\">");
        sb.AppendLine("//     Copyright (c) Ignixa Contributors. All rights reserved.");
        sb.AppendLine("//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.");
        sb.AppendLine("// </copyright>");
        sb.AppendLine();
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Concurrent;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.Linq;");
        sb.AppendLine("using System.Resources;");
        sb.AppendLine("using System.Text.Json;");
        sb.AppendLine("using Ignixa.Abstractions;");
        sb.AppendLine();
        sb.AppendLine($"namespace {namespaceName};");
        sb.AppendLine();
        sb.AppendLine("/// <summary>");
        sb.AppendLine($"/// Pre-generated ValueSet provider for FHIR {fhirVersion}.");
        sb.AppendLine("/// Provides efficient lookup of ValueSet codes with full metadata (system, code, display).");
        sb.AppendLine("/// Data is loaded lazily from embedded .resx resources to minimize memory footprint.");
        sb.AppendLine("/// </summary>");
        sb.AppendLine("[CLSCompliant(false)]");
        sb.AppendLine($"public sealed class {className} : IValueSetProvider");
        sb.AppendLine("{");

        // DTO record
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// DTO for JSON deserialization of codes (compact property names).");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    private sealed record CodeDto(string s, string c, string d); // system, code, display");
        sb.AppendLine();

        // ResourceManager field
        sb.AppendLine("    private static readonly ResourceManager _resources = new(");
        sb.AppendLine($"        \"{namespaceName}.Resources.{className}Resources\",");
        sb.AppendLine($"        typeof({className}).Assembly);");
        sb.AppendLine();

        // Cache
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// Cache of loaded valuesets (lazy loading on first access).");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    private static readonly ConcurrentDictionary<string, FhirCode[]> _cache = new(StringComparer.Ordinal);");
        sb.AppendLine();

        // Validation sets cache
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// Optimized lookup sets for code validation (lazily initialized on first access).");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    private static readonly ConcurrentDictionary<string, HashSet<string>?> _validationSets = new(StringComparer.Ordinal);");
        sb.AppendLine();

        // GetCodes method
        sb.AppendLine("    /// <inheritdoc/>");
        sb.AppendLine("    public IReadOnlyList<FhirCode>? GetCodes(string valueSetUrl)");
        sb.AppendLine("    {");
        sb.AppendLine("        ArgumentNullException.ThrowIfNull(valueSetUrl);");
        sb.AppendLine("        var normalized = NormalizeUrl(valueSetUrl);");
        sb.AppendLine();
        sb.AppendLine("        // Fast path: already cached");
        sb.AppendLine("        if (_cache.TryGetValue(normalized, out var codes))");
        sb.AppendLine("            return codes;");
        sb.AppendLine();
        sb.AppendLine("        // Load from .resx (lazy, on-demand)");
        sb.AppendLine("        var key = EscapeUrl(normalized);");
        sb.AppendLine("        var json = _resources.GetString(key); // ResourceManager handles caching");
        sb.AppendLine("        if (json == null) return null;");
        sb.AppendLine();
        sb.AppendLine("        // Deserialize and cache");
        sb.AppendLine("        var codeDtos = JsonSerializer.Deserialize<CodeDto[]>(json);");
        sb.AppendLine("        if (codeDtos == null) return null;");
        sb.AppendLine();
        sb.AppendLine("        codes = codeDtos.Select(dto => new FhirCode(dto.s, dto.c, dto.d)).ToArray();");
        sb.AppendLine("        _cache.TryAdd(normalized, codes);");
        sb.AppendLine();
        sb.AppendLine("        return codes;");
        sb.AppendLine("    }");
        sb.AppendLine();

        // IsKnownValueSet method
        sb.AppendLine("    /// <inheritdoc/>");
        sb.AppendLine("    public bool IsKnownValueSet(string valueSetUrl)");
        sb.AppendLine("    {");
        sb.AppendLine("        ArgumentNullException.ThrowIfNull(valueSetUrl);");
        sb.AppendLine("        var normalized = NormalizeUrl(valueSetUrl);");
        sb.AppendLine("        var key = EscapeUrl(normalized);");
        sb.AppendLine("        return _resources.GetString(key) != null;");
        sb.AppendLine("    }");
        sb.AppendLine();

        // IsValidCode method
        sb.AppendLine("    /// <inheritdoc/>");
        sb.AppendLine("    public bool? IsValidCode(string valueSetUrl, string code)");
        sb.AppendLine("    {");
        sb.AppendLine("        ArgumentNullException.ThrowIfNull(valueSetUrl);");
        sb.AppendLine("        ArgumentNullException.ThrowIfNull(code);");
        sb.AppendLine();
        sb.AppendLine("        var normalized = NormalizeUrl(valueSetUrl);");
        sb.AppendLine();
        sb.AppendLine("        // Get or create validation set");
        sb.AppendLine("        var validCodes = _validationSets.GetOrAdd(normalized, url =>");
        sb.AppendLine("        {");
        sb.AppendLine("            var codes = GetCodes(url);");
        sb.AppendLine("            if (codes == null) return null;");
        sb.AppendLine("            return codes.Select(static c => c.Code).ToHashSet(StringComparer.Ordinal);");
        sb.AppendLine("        });");
        sb.AppendLine();
        sb.AppendLine("        return validCodes?.Contains(code);");
        sb.AppendLine("    }");
        sb.AppendLine();

        // EscapeUrl method
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// Escapes a URL for use as a .resx resource key.");
        sb.AppendLine("    /// Must match the escaping logic used during code generation.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    private static string EscapeUrl(string url)");
        sb.AppendLine("    {");
        sb.AppendLine("        return url");
        sb.AppendLine("            .Replace(\"://\", \"___\")");
        sb.AppendLine("            .Replace(\"/\", \"_\")");
        sb.AppendLine("            .Replace(\":\", \"_\")");
        sb.AppendLine("            .Replace(\"|\", \"_\")");
        sb.AppendLine("            .Replace(\"?\", \"_\")");
        sb.AppendLine("            .Replace(\"#\", \"_\")");
        sb.AppendLine("            .Replace(\"[\", \"_\")");
        sb.AppendLine("            .Replace(\"]\", \"_\")");
        sb.AppendLine("            .Replace(\"@\", \"_\")");
        sb.AppendLine("            .Replace(\"!\", \"_\")");
        sb.AppendLine("            .Replace(\"$\", \"_\")");
        sb.AppendLine("            .Replace(\"&\", \"_\")");
        sb.AppendLine("            .Replace(\"'\", \"_\")");
        sb.AppendLine("            .Replace(\"(\", \"_\")");
        sb.AppendLine("            .Replace(\")\", \"_\")");
        sb.AppendLine("            .Replace(\"*\", \"_\")");
        sb.AppendLine("            .Replace(\"+\", \"_\")");
        sb.AppendLine("            .Replace(\",\", \"_\")");
        sb.AppendLine("            .Replace(\";\", \"_\")");
        sb.AppendLine("            .Replace(\"=\", \"_\")");
        sb.AppendLine("            .Replace(\" \", \"_\")");
        sb.AppendLine("            .Replace(\"%\", \"_\");");
        sb.AppendLine("    }");
        sb.AppendLine();

        // NormalizeUrl method
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// Normalizes a ValueSet URL by removing version suffix after '|'.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    private static string NormalizeUrl(string url)");
        sb.AppendLine("    {");
        sb.AppendLine("        var pipeIndex = url.IndexOf('|', StringComparison.Ordinal);");
        sb.AppendLine("        return pipeIndex > 0 ? url[..pipeIndex] : url;");
        sb.AppendLine("    }");

        sb.AppendLine("}");

        return sb.ToString();
    }

    /// <summary>
    /// Generates the C# code for the version-specific ValueSetProvider.
    /// </summary>
    private static string GenerateValueSetProviderClass(
        string className,
        string fhirVersion,
        string namespaceName,
        Dictionary<string, List<CodeInfo>> valueSets)
    {
        var sb = new StringBuilder();

        // Extract all unique system URLs for the Constants class
        var uniqueSystems = new HashSet<string>(StringComparer.Ordinal);
        foreach (var codes in valueSets.Values)
        {
            foreach (var code in codes)
            {
                if (!string.IsNullOrEmpty(code.System))
                {
                    uniqueSystems.Add(code.System);
                }
            }
        }

        // Create a mapping from system URL to constant name
        var systemToConstant = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var system in uniqueSystems.OrderBy(s => s))
        {
            var constantName = GenerateConstantName(system);
            // Handle potential duplicates by appending a suffix
            var baseName = constantName;
            int suffix = 1;
            while (systemToConstant.Values.Contains(constantName))
            {
                constantName = $"{baseName}_{suffix++}";
            }
            systemToConstant[system] = constantName;
        }

        // Header
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine($"// <copyright file=\"{className}.g.cs\" company=\"Ignixa Contributors\">");
        sb.AppendLine("//     Copyright (c) Ignixa Contributors. All rights reserved.");
        sb.AppendLine("//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.");
        sb.AppendLine("// </copyright>");
        sb.AppendLine();
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.Linq;");
        sb.AppendLine("using Ignixa.Abstractions;");
        sb.AppendLine();
        sb.AppendLine($"namespace {namespaceName};");
        sb.AppendLine();
        sb.AppendLine("/// <summary>");
        sb.AppendLine($"/// Pre-generated ValueSet provider for FHIR {fhirVersion}.");
        sb.AppendLine("/// Provides efficient lookup of ValueSet codes with full metadata (system, code, display).");
        sb.AppendLine("/// </summary>");
        sb.AppendLine("[CLSCompliant(false)]");
        sb.AppendLine($"public sealed class {className} : IValueSetProvider");
        sb.AppendLine("{");

        // Generate the Constants class
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// System URL constants to reduce string duplication.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    private static class Constants");
        sb.AppendLine("    {");
        foreach (var (system, constantName) in systemToConstant.OrderBy(kv => kv.Value))
        {
            sb.AppendLine($"        public const string {constantName} = \"{EscapeString(system)}\";");
        }
        sb.AppendLine("    }");
        sb.AppendLine();

        // Generate the lazy-initialized valueSets dictionary using constants
        sb.AppendLine("    private static readonly Lazy<Dictionary<string, FhirCode[]>> _valueSetsLazy = new(BuildValueSets);");
        sb.AppendLine("    private static Dictionary<string, FhirCode[]> _valueSets => _valueSetsLazy.Value;");
        sb.AppendLine();

        // Generate the BuildValueSets method - use iterative approach to avoid stack overflow
        // Split into multiple helper methods to avoid method body size limits
        var vsCount = valueSets.Count;
        var methodsNeeded = (vsCount / 50) + 1; // 50 valuesets per method

        sb.AppendLine("    private static Dictionary<string, FhirCode[]> BuildValueSets()");
        sb.AppendLine("    {");
        sb.AppendLine("        var dict = new Dictionary<string, FhirCode[]>(StringComparer.Ordinal);");

        for (int methodIndex = 0; methodIndex < methodsNeeded; methodIndex++)
        {
            sb.AppendLine($"        AddValueSets{methodIndex}(dict);");
        }

        sb.AppendLine("        return dict;");
        sb.AppendLine("    }");
        sb.AppendLine();

        // Generate helper methods, each handling a subset of ValueSets
        int currentMethod = 0;
        int vsInCurrentMethod = 0;
        var orderedValueSets = valueSets.OrderBy(kv => kv.Key).ToList();

        for (int vsIndex = 0; vsIndex < orderedValueSets.Count; vsIndex++)
        {
            if (vsInCurrentMethod == 0)
            {
                sb.AppendLine($"    private static void AddValueSets{currentMethod}(Dictionary<string, FhirCode[]> dict)");
                sb.AppendLine("    {");
            }

            var (url, codes) = orderedValueSets[vsIndex];

            // Use List.Add approach for code arrays
            sb.AppendLine($"        var codes{vsIndex} = new List<FhirCode>();");
            foreach (var code in codes)
            {
                string systemRef = string.IsNullOrEmpty(code.System)
                    ? "string.Empty"
                    : $"Constants.{systemToConstant[code.System]}";
                sb.AppendLine($"        codes{vsIndex}.Add(new FhirCode({systemRef}, \"{EscapeString(code.Code)}\", \"{EscapeString(code.Display)}\"));");
            }
            sb.AppendLine($"        dict[\"{EscapeString(url)}\"] = codes{vsIndex}.ToArray();");
            sb.AppendLine();

            vsInCurrentMethod++;

            // Close current method and start new one if needed
            if (vsInCurrentMethod >= 50 || vsIndex == orderedValueSets.Count - 1)
            {
                sb.AppendLine("    }");
                sb.AppendLine();
                currentMethod++;
                vsInCurrentMethod = 0;
            }
        }

        // Generate the optimized validation sets (lazy initialized on-demand)
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// Optimized lookup sets for code validation (lazily initialized on first access).");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    private static readonly Dictionary<string, HashSet<string>> _validationSets = new(StringComparer.Ordinal);");
        sb.AppendLine();

        // GetCodes method
        sb.AppendLine("    /// <inheritdoc/>");
        sb.AppendLine("    public IReadOnlyList<FhirCode>? GetCodes(string valueSetUrl)");
        sb.AppendLine("    {");
        sb.AppendLine("        ArgumentNullException.ThrowIfNull(valueSetUrl);");
        sb.AppendLine("        var normalized = NormalizeUrl(valueSetUrl);");
        sb.AppendLine("        return _valueSets.TryGetValue(normalized, out var codes) ? codes : null;");
        sb.AppendLine("    }");
        sb.AppendLine();

        // IsKnownValueSet method
        sb.AppendLine("    /// <inheritdoc/>");
        sb.AppendLine("    public bool IsKnownValueSet(string valueSetUrl)");
        sb.AppendLine("    {");
        sb.AppendLine("        ArgumentNullException.ThrowIfNull(valueSetUrl);");
        sb.AppendLine("        return _valueSets.ContainsKey(NormalizeUrl(valueSetUrl));");
        sb.AppendLine("    }");
        sb.AppendLine();

        // IsValidCode method (with lazy initialization)
        sb.AppendLine("    /// <inheritdoc/>");
        sb.AppendLine("    public bool? IsValidCode(string valueSetUrl, string code)");
        sb.AppendLine("    {");
        sb.AppendLine("        ArgumentNullException.ThrowIfNull(valueSetUrl);");
        sb.AppendLine("        ArgumentNullException.ThrowIfNull(code);");
        sb.AppendLine();
        sb.AppendLine("        var normalized = NormalizeUrl(valueSetUrl);");
        sb.AppendLine();
        sb.AppendLine("        // Lazy initialize validation set for this valueSetUrl");
        sb.AppendLine("        if (!_validationSets.TryGetValue(normalized, out var validCodes))");
        sb.AppendLine("        {");
        sb.AppendLine("            // Check if we have this valueset");
        sb.AppendLine("            if (!_valueSets.TryGetValue(normalized, out var codes))");
        sb.AppendLine("            {");
        sb.AppendLine("                return null;");
        sb.AppendLine("            }");
        sb.AppendLine();
        sb.AppendLine("            // Build validation set on-demand");
        sb.AppendLine("            validCodes = codes.Select(static c => c.Code).ToHashSet(StringComparer.Ordinal);");
        sb.AppendLine("            _validationSets[normalized] = validCodes;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        return validCodes.Contains(code);");
        sb.AppendLine("    }");
        sb.AppendLine();

        // NormalizeUrl method (removes version suffix after |)
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// Normalizes a ValueSet URL by removing version suffix after '|'.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    /// <param name=\"url\">The URL to normalize.</param>");
        sb.AppendLine("    /// <returns>The normalized URL without version suffix.</returns>");
        sb.AppendLine("    private static string NormalizeUrl(string url)");
        sb.AppendLine("    {");
        sb.AppendLine("        var pipeIndex = url.IndexOf('|', StringComparison.Ordinal);");
        sb.AppendLine("        return pipeIndex > 0 ? url[..pipeIndex] : url;");
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

    /// <summary>
    /// Generates a valid C# constant name from a system URL.
    /// </summary>
    /// <param name="systemUrl">The system URL to convert.</param>
    /// <returns>A valid C# identifier for use as a constant name.</returns>
    private static string GenerateConstantName(string systemUrl)
    {
        // Extract meaningful parts from the URL
        // Examples:
        // - http://hl7.org/fhir/administrative-gender -> AdministrativeGender
        // - http://terminology.hl7.org/CodeSystem/v3-ActCode -> V3ActCode
        // - http://dicom.nema.org/resources/ontology/DCM -> DicomDcm

        // Remove common prefixes and extract the meaningful part
        var meaningful = systemUrl
            .Replace("http://hl7.org/fhir/", "")
            .Replace("https://hl7.org/fhir/", "")
            .Replace("http://terminology.hl7.org/CodeSystem/", "")
            .Replace("https://terminology.hl7.org/CodeSystem/", "")
            .Replace("http://terminology.hl7.org/", "")
            .Replace("https://terminology.hl7.org/", "")
            .Replace("http://dicom.nema.org/resources/ontology/", "Dicom")
            .Replace("https://dicom.nema.org/resources/ontology/", "Dicom")
            .Replace("http://snomed.info/sct", "Snomed")
            .Replace("http://loinc.org", "Loinc")
            .Replace("http://unitsofmeasure.org", "Ucum")
            .Replace("urn:iso:std:iso:", "Iso")
            .Replace("urn:ietf:bcp:47", "BcpLanguage")
            .Replace("urn:ietf:rfc:3986", "Uri")
            .Replace("urn:oid:", "Oid");

        // Convert to PascalCase
        var sb = new StringBuilder();
        bool capitalizeNext = true;

        foreach (char c in meaningful)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(capitalizeNext ? char.ToUpperInvariant(c) : c);
                capitalizeNext = false;
            }
            else
            {
                // Non-alphanumeric characters signal the start of a new word
                capitalizeNext = true;
            }
        }

        var result = sb.ToString();

        // Ensure it starts with a letter (prepend underscore if it starts with a digit)
        if (result.Length > 0 && char.IsDigit(result[0]))
        {
            result = "_" + result;
        }

        // Ensure it's not empty
        if (string.IsNullOrEmpty(result))
        {
            result = "Unknown";
        }

        return result;
    }
}

/// <summary>
/// Configuration for CSharpValueSetProviderLanguage.
/// </summary>
public sealed class CSharpValueSetProviderConfig
{
    /// <summary>Gets or sets the output directory for generated files.</summary>
    public string OutputDirectory { get; set; } = string.Empty;

    /// <summary>Gets or sets the namespace for generated code.</summary>
    public string Namespace { get; set; } = "Ignixa.Specification.Generated";
}
