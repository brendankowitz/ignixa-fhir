// <copyright file="CSharpTypedModelLanguage.cs" company="Ignixa Contributors">
//     Copyright (c) Ignixa Contributors. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using System.Text;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.CodeGen.FhirExtensions;
using Microsoft.Health.Fhir.CodeGen.Language;
using Microsoft.Health.Fhir.CodeGen.Models;

namespace Ignixa.Specification.Generators;

/// <summary>
/// Generates strongly-typed POCO facades that are zero-copy views over System.Text.Json JsonObject
/// (ADR-2606), restructured into a shared base layer plus per-version subclasses (the shared-base
/// design). A multi-version pass classifies each type/element as Identical / Additive / Incompatible
/// and emits:
/// <list type="bullet">
/// <item>base facades (namespace <c>Ignixa.Models</c>) into the Serialization assembly,</item>
/// <item>per-version subclasses (namespace <c>Ignixa.Models.{Version}</c>) carrying the deltas,</item>
/// <item>per-version entry points + self-registration into the version-aware registry.</item>
/// </list>
/// </summary>
public sealed class CSharpTypedModelLanguage : ILanguage
{
    private const string LanguageName = "CSharpTypedModel";
    private const string BaseNamespace = "Ignixa.Models";

    private static readonly HashSet<string> PrimitiveTypeNames =
    [
        "boolean", "integer", "string", "decimal", "uri", "url", "canonical", "base64Binary",
        "instant", "date", "dateTime", "time", "code", "oid", "id", "markdown", "unsignedInt",
        "positiveInt", "uuid", "integer64",
    ];

    private static readonly HashSet<string> StringLikePrimitives =
    [
        "string", "code", "uri", "url", "canonical", "oid", "id", "markdown", "base64Binary",
        "date", "dateTime", "time", "instant", "uuid",
    ];

    /// <summary>
    /// Resource types that already have a hand-written facade in <c>Ignixa.Serialization</c> or
    /// <c>Ignixa.Application</c> (the <c>*JsonNode</c> classes the request pipeline depends on). The
    /// generated base layer (namespace <c>Ignixa.Models</c>) emits a facade named exactly after the
    /// resource (<c>Bundle</c>), so it would NOT collide with the hand-written <c>BundleJsonNode</c> at
    /// the CLR level. This guard is nonetheless ACTIVE: it stops the generator from emitting a base
    /// facade for any resource the server already models by hand, so the two never diverge and a
    /// consumer reaching for, say, <c>Bundle</c> handling is steered to the single server-critical
    /// implementation rather than a parallel generated one. The skip logs and continues (see
    /// <see cref="ExportMultiVersion"/>). Sourced from the hand-written resource facades under
    /// <c>src/Core/Ignixa.Serialization/Models/*JsonNode.cs</c> and
    /// <c>src/Application/**/Models/*JsonNode.cs</c>.
    /// </summary>
    private static readonly HashSet<string> ReservedBaseTypeNames = new(StringComparer.Ordinal)
    {
        "Bundle",
        "OperationOutcome",
        "Parameters",
        "Provenance",
        "SearchParameter",
        "CapabilityStatement",
        "StructureDefinition",
        "StructureMap",
        "ConceptMap",
        "Composition",
    };

    /// <summary>Gets the language name.</summary>
    public string Name => LanguageName;

    /// <summary>Gets the configuration type.</summary>
    public Type ConfigType => typeof(CSharpTypedModelConfig);

    /// <summary>Gets the FHIR primitive type map (not used for this generator).</summary>
    public Dictionary<string, string> FhirPrimitiveTypeMap => new();

    /// <summary>Gets a value indicating whether this language is idempotent.</summary>
    public bool IsIdempotent => true;

    /// <summary>
    /// Single-version entry point retained for the <see cref="ILanguage"/> contract. The shared-base
    /// design requires the multi-version pass; this throws to make accidental single-version use loud.
    /// </summary>
    public void Export(object config, DefinitionCollection definitions)
        => throw new NotSupportedException(
            "CSharpTypedModelLanguage requires the multi-version pass. Call ExportMultiVersion.");

    /// <summary>
    /// Multi-version export: classify across all supplied versions, then emit the shared base layer and
    /// every version's subclasses + entry points.
    /// </summary>
    /// <param name="config">The shared configuration (allow-lists, datatype closure flag).</param>
    /// <param name="versions">Ordered (version, definitions, output-dir) tuples for each target version.</param>
    /// <param name="baseOutputDir">
    /// Output directory for the shared base layer, i.e.
    /// <c>src/Core/Ignixa.Serialization/Generated/Models</c> (the per-version subclass directories come
    /// from each tuple's <c>OutputDir</c>).
    /// </param>
    public void ExportMultiVersion(
        CSharpTypedModelConfig config,
        IReadOnlyList<(string Version, DefinitionCollection Definitions, string OutputDir)> versions,
        string baseOutputDir)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(versions);

        var classifier = new TypedModelClassifier(
            versions.Select(v => (v.Version, v.Definitions)).ToList(),
            config);
        var classifications = classifier.Classify();

        Directory.CreateDirectory(baseOutputDir);
        foreach (var (_, _, outputDir) in versions)
        {
            Directory.CreateDirectory(outputDir);
        }

        // Definitions lookup per version for enum expansion + element re-fetch.
        var defsByVersion = versions.ToDictionary(v => v.Version, v => v.Definitions, StringComparer.Ordinal);
        var outputByVersion = versions.ToDictionary(v => v.Version, v => v.OutputDir, StringComparer.Ordinal);

