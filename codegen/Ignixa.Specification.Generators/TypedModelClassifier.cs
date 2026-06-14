// <copyright file="TypedModelClassifier.cs" company="Ignixa Contributors">
//     Copyright (c) Ignixa Contributors. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.CodeGen.FhirExtensions;
using Microsoft.Health.Fhir.CodeGen.Models;

namespace Ignixa.Specification.Generators;

/// <summary>
/// Cross-version classification engine. Walks the <see cref="DefinitionCollection"/> for every
/// targeted version, builds a per-type / per-element signature, and assigns each type and element to a
/// <see cref="SharingBucket"/> (Identical / Additive / Incompatible). The result drives base-vs-version
/// emission in <see cref="CSharpTypedModelLanguage"/>.
/// </summary>
internal sealed class TypedModelClassifier
{
    private readonly IReadOnlyList<(string Version, DefinitionCollection Definitions)> _versions;
    private readonly CSharpTypedModelConfig _config;

    public TypedModelClassifier(
        IReadOnlyList<(string Version, DefinitionCollection Definitions)> versions,
        CSharpTypedModelConfig config)
    {
        _versions = versions;
        _config = config;
    }

    /// <summary>Builds the merged classification for every discovered type, keyed by type name.</summary>
    public IReadOnlyDictionary<string, TypeClassification> Classify()
    {
        var perVersion = new Dictionary<string, VersionView>(StringComparer.Ordinal);
        foreach (var (version, definitions) in _versions)
        {
            perVersion[version] = BuildVersionView(version, definitions);
        }

        var allTypeNames = perVersion.Values
            .SelectMany(v => v.Types.Keys)
            .ToHashSet(StringComparer.Ordinal);

        var result = new Dictionary<string, TypeClassification>(StringComparer.Ordinal);
        foreach (string typeName in allTypeNames.OrderBy(x => x, StringComparer.Ordinal))
        {
            result[typeName] = MergeType(typeName, perVersion);
        }

        return result;
    }

    private TypeClassification MergeType(string typeName, Dictionary<string, VersionView> perVersion)
    {
        var presentIn = _versions
            .Where(v => perVersion[v.Version].Types.ContainsKey(typeName))
            .Select(v => v.Version)
            .ToList();

        // Kind is taken from the first version that defines the type (stable across versions in practice).
        FacadeKind kind = perVersion[presentIn[0]].Types[typeName].Kind;

        // Union of element names across the versions that have this type.
        var elementNames = presentIn
            .SelectMany(v => perVersion[v].Types[typeName].Elements.Keys)
            .ToHashSet(StringComparer.Ordinal);

        var baseElements = new List<ElementClassification>();
        var subclassElements = new List<ElementClassification>();
        var subclassVersions = new HashSet<string>(StringComparer.Ordinal);

        foreach (string elementName in elementNames.OrderBy(x => x, StringComparer.Ordinal))
        {
            var factsByVersion = new Dictionary<string, ElementFacts>(StringComparer.Ordinal);
            foreach (string version in presentIn)
            {
                if (perVersion[version].Types[typeName].Elements.TryGetValue(elementName, out var facts))
                {
                    factsByVersion[version] = facts;
                }
            }

            bool presentEverywhere = factsByVersion.Count == presentIn.Count;
            var distinctSignatures = factsByVersion.Values.Select(f => f.Signature).Distinct().ToList();

            if (presentEverywhere && distinctSignatures.Count == 1)
            {
                // Identical across all present versions -> base type.
                baseElements.Add(new ElementClassification
                {
                    JsonName = elementName,
                    Bucket = SharingBucket.Identical,
                    BaseSignature = distinctSignatures[0],
                    PerVersion = factsByVersion,
                    EmittedInVersions = [],
                });
                continue;
            }

            // Either additive (missing in some versions) or incompatible (differs). Both emit on the
            // per-version subclass for every version that actually has the element.
            SharingBucket bucket = distinctSignatures.Count > 1
                ? SharingBucket.Incompatible
                : SharingBucket.Additive;

            var emittedIn = presentIn.Where(factsByVersion.ContainsKey).ToList();
            foreach (string version in emittedIn)
            {
                subclassVersions.Add(version);
            }

            subclassElements.Add(new ElementClassification
            {
                JsonName = elementName,
                Bucket = bucket,
                BaseSignature = null,
                PerVersion = factsByVersion,
                EmittedInVersions = emittedIn,
            });
        }

        SharingBucket typeBucket;
        if (subclassElements.Count == 0 && presentIn.Count == _versions.Count)
        {
            typeBucket = SharingBucket.Identical;
        }
        else if (subclassElements.Any(e => e.Bucket == SharingBucket.Incompatible))
        {
            typeBucket = SharingBucket.Incompatible;
        }
        else
        {
            typeBucket = SharingBucket.Additive;
        }

        // A type present in only some versions still needs a subclass in those versions so the
        // version-only type resolves (even when its element set is otherwise identical there).
        if (presentIn.Count != _versions.Count)
        {
            foreach (string version in presentIn)
            {
                subclassVersions.Add(version);
            }
        }

        return new TypeClassification
        {
            TypeName = typeName,
            Kind = kind,
            Bucket = typeBucket,
            PresentInVersions = presentIn,
            SubclassVersions = subclassVersions.OrderBy(x => x, StringComparer.Ordinal).ToList(),
            BaseElements = baseElements,
            SubclassElements = subclassElements,
        };
    }

