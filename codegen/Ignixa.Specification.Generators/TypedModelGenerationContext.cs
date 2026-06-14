// <copyright file="TypedModelGenerationContext.cs" company="Ignixa Contributors">
//     Copyright (c) Ignixa Contributors. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using System.Text;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.CodeGen.Models;

namespace Ignixa.Specification.Generators;

/// <summary>
/// Mutable state shared across a single typed-model export run: the dedupe registries for
/// discriminator/value-set enums, the queue of nested backbone facades to emit, the set of
/// type names that resolve to a typed facade, and the running counters.
/// </summary>
internal sealed class GenerationContext
{
    private readonly DefinitionCollection _definitions;
    private readonly Dictionary<string, string> _enumFiles = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _valueSetEnumCodes = new(StringComparer.Ordinal);

    public GenerationContext(DefinitionCollection definitions, CSharpTypedModelConfig config, string ns)
    {
        _definitions = definitions;
        _ = config;
        Namespace = ns;
    }

    public DefinitionCollection Definitions => _definitions;

    public string Namespace { get; }

    public int ChoiceEnumCount { get; private set; }

    public int ValueSetEnumCount { get; private set; }

    public IReadOnlyDictionary<string, string> EnumFiles => _enumFiles;

    /// <summary>
    /// Records a choice discriminator enum for emission to its own file. The supplied declaration is
    /// wrapped in the standard auto-generated file template. Idempotent per enum name.
    /// </summary>
    public void AddChoiceEnum(string enumName, string enumDeclaration)
    {
        if (_enumFiles.TryAdd(enumName, WrapEnumFile(Namespace, enumDeclaration)))
        {
            ChoiceEnumCount++;
        }
    }

    /// <summary>
    /// Resolves a value-set enum name for a required code-bound element (Enhancement B). Returns
    /// null (fall back to string) when the value set cannot be expanded to a finite code set or when
    /// the derived name collides with an existing enum that has a different code set.
    /// </summary>
    public string? TryResolveValueSetEnum(ElementDefinition element, string elementContext)
    {
        string? valueSetUrl = element.Binding?.ValueSet;
        if (string.IsNullOrEmpty(valueSetUrl))
        {
            return null;
        }

        string enumName = DeriveEnumName(valueSetUrl);

        var concepts = ExpandConcepts(valueSetUrl);
        if (concepts.Count == 0)
        {
            // Unexpandable / huge value set (e.g. all-languages): keep the element as string.
            return null;
        }

        // Codes that cannot produce a valid, unique C# identifier (e.g. symbol-only quantity
        // comparators with no friendly mapping) make the enum unusable: fall back to string.
        var memberNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var concept in concepts)
        {
            string member = CodeToEnumMember(concept.Code);
            if (!IsValidIdentifier(member))
            {
                Console.WriteLine($"  ! Value-set '{enumName}' for {elementContext} has code '{concept.Code}' with no valid C# member name; falling back to string");
                return null;
            }

            memberNames.Add(member);
        }

        var codeSet = new HashSet<string>(concepts.Select(c => c.Code), StringComparer.Ordinal);

        if (_valueSetEnumCodes.TryGetValue(enumName, out var existingCodes))
        {
            if (existingCodes.SetEquals(codeSet))
            {
                return enumName;
            }

            Console.WriteLine($"  ! Value-set enum name collision: '{enumName}' for {elementContext} ({valueSetUrl}) has a different code set than the first binding; falling back to string");
            return null;
        }

        _enumFiles[enumName] = RenderValueSetEnum(Namespace, enumName, valueSetUrl, concepts);
        _valueSetEnumCodes[enumName] = codeSet;
        ValueSetEnumCount++;
        return enumName;
    }

    private List<(string Code, string? Display, string? System)> ExpandConcepts(string valueSetUrl)
    {
        ValueSet? vs;
        try
        {
            vs = _definitions.ExpandVs(valueSetUrl).Result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ! Failed to expand value set {valueSetUrl}: {ex.Message}");
            return [];
        }

        if (vs?.Expansion?.Contains is { Count: > 0 } contains)
        {
            return contains
                .Where(c => !string.IsNullOrEmpty(c.Code))
                .Select(c => (c.Code, (string?)c.Display, (string?)c.System))
                .ToList();
        }

        return [];
    }

    private static string DeriveEnumName(string valueSetUrl)
    {
        string url = valueSetUrl;
        int pipe = url.IndexOf('|');
        if (pipe >= 0)
        {
            url = url[..pipe];
        }

        string lastSegment = url.Split('/').LastOrDefault(s => !string.IsNullOrEmpty(s)) ?? url;
        return ToPascalCase(lastSegment);
    }

    private static string WrapEnumFile(string ns, string enumDeclaration)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("// -------------------------------------------------------------------------------------------------");
        sb.AppendLine("// Copyright (c) Ignixa Contributors. All rights reserved.");
        sb.AppendLine("// Licensed under the MIT License. See LICENSE in the repo root for license information.");
        sb.AppendLine("// -------------------------------------------------------------------------------------------------");
        sb.AppendLine();
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("using Ignixa.Serialization;");
        sb.AppendLine();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();
        sb.Append(enumDeclaration);
        return sb.ToString();
    }

    private static string RenderValueSetEnum(string ns, string enumName, string valueSetUrl, List<(string Code, string? Display, string? System)> concepts)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"// Generated from FHIR ValueSet: {valueSetUrl}");
        sb.AppendLine($"public enum {enumName}");
        sb.AppendLine("{");

        var seenMembers = new HashSet<string>(StringComparer.Ordinal);
        bool first = true;
        foreach (var (code, display, system) in concepts)
        {
            string member = CodeToEnumMember(code);
            if (!seenMembers.Add(member))
            {
                continue;
            }

            if (!first)
            {
                sb.AppendLine();
            }

            first = false;

            if (!string.IsNullOrEmpty(display) && display != code)
            {
                sb.AppendLine($"    /// <summary>{EscapeXmlComment(display)}</summary>");
            }

            string systemPart = !string.IsNullOrEmpty(system) ? $", \"{system}\"" : string.Empty;
            sb.AppendLine($"    [EnumLiteral(\"{code}\"{systemPart})]");
            sb.AppendLine($"    {member},");
        }

        sb.AppendLine("}");
        return WrapEnumFile(ns, sb.ToString());
    }

    private static string CodeToEnumMember(string code)
    {
        if (string.IsNullOrEmpty(code))
        {
            return "Unknown";
        }

        return code switch
        {
            "not-in" => "NotIn",
            "of-type" or "ofType" => "OfType",
            "<" => "LessThan",
            "<=" => "LessOrEqual",
            ">=" => "GreaterOrEqual",
            ">" => "GreaterThan",
            "=" => "Equal",
            "!=" => "NotEqual",
            _ => ToPascalCase(code),
        };
    }

    private static bool IsValidIdentifier(string value)
    {
        if (string.IsNullOrEmpty(value) || !(char.IsLetter(value[0]) || value[0] == '_'))
        {
            return false;
        }

        foreach (char c in value)
        {
            if (!(char.IsLetterOrDigit(c) || c == '_'))
            {
                return false;
            }
        }

        return true;
    }

    private static string ToPascalCase(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var parts = value.Split(['-', '_'], StringSplitOptions.RemoveEmptyEntries);
        var sb = new StringBuilder();
        foreach (var part in parts)
        {
            sb.Append(char.ToUpperInvariant(part[0]));
            if (part.Length > 1)
            {
                sb.Append(part[1..]);
            }
        }

        return sb.Length == 0 ? value : sb.ToString();
    }

    private static string EscapeXmlComment(string text)
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