        // --- Base layer -------------------------------------------------------------------------
        // Enums for base elements expand from the first version's definitions (Identical => same set).
        string primaryVersion = versions[0].Version;
        var baseEnumContext = new GenerationContext(defsByVersion[primaryVersion], config, BaseNamespace);
        WriteSupportFiles(baseOutputDir, BaseNamespace);

        int baseTypeCount = 0;
        int baseOnlyTypes = 0;
        int subclassedTypes = 0;
        int incompatibleElementCount = 0;
        var incompatibleSummary = new List<string>();

        foreach (var classification in classifications.Values.OrderBy(c => c.TypeName, StringComparer.Ordinal))
        {
            if (ReservedBaseTypeNames.Contains(classification.TypeName))
            {
                Console.WriteLine($"  ! Skipping reserved base type '{classification.TypeName}' (collides with a hand-written facade)");
                continue;
            }

            if (!classification.HasBaseType)
            {
                continue;
            }

            string code = GenerateBaseFacade(classification, baseEnumContext, defsByVersion, primaryVersion);
            File.WriteAllText(Path.Combine(baseOutputDir, $"{classification.TypeName}.cs"), code, Encoding.UTF8);
            baseTypeCount++;

            if (classification.SubclassVersions.Count == 0)
            {
                baseOnlyTypes++;
            }
            else
            {
                subclassedTypes++;
            }

            foreach (var element in classification.SubclassElements.Where(e => e.Bucket == SharingBucket.Incompatible))
            {
                incompatibleElementCount++;
                incompatibleSummary.Add($"{classification.TypeName}.{element.JsonName} [{string.Join(",", element.EmittedInVersions)}]");
            }
        }

        foreach (var (enumName, enumCode) in baseEnumContext.EnumFiles)
        {
            File.WriteAllText(Path.Combine(baseOutputDir, $"{enumName}.cs"), enumCode, Encoding.UTF8);
        }

        // Contexts whose downgrade/fallback counters feed the end-of-run summary.
        var summaryContexts = new List<(string Label, GenerationContext Context)>
        {
            (BaseNamespace, baseEnumContext),
        };

        // --- Per-version layers -----------------------------------------------------------------
        foreach (var (version, definitions, outputDir) in versions)
        {
            string versionNamespace = $"{BaseNamespace}.{version}";
            var versionEnumContext = new GenerationContext(definitions, config, versionNamespace);
            summaryContexts.Add((versionNamespace, versionEnumContext));

            int versionTypeCount = 0;
            var registrations = new List<(string ResourceType, string TypeName)>();

            foreach (var classification in classifications.Values.OrderBy(c => c.TypeName, StringComparer.Ordinal))
            {
                if (!classification.SubclassVersions.Contains(version))
                {
                    continue;
                }

                string code = GenerateVersionSubclass(classification, version, versionEnumContext, defsByVersion);
                File.WriteAllText(Path.Combine(outputDir, $"{classification.TypeName}.cs"), code, Encoding.UTF8);
                versionTypeCount++;

                if (classification.Kind is FacadeKind.DomainResource or FacadeKind.Resource)
                {
                    registrations.Add((classification.TypeName, classification.TypeName));
                }
            }

            // Also register resource types that are base-only (no subclass) for this version.
            foreach (var classification in classifications.Values)
            {
                if (classification.Kind is not (FacadeKind.DomainResource or FacadeKind.Resource))
                {
                    continue;
                }

                if (!classification.PresentInVersions.Contains(version))
                {
                    continue;
                }

                if (!classification.SubclassVersions.Contains(version))
                {
                    // base-only resource in this version: AsVersion should yield the base type.
                    registrations.Add((classification.TypeName, classification.TypeName));
                }
            }

            foreach (var (enumName, enumCode) in versionEnumContext.EnumFiles)
            {
                File.WriteAllText(Path.Combine(outputDir, $"{enumName}.cs"), enumCode, Encoding.UTF8);
            }

            File.WriteAllText(
                Path.Combine(outputDir, "_GlobalUsings.cs"),
                RenderGlobalUsings(version, classifications),
                Encoding.UTF8);

            File.WriteAllText(
                Path.Combine(outputDir, $"{version}.cs"),
                RenderEntryPoints(version, registrations),
                Encoding.UTF8);

            Console.WriteLine($"  {version}: emitted {versionTypeCount} subclasses, {versionEnumContext.EnumFiles.Count} enums, {registrations.Count} registrations");
        }

        Console.WriteLine();
        Console.WriteLine($"Base layer ({BaseNamespace}) -> {baseOutputDir}");
        Console.WriteLine($"  base types: {baseTypeCount} (base-only/identical: {baseOnlyTypes}, subclassed: {subclassedTypes}), base enums: {baseEnumContext.EnumFiles.Count}");
        Console.WriteLine($"  incompatible elements across versions: {incompatibleElementCount}");
        foreach (string summary in incompatibleSummary.OrderBy(x => x, StringComparer.Ordinal))
        {
            Console.WriteLine($"    incompatible: {summary}");
        }