    private VersionView BuildVersionView(string version, DefinitionCollection definitions)
    {
        var view = new VersionView();
        var resourceAllow = new HashSet<string>(_config.ResourceAllowList, StringComparer.Ordinal);
        var datatypeAllow = _config.GenerateAllDatatypes
            ? ResolveAllConcreteDatatypes(definitions)
            : new HashSet<string>(_config.DatatypeAllowList, StringComparer.Ordinal);

        view.DatatypeNames = datatypeAllow;
        view.TypedComplex = new HashSet<string>(datatypeAllow, StringComparer.Ordinal);

        // Resources.
        foreach (string resourceName in resourceAllow)
        {
            if (!definitions.ResourcesByName.TryGetValue(resourceName, out var sd))
            {
                continue;
            }

            FacadeKind kind = IsDomainResource(sd, definitions) ? FacadeKind.DomainResource : FacadeKind.Resource;
            WalkType(view, definitions, resourceName, resourceName, sd, backbonePath: null, kind, isResource: true);
        }

        // Datatypes.
        foreach (string datatypeName in datatypeAllow)
        {
            if (!definitions.ComplexTypesByName.TryGetValue(datatypeName, out var sd))
            {
                continue;
            }

            WalkType(view, definitions, datatypeName, datatypeName, sd, backbonePath: null, FacadeKind.Datatype, isResource: false);
        }

        // Backbones (queue drained, may enqueue deeper ones).
        while (view.PendingBackbones.Count > 0)
        {
            var backbone = view.PendingBackbones.Dequeue();
            WalkType(view, definitions, backbone.RootStructureName, backbone.TypeName, backbone.Sd, backbone.ElementPath, FacadeKind.Backbone, isResource: false);
        }

        return view;
    }

    private void WalkType(
        VersionView view,
        DefinitionCollection definitions,
        string rootStructureName,
        string typeName,
        StructureDefinition sd,
        string? backbonePath,
        FacadeKind kind,
        bool isResource)
    {
        if (view.Types.ContainsKey(typeName))
        {
            return;
        }

        var typeInfo = new VersionTypeInfo { Kind = kind };
        view.Types[typeName] = typeInfo;

        var elements = backbonePath is null
            ? sd.cgElements(topLevelOnly: true, includeRoot: false, skipSlices: true).ToList()
            : sd.cgElements(forBackbonePath: backbonePath, topLevelOnly: true, includeRoot: false, skipSlices: true).ToList();

        foreach (var element in elements)
        {
            string jsonName = element.cgName();

            if (isResource && (jsonName is "id" or "meta" or "resourceType"))
            {
                continue;
            }

            bool isChoice = element.Path.EndsWith("[x]", StringComparison.Ordinal);
            bool isArray = element.cgIsArray();

            if (IsBackbone(element, sd))
            {
                string backboneTypeName = typeName + ToPascalCase(StripChoiceMarker(jsonName));
                view.TypedComplex.Add(backboneTypeName);
                if (view.RegisteredBackbonePaths.Add(element.Path))
                {
                    view.PendingBackbones.Enqueue(new PendingBackboneInfo(rootStructureName, backboneTypeName, sd, element.Path));
                }

                var backboneSig = new ElementSignature(backboneTypeName, isArray, IsChoice: false, ValueSetUrl: null, VariantTypeCodes: null);
                typeInfo.Elements[jsonName] = new ElementFacts(jsonName, backboneSig, element, sd);
                continue;
            }

            ElementSignature signature = isChoice
                ? BuildChoiceSignature(element)
                : BuildSimpleSignature(element, definitions, view);

            string strippedName = StripChoiceMarker(jsonName);
            typeInfo.Elements[strippedName] = new ElementFacts(strippedName, signature, element, sd);
        }
    }