        WriteDowngradeSummary(summaryContexts);
    }

    /// <summary>
    /// Prints every place a typed accessor was NOT produced, aggregated across the base layer and each
    /// version layer: value-set enum downgrades to <c>string</c>, raw <c>JsonNode</c> fallbacks (with
    /// the FHIR type code), and dropped choice <c>[x]</c> variants. Regenerating therefore surfaces the
    /// full coverage gap rather than burying it in interleaved per-element log lines.
    /// </summary>
    private static void WriteDowngradeSummary(IReadOnlyList<(string Label, GenerationContext Context)> contexts)
    {
        int valueSetTotal = contexts.Sum(c => c.Context.ValueSetDowngrades.Count);
        int fallbackTotal = contexts.Sum(c => c.Context.JsonNodeFallbacks.Count);
        int droppedChoiceTotal = contexts.Sum(c => c.Context.DroppedChoiceVariants.Count);

        Console.WriteLine();
        Console.WriteLine("Coverage downgrades (no typed accessor produced):");
        Console.WriteLine($"  value-set enum -> string: {valueSetTotal}");
        Console.WriteLine($"  JsonNode fallbacks: {fallbackTotal}");
        Console.WriteLine($"  dropped choice variants: {droppedChoiceTotal}");

        foreach (var (label, context) in contexts)
        {
            WriteDowngradeSection(label, "value-set->string", context.ValueSetDowngrades);
            WriteDowngradeSection(label, "JsonNode fallback", context.JsonNodeFallbacks);
            WriteDowngradeSection(label, "dropped choice variant", context.DroppedChoiceVariants);
        }
    }

    private static void WriteDowngradeSection(string label, string kind, IReadOnlyList<string> entries)
    {
        foreach (string entry in entries.OrderBy(x => x, StringComparer.Ordinal))
        {
            Console.WriteLine($"    [{label}] {kind}: {entry}");
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Base facade emission
    // ---------------------------------------------------------------------------------------------
    private string GenerateBaseFacade(
        TypeClassification classification,
        GenerationContext enumContext,
        Dictionary<string, DefinitionCollection> defsByVersion,
        string primaryVersion)
    {
        bool isResource = classification.Kind is FacadeKind.DomainResource or FacadeKind.Resource;
        var memberNames = new MemberNameAllocator(classification.TypeName, isResource);
        var body = new StringBuilder();

        foreach (var element in classification.BaseElements)
        {
            // Identical elements: use any present version's ElementDefinition (prefer primary).
            var facts = element.PerVersion.TryGetValue(primaryVersion, out var f)
                ? f
                : element.PerVersion.Values.First();

            EmitElement(enumContext, body, classification.TypeName, classification.TypeName, facts.StructureDefinition, facts.Element, isResource, memberNames);
        }

        string baseClass = classification.Kind switch
        {
            FacadeKind.DomainResource => "DomainResourceJsonNode",
            FacadeKind.Resource => "ResourceJsonNode",
            _ => "BaseJsonNode",
        };

        // A subclassed base type must NOT be sealed. Base resources are never sealed either: their
        // ctor is 'protected internal' (CS0628 on a sealed type) and a future version may subclass
        // them. Identical base datatypes can be sealed.
        bool sealedType = classification.SubclassVersions.Count == 0 && !isResource;

        return RenderClass(
            BaseNamespace,
            classification.TypeName,
            baseClass,
            isResource,
            sealedType,
            isVersionSubclass: false,
            additionalUsings: [],
            body: body.ToString(),
            kindLabel: isResource ? "resource" : "datatype");
    }

    // ---------------------------------------------------------------------------------------------
    // Version subclass emission
    // ---------------------------------------------------------------------------------------------
    private string GenerateVersionSubclass(
        TypeClassification classification,
        string version,
        GenerationContext enumContext,
        Dictionary<string, DefinitionCollection> defsByVersion)
    {
        bool isResource = classification.Kind is FacadeKind.DomainResource or FacadeKind.Resource;
        var memberNames = new MemberNameAllocator(classification.TypeName, isResource);

        // Seed the allocator with base members so subclass deltas never shadow (CS0108) a base accessor.
        foreach (var baseElement in classification.BaseElements)
        {
            memberNames.Reserve(ToPascalCase(StripChoiceMarker(baseElement.JsonName)));
        }

        var body = new StringBuilder();

        foreach (var element in classification.SubclassElements)
        {
            if (!element.EmittedInVersions.Contains(version))
            {
                continue;
            }

            var facts = element.PerVersion[version];
            EmitElement(enumContext, body, classification.TypeName, classification.TypeName, facts.StructureDefinition, facts.Element, isResource, memberNames);
        }

        // The subclass derives from the shared base type. It must be fully qualified: inside namespace
        // Ignixa.Models.{version}, the unqualified type name would bind to the subclass itself (CS0146).
        string baseClass = $"{BaseNamespace}.{classification.TypeName}";

        return RenderClass(
            $"{BaseNamespace}.{version}",
            classification.TypeName,
            baseClass,
            isResource,
            sealedType: true,
            isVersionSubclass: true,
            additionalUsings: [BaseNamespace],
            body: body.ToString(),
            kindLabel: $"{version} {(isResource ? "resource" : "datatype")}");
    }

    // ---------------------------------------------------------------------------------------------
    // Element emission (shared by base + version layers)
    // ---------------------------------------------------------------------------------------------
    private void EmitElement(
        GenerationContext context,
        StringBuilder body,
        string rootStructureName,
        string typeName,
        StructureDefinition sd,
        ElementDefinition element,
        bool isResource,
        MemberNameAllocator memberNames)
    {
        string jsonName = element.cgName();

        if (isResource && (jsonName is "id" or "meta" or "resourceType"))
        {
            return;
        }

        bool isChoice = element.Path.EndsWith("[x]", StringComparison.Ordinal);
        if (isChoice)
        {
            string baseName = StripChoiceMarker(jsonName);
            EmitChoice(context, body, typeName, baseName, element, memberNames);
            return;
        }

        EmitSimpleElement(context, body, rootStructureName, typeName, sd, jsonName, element, isResource, memberNames);
    }

    private void EmitSimpleElement(
        GenerationContext context,
        StringBuilder body,
        string rootStructureName,
        string typeName,
        StructureDefinition sd,
        string jsonName,
        ElementDefinition element,
        bool isResource,
        MemberNameAllocator memberNames)
    {
        bool isArray = element.cgIsArray();
        string fhirTypeCode = ResolveTypeCode(element);

        if (IsBackbone(element, sd))
        {
            // Backbone type name mirrors the classifier: parentType + PascalCase(jsonName).
            string backboneTypeName = typeName + ToPascalCase(jsonName);
            string complexName = memberNames.Allocate(ToPascalCase(jsonName));
            EmitComplexProperty(body, backboneTypeName, complexName, jsonName, isArray);
            return;
        }

        if (PrimitiveTypeNames.Contains(fhirTypeCode))
        {
            if (!isArray && element.cgHasCodes())
            {
                string? enumName = context.TryResolveValueSetEnum(element, $"{typeName}.{jsonName}");
                if (enumName is not null)
                {
                    EmitEnumAccessor(body, enumName, memberNames.Allocate(ToPascalCase(jsonName)), jsonName);
                    return;
                }
            }

            EmitPrimitive(body, memberNames.AllocatePrimitive(ToPascalCase(jsonName), fhirTypeCode, isArray), jsonName, fhirTypeCode, isArray);
            return;
        }

        if (IsTypedComplex(fhirTypeCode))
        {
            EmitComplexProperty(body, fhirTypeCode, memberNames.Allocate(ToPascalCase(jsonName)), jsonName, isArray);
            return;
        }

        context.RecordJsonNodeFallback($"{typeName}.{jsonName}", fhirTypeCode);
        EmitFallback(body, memberNames.Allocate(ToPascalCase(jsonName)), jsonName, fhirTypeCode, isArray);
    }

    /// <summary>
    /// True for type codes that resolve to a generated facade. Because the base layer carries a facade
    /// for every datatype/backbone present in any version (under <c>Ignixa.Models</c>), a complex
    /// property is emitted as the unqualified type name in both layers and resolves to the base type.
    /// Concrete FHIR datatypes/backbones are PascalCase; primitives and abstract bases are excluded by
    /// the caller's primitive check. We treat any non-primitive PascalCase code as typed-complex except
    /// the known abstract bases and <c>Reference</c>-style fallbacks handled below.
    /// </summary>
    private static bool IsTypedComplex(string typeCode)
    {
        if (string.IsNullOrEmpty(typeCode) || PrimitiveTypeNames.Contains(typeCode))
        {
            return false;
        }

        // Abstract / open types keep the JsonNode fallback (Resource, Element, etc.).
        if (AbstractOrFallbackTypes.Contains(typeCode))
        {
            return false;
        }

        // Reference is intentionally a fallback (it has no generated facade in this cut).
        return char.IsUpper(typeCode[0]);
    }

    private static readonly HashSet<string> AbstractOrFallbackTypes = new(StringComparer.Ordinal)
    {
        "Base", "Element", "BackboneElement", "BackboneType", "DataType",
        "PrimitiveType", "Resource", "DomainResource", "Reference",
    };

    private void EmitComplexProperty(StringBuilder body, string clrTypeName, string propertyName, string jsonName, bool isArray)
    {
        body.AppendLine("    [JsonIgnore]");

        if (isArray)
        {
            body.AppendLine($"    public MutableJsonList<{clrTypeName}> {propertyName} => GetListProperty<{clrTypeName}>(\"{jsonName}\");");
        }
        else
        {
            body.AppendLine($"    public {clrTypeName}? {propertyName}");
            body.AppendLine("    {");
            body.AppendLine($"        get => GetComplexProperty<{clrTypeName}>(\"{jsonName}\");");
            body.AppendLine($"        set => SetProperty(\"{jsonName}\", value?.MutableNode);");
            body.AppendLine("    }");
        }

        body.AppendLine();
    }

    private void EmitEnumAccessor(StringBuilder body, string enumName, string propertyName, string jsonName)
    {
        body.AppendLine("    [JsonIgnore]");
        body.AppendLine($"    public {enumName}? {propertyName}");
        body.AppendLine("    {");
        body.AppendLine($"        get => EnumUtility.ParseLiteral<{enumName}>(GetProperty<string>(\"{jsonName}\"));");
        body.AppendLine($"        set => SetProperty(\"{jsonName}\", value?.GetLiteral());");
        body.AppendLine("    }");
        body.AppendLine();
    }

    private void EmitPrimitive(StringBuilder body, string propertyName, string jsonName, string fhirTypeCode, bool isArray)
    {
        string clr = MapPrimitiveToClr(fhirTypeCode);
        bool stringLike = StringLikePrimitives.Contains(fhirTypeCode);

        if (isArray)
        {
            body.AppendLine("    [JsonIgnore]");
            body.AppendLine($"    public MutablePrimitiveList<{clr}> {propertyName} => GetPrimitiveListProperty<{clr}>(\"{jsonName}\");");
            body.AppendLine();
            return;
        }

        if (stringLike)
        {
            body.AppendLine("    [JsonIgnore]");
            body.AppendLine($"    public PrimitiveElement<{clr}> {propertyName}Element => new(MutableNode, \"{jsonName}\");");
            body.AppendLine();
            body.AppendLine("    [JsonIgnore]");
            body.AppendLine($"    public {clr}? {propertyName}");
            body.AppendLine("    {");
            body.AppendLine($"        get => {propertyName}Element.Value;");
            body.AppendLine($"        set => {propertyName}Element.Value = value;");
            body.AppendLine("    }");
            body.AppendLine();
            return;
        }

        body.AppendLine("    [JsonIgnore]");
        body.AppendLine($"    public {clr}? {propertyName}");
        body.AppendLine("    {");
        body.AppendLine($"        get => GetProperty<{clr}?>(\"{jsonName}\");");
        body.AppendLine($"        set => SetProperty(\"{jsonName}\", value);");
        body.AppendLine("    }");
        body.AppendLine();

        if (fhirTypeCode == "decimal")
        {
            EmitDecimalRaw(body, propertyName, jsonName);
        }
    }

    private void EmitDecimalRaw(StringBuilder body, string propertyName, string jsonName)
    {
        body.AppendLine("    /// <summary>");
        body.AppendLine($"    /// Raw JSON node for <c>{jsonName}</c>, preserving precision beyond <see cref=\"decimal\"/>'s range");
        body.AppendLine($"    /// (System.Decimal rounds past ~28-29 significant digits and overflows past ~7.9e28). Use this");
        body.AppendLine($"    /// instead of <see cref=\"{propertyName}\"/> when a value may exceed those bounds.");
        body.AppendLine("    /// </summary>");
        body.AppendLine("    [JsonIgnore]");
        body.AppendLine($"    public System.Text.Json.Nodes.JsonNode? {propertyName}Raw");
        body.AppendLine("    {");
        body.AppendLine($"        get => MutableNode[\"{jsonName}\"];");
        body.AppendLine($"        set => SetProperty(\"{jsonName}\", value);");
        body.AppendLine("    }");
        body.AppendLine();
    }

    private void EmitFallback(StringBuilder body, string propertyName, string jsonName, string fhirTypeCode, bool isArray)
    {
        body.AppendLine($"    // fallback: {fhirTypeCode}");
        body.AppendLine("    [JsonIgnore]");

        if (isArray)
        {
            body.AppendLine($"    public JsonArray? {propertyName}");
            body.AppendLine("    {");
            body.AppendLine($"        get => MutableNode[\"{jsonName}\"] as JsonArray;");
            body.AppendLine($"        set => SetProperty(\"{jsonName}\", value);");
            body.AppendLine("    }");
        }
        else
        {
            body.AppendLine($"    public JsonNode? {propertyName}");
            body.AppendLine("    {");
            body.AppendLine($"        get => MutableNode[\"{jsonName}\"];");
            body.AppendLine($"        set => SetProperty(\"{jsonName}\", value);");
            body.AppendLine("    }");
        }

        body.AppendLine();
    }

    private void EmitChoice(
        GenerationContext context,
        StringBuilder body,
        string typeName,
        string baseName,
        ElementDefinition element,
        MemberNameAllocator memberNames)
    {
        string basePascal = ToPascalCase(baseName);
        string enumName = $"{typeName}{basePascal}Type";

        var variants = new List<(string Key, string VariantPascal, string FhirTypeCode, bool IsComplex)>();

        foreach (var typeRef in element.Type)
        {
            string code = NormalizeFhirPathTypeUrl(typeRef.Code ?? string.Empty);

            bool isPrimitive = PrimitiveTypeNames.Contains(code);
            bool isComplexInList = IsTypedComplex(code);

            if (!isPrimitive && !isComplexInList)
            {
                context.RecordDroppedChoiceVariant($"{typeName}.{baseName}[x]", code);
                continue;
            }

            string variantPascal = ToPascalCase(code);
            string key = baseName + variantPascal;
            variants.Add((key, variantPascal, code, isComplexInList));
        }

        if (variants.Count == 0)
        {
            return;
        }

        string discriminatorName = memberNames.Allocate($"{basePascal}Type");

        var enumBuilder = new StringBuilder();
        enumBuilder.AppendLine("/// <summary>");
        enumBuilder.AppendLine($"/// Discriminator for the <c>{typeName}.{baseName}[x]</c> choice element: which typed");
        enumBuilder.AppendLine("/// variant (if any) is currently present in the JSON.");
        enumBuilder.AppendLine("/// </summary>");
        enumBuilder.AppendLine($"public enum {enumName}");
        enumBuilder.AppendLine("{");
        enumBuilder.AppendLine("    None,");
        foreach (var variant in variants)
        {
            enumBuilder.AppendLine($"    {variant.VariantPascal},");
        }

        enumBuilder.AppendLine("}");
        context.AddChoiceEnum(enumName, enumBuilder.ToString());

        string variantKeysLiteral = string.Join(", ", variants.Select(v => $"\"{v.Key}\""));
        body.AppendLine($"    private static readonly string[] {basePascal}VariantKeys =");
        body.AppendLine($"        [{variantKeysLiteral}];");
        body.AppendLine();

        body.AppendLine("    [JsonIgnore]");
        body.AppendLine($"    public {enumName} {discriminatorName}");
        body.AppendLine("    {");
        body.AppendLine("        get");
        body.AppendLine("        {");
        foreach (var variant in variants)
        {
            body.AppendLine($"            if (MutableNode[\"{variant.Key}\"] is not null)");
            body.AppendLine("            {");
            body.AppendLine($"                return {enumName}.{variant.VariantPascal};");
            body.AppendLine("            }");
            body.AppendLine();
        }

        body.AppendLine($"            return {enumName}.None;");
        body.AppendLine("        }");
        body.AppendLine("    }");
        body.AppendLine();

        foreach (var variant in variants)
        {
            string variantName = memberNames.Allocate(ToPascalCase(variant.Key));
            body.AppendLine("    [JsonIgnore]");

            if (variant.IsComplex)
            {
                body.AppendLine($"    public {variant.FhirTypeCode}? {variantName}");
                body.AppendLine("    {");
                body.AppendLine($"        get => GetComplexProperty<{variant.FhirTypeCode}>(\"{variant.Key}\");");
                body.AppendLine($"        set => Set{basePascal}Variant(\"{variant.Key}\", value?.MutableNode);");
                body.AppendLine("    }");
            }
            else
            {
                string clr = MapPrimitiveToClr(variant.FhirTypeCode);
                body.AppendLine($"    public {clr}? {variantName}");
                body.AppendLine("    {");
                body.AppendLine($"        get => GetProperty<{clr}?>(\"{variant.Key}\");");
                body.AppendLine($"        set => Set{basePascal}Variant(\"{variant.Key}\", value is null ? null : JsonValue.Create(value));");
                body.AppendLine("    }");
            }

            body.AppendLine();
        }

        string rawName = memberNames.Allocate(basePascal);
        body.AppendLine($"    /// <summary>The raw node of whichever <c>{baseName}*</c> variant is present, or null.</summary>");
        body.AppendLine("    [JsonIgnore]");
        body.AppendLine($"    public JsonNode? {rawName}");
        body.AppendLine("    {");
        body.AppendLine("        get");
        body.AppendLine("        {");
        body.AppendLine($"            foreach (var key in {basePascal}VariantKeys)");
        body.AppendLine("            {");
        body.AppendLine("                if (MutableNode[key] is JsonNode node)");
        body.AppendLine("                {");
        body.AppendLine("                    return node;");
        body.AppendLine("                }");
        body.AppendLine("            }");
        body.AppendLine();
        body.AppendLine("            return null;");
        body.AppendLine("        }");
        body.AppendLine("    }");
        body.AppendLine();

        body.AppendLine($"    private void Set{basePascal}Variant(string key, JsonNode? value)");
        body.AppendLine("    {");
        body.AppendLine($"        foreach (var variant in {basePascal}VariantKeys)");
        body.AppendLine("        {");
        body.AppendLine("            if (variant != key)");
        body.AppendLine("            {");
        body.AppendLine("                MutableNode.Remove(variant);");
        body.AppendLine("            }");
        body.AppendLine("        }");
        body.AppendLine();
        body.AppendLine("        if (value is null)");
        body.AppendLine("        {");
        body.AppendLine("            MutableNode.Remove(key);");
        body.AppendLine("        }");
        body.AppendLine("        else");
        body.AppendLine("        {");
        body.AppendLine("            MutableNode[key] = value;");
        body.AppendLine("        }");
        body.AppendLine("    }");
        body.AppendLine();
    }

    // ---------------------------------------------------------------------------------------------
    // File rendering
    // ---------------------------------------------------------------------------------------------
    private string RenderClass(
        string ns,
        string typeName,
        string baseClass,
        bool isResource,
        bool sealedType,
        bool isVersionSubclass,
        IReadOnlyList<string> additionalUsings,
        string body,
        string kindLabel)
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
        sb.AppendLine("using System.Text.Json.Nodes;");
        sb.AppendLine("using System.Text.Json.Serialization;");
        sb.AppendLine("using Ignixa.Abstractions;");
        sb.AppendLine("using Ignixa.Serialization;");
        sb.AppendLine("using Ignixa.Serialization.SourceNodes;");
        foreach (string extra in additionalUsings)
        {
            sb.AppendLine($"using {extra};");
        }

        sb.AppendLine();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();

        sb.AppendLine("/// <summary>");
        sb.AppendLine($"/// FHIR {typeName} {kindLabel} facade. Zero-copy view over the underlying JsonObject.");
        sb.AppendLine("/// </summary>");
        sb.AppendLine($"public {(sealedType ? "sealed " : string.Empty)}class {typeName} : {baseClass}");
        sb.AppendLine("{");

        if (isResource)
        {
            sb.AppendLine($"    public {typeName}()");
            sb.AppendLine("    {");
            sb.AppendLine($"        ResourceType = \"{typeName}\";");
            sb.AppendLine("    }");
            sb.AppendLine();
            // Base resource ctor must be reachable by version subclasses in other assemblies, so it is
            // 'protected internal'. The version subclass ctor is only used within its own assembly.
            sb.AppendLine($"    {(isVersionSubclass ? "internal" : "protected internal")} {typeName}(JsonObject jsonObject)");
            sb.AppendLine("        : base(jsonObject)");
            sb.AppendLine("    {");
            sb.AppendLine("    }");
            sb.AppendLine();
            // Required by JsonNodeConverter (Activator.CreateInstance with (JsonObject, null)) and the
            // BaseJsonNode constructor guard test; base resource facades live in the Serialization assembly.
            sb.AppendLine($"    public {typeName}(JsonObject jsonObject, FhirVersion? fhirVersion = null)");
            sb.AppendLine("        : base(jsonObject, fhirVersion)");
            sb.AppendLine("    {");
            sb.AppendLine("    }");
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine($"    public {typeName}()");
            sb.AppendLine("    {");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine($"    public {typeName}(JsonObject jsonObject, FhirVersion? fhirVersion = null)");
            sb.AppendLine("        : base(jsonObject, fhirVersion)");
            sb.AppendLine("    {");
            sb.AppendLine("    }");
            sb.AppendLine();
        }

        sb.Append(body);

        while (sb.Length >= 2 && sb[^1] == '\n')
        {
            sb.Length -= Environment.NewLine.Length;
            sb.AppendLine();
            break;
        }

        sb.AppendLine("}");

        return sb.ToString();
    }

    private static string RenderGlobalUsings(string version, IReadOnlyDictionary<string, TypeClassification> classifications)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("// -------------------------------------------------------------------------------------------------");
        sb.AppendLine("// Copyright (c) Ignixa Contributors. All rights reserved.");
        sb.AppendLine("// Licensed under the MIT License. See LICENSE in the repo root for license information.");
        sb.AppendLine("// -------------------------------------------------------------------------------------------------");
        sb.AppendLine();
        sb.AppendLine("// Base-only (Identical) types have no per-version subclass; alias them so unqualified use");
        sb.AppendLine($"// inside Ignixa.Models.{version} resolves to the shared Ignixa.Models base type.");
        sb.AppendLine();

        foreach (var classification in classifications.Values.OrderBy(c => c.TypeName, StringComparer.Ordinal))
        {
            // Only types that are present in this version but have NO subclass here need an alias;
            // types with a subclass already define Ignixa.Models.{version}.X.
            if (!classification.PresentInVersions.Contains(version))
            {
                continue;
            }

            if (classification.SubclassVersions.Contains(version))
            {
                continue;
            }

            sb.AppendLine($"global using {classification.TypeName} = Ignixa.Models.{classification.TypeName};");
        }

        return sb.ToString();
    }

    private static string RenderEntryPoints(string version, IReadOnlyList<(string ResourceType, string TypeName)> registrations)
    {
        var distinct = registrations
            .GroupBy(r => r.ResourceType, StringComparer.Ordinal)
            .Select(g => g.First())
            .OrderBy(r => r.ResourceType, StringComparer.Ordinal)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("// -------------------------------------------------------------------------------------------------");
        sb.AppendLine("// Copyright (c) Ignixa Contributors. All rights reserved.");
        sb.AppendLine("// Licensed under the MIT License. See LICENSE in the repo root for license information.");
        sb.AppendLine("// -------------------------------------------------------------------------------------------------");
        sb.AppendLine();
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("using System.Runtime.CompilerServices;");
        sb.AppendLine("using System.Text.Json.Nodes;");
        sb.AppendLine("using Ignixa.Abstractions;");
        sb.AppendLine("using Ignixa.Serialization;");
        sb.AppendLine("using Ignixa.Serialization.SourceNodes;");
        sb.AppendLine();
        sb.AppendLine($"namespace Ignixa.Models.{version};");
        sb.AppendLine();
        sb.AppendLine("/// <summary>");
        sb.AppendLine($"/// Entry points and version-aware registration for the FHIR {version} typed-model package.");
        sb.AppendLine("/// </summary>");
        sb.AppendLine($"public static class {version}");
        sb.AppendLine("{");
        sb.AppendLine($"    /// <summary>The FHIR version this package targets.</summary>");
        sb.AppendLine($"    public const FhirVersion Version = FhirVersion.{version};");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>Deserializes JSON straight to the requested resource's view, stamped with this version.</summary>");
        sb.AppendLine("    /// <exception cref=\"InvalidCastException\">The parsed resource's <c>resourceType</c> does not match <typeparamref name=\"T\"/>.</exception>");
        sb.AppendLine("    public static T Parse<T>(string json)");
        sb.AppendLine("        where T : ResourceJsonNode");
        sb.AppendLine("    {");
        sb.AppendLine("        var node = ResourceJsonNode.Parse(json);");
        sb.AppendLine("        node.FhirVersion = Version;");
        sb.AppendLine("        return node.As<T>();");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine($"    /// <summary>Registers every {version} resource type into the version-aware model registry.</summary>");
        sb.AppendLine("    [ModuleInitializer]");
        sb.AppendLine("    public static void Register()");
        sb.AppendLine("    {");
        foreach (var (resourceType, typeName) in distinct)
        {
            sb.AppendLine($"        VersionedModelRegistry.Register(\"{resourceType}\", Version, jsonObject => new {typeName}(jsonObject));");
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("/// <summary>Version-specific reinterpret extensions for the FHIR " + version + " package.</summary>");
        sb.AppendLine($"public static class {version}Extensions");
        sb.AppendLine("{");
        sb.AppendLine($"    /// <summary>Reinterprets an existing node as its {version} view (zero-copy).</summary>");
        sb.AppendLine("    /// <exception cref=\"InvalidCastException\">The node's <c>resourceType</c> does not match <typeparamref name=\"T\"/>.</exception>");
        sb.AppendLine($"    public static T As{version}<T>(this ResourceJsonNode node)");
        sb.AppendLine("        where T : ResourceJsonNode");
        sb.AppendLine("    {");
        sb.AppendLine("        ArgumentNullException.ThrowIfNull(node);");
        sb.AppendLine($"        node.FhirVersion = {version}.Version;");
        sb.AppendLine("        return node.As<T>();");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private void WriteSupportFiles(string outputDir, string ns)
    {
        File.WriteAllText(Path.Combine(outputDir, "PrimitiveElement.cs"), RenderPrimitiveElement(ns), Encoding.UTF8);
    }

    private static string RenderPrimitiveElement(string ns)
    {
        return $$"""
// <auto-generated/>
// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System.Text.Json.Nodes;

namespace {{ns}};

/// <summary>
/// Firely-style wrapper over a FHIR primitive element. A FHIR primitive spans TWO sibling
/// keys on the parent object: the value key (e.g. <c>birthDate</c>) and an optional shadow
/// (<c>_birthDate</c>) carrying <c>id</c>/<c>extension</c>.
/// </summary>
/// <typeparam name="T">The CLR primitive type stored at the value key (e.g. <see cref="string"/>).</typeparam>
public sealed class PrimitiveElement<T>
{
    private readonly JsonObject _parent;
    private readonly string _name;

    public PrimitiveElement(JsonObject parent, string name)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentException.ThrowIfNullOrEmpty(name);
        _parent = parent;
        _name = name;
    }

    private string ShadowName => "_" + _name;

    /// <summary>
    /// The primitive value at <c>parent[name]</c>. Setting null removes only the value key;
    /// any shadow (extensions/id) is preserved.
    /// </summary>
    public T? Value
    {
        get => _parent[_name] is JsonValue v ? v.GetValue<T>() : default;
        set
        {
            if (value is null)
            {
                _parent.Remove(_name);
            }
            else
            {
                _parent[_name] = JsonValue.Create(value);
            }
        }
    }

    /// <summary>The <c>id</c> from the shadow object, or null.</summary>
    public string? Id
    {
        get => Shadow(create: false)?["id"]?.GetValue<string>();
        set
        {
            if (value is null)
            {
                var shadow = Shadow(create: false);
                shadow?.Remove("id");
                PruneShadowIfEmpty();
            }
            else
            {
                Shadow(create: true)!["id"] = value;
            }
        }
    }

    /// <summary>
    /// The shadow's <c>extension</c> array. Reading creates the shadow + array on demand (so callers
    /// can <c>Add</c>); call <see cref="PruneEmptyShadow"/> after clearing to drop an empty shadow.
    /// </summary>
    public JsonArray Extension
    {
        get
        {
            var shadow = Shadow(create: true)!;
            if (shadow["extension"] is not JsonArray array)
            {
                array = new JsonArray();
                shadow["extension"] = array;
            }

            return array;
        }
    }

    /// <summary>True when a shadow object with id or extensions is present.</summary>
    public bool HasExtensions
    {
        get
        {
            var shadow = Shadow(create: false);
            return shadow is not null
                   && ((shadow["extension"] is JsonArray a && a.Count > 0) || shadow["id"] is not null);
        }
    }

    /// <summary>Removes the shadow object if it carries no id and no (non-empty) extensions.</summary>
    public void PruneEmptyShadow() => PruneShadowIfEmpty();

    private JsonObject? Shadow(bool create)
    {
        if (_parent[ShadowName] is JsonObject existing)
        {
            return existing;
        }

        if (!create)
        {
            return null;
        }

        var shadow = new JsonObject();
        _parent[ShadowName] = shadow;
        return shadow;
    }

    private void PruneShadowIfEmpty()
    {
        if (_parent[ShadowName] is not JsonObject shadow)
        {
            return;
        }

        bool hasExtensions = shadow["extension"] is JsonArray a && a.Count > 0;
        bool hasId = shadow["id"] is not null;

        if (shadow["extension"] is JsonArray empty && empty.Count == 0)
        {
            shadow.Remove("extension");
        }

        if (!hasExtensions && !hasId)
        {
            _parent.Remove(ShadowName);
        }
    }
}

""";
    }

    private static bool IsBackbone(ElementDefinition element, StructureDefinition sd)
    {
        if (element.Type is null || element.Type.Count == 0)
        {
            return false;
        }

        if (element.Type.Any(t => t.Code == "BackboneElement"))
        {
            return true;
        }

        if (element.Type.Any(t => t.Code == "Element"))
        {
            var children = sd.cgElements(forBackbonePath: element.Path, topLevelOnly: true, includeRoot: false, skipSlices: true);
            return children.Any();
        }

        return false;
    }

    private static string ResolveTypeCode(ElementDefinition element)
    {
        var first = element.Type?.FirstOrDefault();
        return first is null ? "Element" : NormalizeFhirPathTypeUrl(first.Code ?? "Element");
    }

    private static string NormalizeFhirPathTypeUrl(string typeCode)
    {
        return typeCode switch
        {
            "http://hl7.org/fhirpath/System.String" => "string",
            "http://hl7.org/fhirpath/System.Boolean" => "boolean",
            "http://hl7.org/fhirpath/System.Integer" => "integer",
            "http://hl7.org/fhirpath/System.Decimal" => "decimal",
            "http://hl7.org/fhirpath/System.DateTime" => "dateTime",
            "http://hl7.org/fhirpath/System.Date" => "date",
            "http://hl7.org/fhirpath/System.Time" => "time",
            "http://hl7.org/fhirpath/System.Quantity" => "Quantity",
            _ => typeCode,
        };
    }

    private static string MapPrimitiveToClr(string fhirTypeCode)
    {
        return fhirTypeCode switch
        {
            "boolean" => "bool",
            "integer" or "unsignedInt" or "positiveInt" => "int",
            "integer64" => "long",
            "decimal" => "decimal",
            _ => "string",
        };
    }

    private static string StripChoiceMarker(string jsonName)
        => jsonName.EndsWith("[x]", StringComparison.Ordinal) ? jsonName[..^3] : jsonName;

    private static string ToPascalCase(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return char.IsUpper(value[0]) ? value : char.ToUpperInvariant(value[0]) + value[1..];
    }

    /// <summary>
    /// Allocates unique C# member names within a single facade. Seeded with the enclosing type name
    /// (a member may not share it — CS0542) and inherited base-class members it would shadow
    /// (CS0108), then deduplicates each emitted member (CS0102) by appending an ordinal suffix.
    /// </summary>
    private sealed class MemberNameAllocator
    {
        private readonly HashSet<string> _used = new(StringComparer.Ordinal);

        public MemberNameAllocator(string typeName, bool isResource)
        {
            _used.Add(typeName);
            _used.Add("MutableNode");
            _used.Add("FhirVersion");

            if (isResource)
            {
                _used.Add("ResourceType");
                _used.Add("Id");
                _used.Add("Meta");
            }
        }

        /// <summary>Reserves a name (and primitive companions) so subclass deltas never shadow it.</summary>
        public void Reserve(string name)
        {
            _used.Add(name);
            _used.Add(name + "Element");
            _used.Add(name + "Raw");
            _used.Add(name + "Type");
        }

        public string Allocate(string preferred)
        {
            string candidate = preferred;
            int suffix = 2;
            while (!_used.Add(candidate))
            {
                candidate = preferred + suffix;
                suffix++;
            }

            return candidate;
        }

        public string AllocatePrimitive(string preferred, string fhirTypeCode, bool isArray)
        {
            string name = Allocate(preferred);

            if (!isArray)
            {
                if (StringLikePrimitives.Contains(fhirTypeCode))
                {
                    _used.Add(name + "Element");
                }

                if (fhirTypeCode == "decimal")
                {
                    _used.Add(name + "Raw");
                }
            }

            return name;
        }
    }
}