    private ElementSignature BuildChoiceSignature(ElementDefinition element)
    {
        var variantCodes = (element.Type ?? [])
            .Select(t => NormalizeTypeCode(t.Code ?? string.Empty))
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        return new ElementSignature(
            TypeCode: "(choice)",
            IsArray: element.cgIsArray(),
            IsChoice: true,
            ValueSetUrl: null,
            VariantTypeCodes: string.Join("|", variantCodes));
    }

    private ElementSignature BuildSimpleSignature(ElementDefinition element, DefinitionCollection definitions, VersionView view)
    {
        string typeCode = NormalizeTypeCode(element.Type?.FirstOrDefault()?.Code ?? "Element");
        bool isArray = element.cgIsArray();

        // Binding only differentiates when it produces an enum (required code binding with a value set).
        string? valueSetUrl = null;
        if (!isArray && element.cgHasCodes() && !string.IsNullOrEmpty(element.Binding?.ValueSet))
        {
            valueSetUrl = StripVersion(element.Binding!.ValueSet);
        }

        return new ElementSignature(typeCode, isArray, IsChoice: false, valueSetUrl, VariantTypeCodes: null);
    }

    private static string StripChoiceMarker(string jsonName)
        => jsonName.EndsWith("[x]", StringComparison.Ordinal) ? jsonName[..^3] : jsonName;

    private static string StripVersion(string url)
    {
        int pipe = url.IndexOf('|');
        return pipe >= 0 ? url[..pipe] : url;
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

    private static bool IsDomainResource(StructureDefinition sd, DefinitionCollection definitions)
    {
        // Walk the baseDefinition chain until we hit DomainResource or Resource.
        string? baseUrl = sd.BaseDefinition;
        int guard = 0;
        while (!string.IsNullOrEmpty(baseUrl) && guard++ < 10)
        {
            string baseName = baseUrl.Split('/').Last();
            if (baseName == "DomainResource")
            {
                return true;
            }

            if (baseName == "Resource")
            {
                return false;
            }

            if (definitions.ResourcesByName.TryGetValue(baseName, out var baseSd))
            {
                baseUrl = baseSd.BaseDefinition;
            }
            else
            {
                break;
            }
        }

        return false;
    }

    private static string NormalizeTypeCode(string typeCode)
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

    private static HashSet<string> ResolveAllConcreteDatatypes(DefinitionCollection definitions)
    {
        var abstractBaseTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "Base", "Element", "BackboneElement", "BackboneType", "DataType",
            "PrimitiveType", "Resource", "DomainResource",
        };

        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (name, sd) in definitions.ComplexTypesByName)
        {
            if (abstractBaseTypes.Contains(name) || sd.Abstract == true)
            {
                continue;
            }

            result.Add(name);
        }

        return result;
    }

    private static string ToPascalCase(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return char.IsUpper(value[0]) ? value : char.ToUpperInvariant(value[0]) + value[1..];
    }

    private sealed class VersionView
    {
        public Dictionary<string, VersionTypeInfo> Types { get; } = new(StringComparer.Ordinal);

        public Queue<PendingBackboneInfo> PendingBackbones { get; } = new();

        public HashSet<string> RegisteredBackbonePaths { get; } = new(StringComparer.Ordinal);

        public HashSet<string> TypedComplex { get; set; } = new(StringComparer.Ordinal);

        public HashSet<string> DatatypeNames { get; set; } = new(StringComparer.Ordinal);
    }

    private sealed class VersionTypeInfo
    {
        public FacadeKind Kind { get; init; }

        public Dictionary<string, ElementFacts> Elements { get; } = new(StringComparer.Ordinal);
    }

    private sealed record PendingBackboneInfo(string RootStructureName, string TypeName, StructureDefinition Sd, string ElementPath);
}
