// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using HotChocolate;
using HotChocolate.Execution.Configuration;
using HotChocolate.Language;
using HotChocolate.Resolvers;
using HotChocolate.Types;
using HotChocolate.Types.Descriptors;
using Ignixa.Abstractions;
using Ignixa.Application.Features.Experimental.GraphQl.Contracts;
using Ignixa.Application.Features.Experimental.GraphQl.DataLoaders;
using Ignixa.Application.Features.Experimental.GraphQl.Models;
using Ignixa.Search.Definition;
using Ignixa.Search.Indexing.SearchValues;
using Microsoft.Extensions.Logging;
using FhirIType = Ignixa.Abstractions.IType;
using FhirITypeExtended = Ignixa.Abstractions.ITypeExtended;
using FhirIFhirSchemaProvider = Ignixa.Abstractions.IFhirSchemaProvider;
using FhirFieldResolver = Ignixa.Application.Features.Experimental.GraphQl.Resolvers.FieldResolver;
using ReferenceResolver = Ignixa.Application.Features.Experimental.GraphQl.Resolvers.ReferenceResolver;
using AppResourceResolver = Ignixa.Application.Features.Experimental.GraphQl.Resolvers.ResourceResolver;
using AppSearchResolver = Ignixa.Application.Features.Experimental.GraphQl.Resolvers.SearchResolver;
using AppMutationResolver = Ignixa.Application.Features.Experimental.GraphQl.Resolvers.MutationResolver;

namespace Ignixa.Application.Features.Experimental.GraphQl.Schema;

public sealed class FhirTypeModule(
    FhirIFhirSchemaProvider schemaProvider,
    ISearchParameterDefinitionManager searchParameterManager,
    ILogger<FhirTypeModule> logger) : ITypeModule, IFhirTypeModule
{
    private readonly IReferenceSearchValueParser _referenceParser = new ReferenceSearchValueParser(schemaProvider);
    private static readonly string[] ComplexExtensionValueTypes =
    [
        "valueCoding", "valueCodeableConcept", "valueQuantity",
        "valuePeriod", "valueRange", "valueReference",
        "valueIdentifier", "valueHumanName", "valueAddress",
        "valueContactPoint", "valueAttachment",
    ];

    public event EventHandler<EventArgs>? TypesChanged;

    private static bool IsAbstractBaseType(string? typeName) =>
        typeName is "BackboneElement" or "Element" or "Base" or "DataType";

    private record BackboneTypeInfo(string GraphQlName, FhirIType FhirType);

    public ValueTask<IReadOnlyCollection<ITypeSystemMember>> CreateTypesAsync(
        IDescriptorContext context,
        CancellationToken cancellationToken)
    {
        var types = new List<ITypeSystemMember>();

        EmitFhirScalars(types);
        EmitElementTypes(types);

        var concreteResourceTypes = GetConcreteResourceTypes();
        var resourceTypeSet = new HashSet<string>(concreteResourceTypes, StringComparer.Ordinal);

        var backbones = new List<BackboneTypeInfo>();
        var referencedDataTypes = new HashSet<string>(StringComparer.Ordinal);
        var visitedPaths = new HashSet<string>(StringComparer.Ordinal);

        foreach (var rt in concreteResourceTypes)
        {
            var typeDef = schemaProvider.GetTypeDefinition(rt);
            if (typeDef is not null)
                DiscoverNestedTypes(typeDef, rt, resourceTypeSet, backbones, referencedDataTypes, visitedPaths);
        }

        foreach (var complexValueName in ComplexExtensionValueTypes)
        {
            var typeName = complexValueName[5..];
            if (schemaProvider.IsKnownType(typeName) && !resourceTypeSet.Contains(typeName))
                referencedDataTypes.Add(typeName);
        }

        var processedDataTypes = new HashSet<string>(StringComparer.Ordinal);
        var dtQueue = new Queue<string>(referencedDataTypes);
        while (dtQueue.Count > 0)
        {
            var dtName = dtQueue.Dequeue();
            if (!processedDataTypes.Add(dtName)) continue;
            var dtDef = schemaProvider.GetTypeDefinition(dtName);
            if (dtDef is null) continue;
            var beforeCount = referencedDataTypes.Count;
            DiscoverNestedTypes(dtDef, dtName, resourceTypeSet, backbones, referencedDataTypes, visitedPaths);
            if (referencedDataTypes.Count > beforeCount)
            {
                foreach (var newDt in referencedDataTypes)
                {
                    if (!processedDataTypes.Contains(newDt))
                        dtQueue.Enqueue(newDt);
                }
            }
        }

        var manuallyEmitted = new HashSet<string>(StringComparer.Ordinal)
        {
            "Element", "Extension", "Reference", "Resource",
            "DomainResource", "BackboneElement", "Base",
            "DataType", "PrimitiveType", "xhtml",
        };
        referencedDataTypes.ExceptWith(manuallyEmitted);

        foreach (var bb in backbones)
            types.Add(BuildPreDiscoveredBackboneType(bb.GraphQlName, bb.FhirType));

        foreach (var dtName in referencedDataTypes)
        {
            var dtDef = schemaProvider.GetTypeDefinition(dtName);
            if (dtDef is not null)
                types.Add(BuildDataTypeObjectType(dtName, dtDef));
        }

        foreach (var resourceTypeName in concreteResourceTypes)
        {
            var fhirType = schemaProvider.GetTypeDefinition(resourceTypeName);
            if (fhirType is null) continue;
            types.Add(BuildResourceObjectType(resourceTypeName, fhirType, concreteResourceTypes));
        }

        types.Add(BuildResourceReferenceType());
        types.Add(BuildResourceUnionType(concreteResourceTypes));

        foreach (var resourceTypeName in concreteResourceTypes)
            types.Add(BuildConnectionType(resourceTypeName));
        foreach (var resourceTypeName in concreteResourceTypes)
            types.Add(BuildEdgeType(resourceTypeName));

        types.Add(BuildQueryType(concreteResourceTypes));
        types.Add(BuildMutationType(concreteResourceTypes));

        logger.LogInformation("FhirTypeModule generated {TypeCount} GraphQL types for FHIR {Version}",
            types.Count, schemaProvider.Version);

        return ValueTask.FromResult<IReadOnlyCollection<ITypeSystemMember>>(types);
    }

    public void NotifyTypesChanged() => TypesChanged?.Invoke(this, EventArgs.Empty);

    private void DiscoverNestedTypes(
        FhirIType type,
        string parentPath,
        HashSet<string> resourceTypeSet,
        List<BackboneTypeInfo> backbones,
        HashSet<string> referencedDataTypes,
        HashSet<string> visitedPaths)
    {
        if (type is not FhirITypeExtended extended) return;

        foreach (var child in extended.Children)
        {
            var elementName = child.Info.Name;
            var childPath = $"{parentPath}_{GraphQlNamingHelper.ToPascalCase(elementName)}";

            if (child.Info.IsChoiceElement && child is FhirITypeExtended choiceExt)
            {
                foreach (var memberType in choiceExt.Types)
                {
                    var code = memberType.Code;
                    if (string.IsNullOrEmpty(code) || !schemaProvider.IsKnownType(code))
                        continue;
                    if (IsPrimitiveTypeCode(code))
                        continue;
                    if (!resourceTypeSet.Contains(code))
                        referencedDataTypes.Add(code);
                }

                continue;
            }

            if (child is FhirITypeExtended ext)
            {
                var typeName = ext.Types.Count > 0 ? ext.Types[0].Code : null;

                if (typeName == "Reference") continue;
                if (child.Info.IsPrimitive) continue;

                if (child.Children.Count > 0 && (typeName is null || IsAbstractBaseType(typeName) || !schemaProvider.IsKnownType(typeName)))
                {
                    var backboneName = GraphQlNamingHelper.ToBackboneTypeName(parentPath, elementName);
                    if (visitedPaths.Add(backboneName))
                    {
                        backbones.Add(new BackboneTypeInfo(backboneName, child));
                        DiscoverNestedTypes(child, backboneName, resourceTypeSet, backbones, referencedDataTypes, visitedPaths);
                    }

                    continue;
                }

                if (typeName is not null && schemaProvider.IsKnownType(typeName) && !resourceTypeSet.Contains(typeName))
                    referencedDataTypes.Add(typeName);
            }
        }
    }

    private IReadOnlyList<string> GetConcreteResourceTypes()
    {
        var result = new List<string>();
        foreach (var name in schemaProvider.ResourceTypeNames)
        {
            var typeDef = schemaProvider.GetTypeDefinition(name);
            if (typeDef is not null && !typeDef.Info.IsAbstract)
                result.Add(name);
        }
        result.Sort(StringComparer.Ordinal);
        return result;
    }

    private static void EmitFhirScalars(List<ITypeSystemMember> types)
    {
        types.Add(new FhirDateScalarType());
        types.Add(new FhirDateTimeScalarType());
        types.Add(new FhirInstantScalarType());
        types.Add(new FhirTimeScalarType());
    }

    private static void EmitElementTypes(List<ITypeSystemMember> types)
    {
        types.Add(new ObjectType(descriptor =>
        {
            descriptor.Name("Element");
            descriptor.Description("FHIR Element with id and extensions on primitives");

            descriptor.Field("id").Type<StringType>()
                .Resolve(ctx =>
                {
                    var parent = ctx.Parent<JsonElement>();
                    return parent.TryGetProperty("id", out var id) ? id.GetString() : null;
                });

            descriptor.Field("extension")
                .Type(new ListTypeNode(new NamedTypeNode("Extension")))
                .Argument("url", a => a.Type<StringType>()
                    .Description("Filter extensions by their canonical URL"))
                .Resolve(ctx =>
                {
                    var parent = ctx.Parent<JsonElement>();
                    var urlFilter = ctx.ArgumentOptional<string?>("url");
                    var url = urlFilter.HasValue ? urlFilter.Value : null;
                    return FhirFieldResolver.FilterExtensionsByUrl(parent, url);
                });
        }));

        types.Add(new ObjectType(descriptor =>
        {
            descriptor.Name("Extension");
            descriptor.Description("FHIR Extension element");

            descriptor.Field("url").Type<NonNullType<StringType>>()
                .Resolve(ctx => FhirFieldResolver.GetStringProperty(ctx.Parent<JsonElement>(), "url"));

            // Nested extensions (extensions can contain extensions)
            descriptor.Field("extension")
                .Type(new ListTypeNode(new NamedTypeNode("Extension")))
                .Argument("url", a => a.Type<StringType>()
                    .Description("Filter nested extensions by their canonical URL"))
                .Resolve(ctx =>
                {
                    var parent = ctx.Parent<JsonElement>();
                    var urlFilter = ctx.ArgumentOptional<string?>("url");
                    var url = urlFilter.HasValue ? urlFilter.Value : null;
                    return FhirFieldResolver.FilterExtensionsByUrl(parent, url);
                });

            // Common value[x] types
            foreach (var (valueName, graphQlType) in new[]
            {
                ("valueString", "String"),
                ("valueBoolean", "Boolean"),
                ("valueInteger", "Int"),
                ("valuePositiveInt", "Int"),
                ("valueUnsignedInt", "Int"),
                ("valueDecimal", "Decimal"),
                ("valueCode", "String"),
                ("valueUri", "String"),
                ("valueUrl", "String"),
                ("valueDate", "FhirDate"),
                ("valueDateTime", "FhirDateTime"),
                ("valueInstant", "FhirInstant"),
                ("valueTime", "FhirTime"),
            })
            {
                var capturedName = valueName;
                descriptor.Field(GraphQlNamingHelper.ToCamelCase(capturedName))
                    .Type(new NamedTypeNode(graphQlType))
                    .Resolve(ctx => FhirFieldResolver.ResolveField(ctx, capturedName));
            }

            // Complex value types that reference known types
            foreach (var complexValueName in ComplexExtensionValueTypes)
            {
                var capturedName = complexValueName;
                var typeName = MapFhirTypeToGraphQl(capturedName[5..]); // Remove "value" prefix, map Reference → ResourceReference
                descriptor.Field(GraphQlNamingHelper.ToCamelCase(capturedName))
                    .Type(new NamedTypeNode(typeName))
                    .Resolve(ctx => FhirFieldResolver.ResolveRawJsonField(ctx, capturedName));
            }
        }));
    }

    private ObjectType BuildResourceObjectType(
        string resourceTypeName,
        FhirIType fhirType,
        IReadOnlyList<string> allResourceTypes)
    {
        return new ObjectType(descriptor =>
        {
            descriptor.Name(resourceTypeName);
            descriptor.Description($"FHIR {resourceTypeName} resource");

            descriptor.Field("resourceType")
                .Type<NonNullType<StringType>>()
                .Resolve(_ => resourceTypeName);

            descriptor.IsOfType((_, obj) =>
                obj is JsonElement je && je.TryGetProperty("resourceType", out var rt)
                    && rt.GetString() == resourceTypeName);

            if (fhirType is FhirITypeExtended extended)
            {
                foreach (var child in extended.Children)
                    AddFieldForElement(descriptor, child, resourceTypeName);
            }

            // Reverse reference fields for instance-level queries
            foreach (var otherType in allResourceTypes)
            {
                var capturedOtherType = otherType;

                var listField = descriptor.Field($"{capturedOtherType}List")
                    .Type(new ListTypeNode(new NamedTypeNode(capturedOtherType)))
                    .Argument("_reference", a => a.Type<NonNullType<StringType>>()
                        .Description("Search parameter on the target type that references this resource"));
                AddSearchArguments(listField);
                AddResourceSearchArguments(listField, capturedOtherType);
                listField.Resolve(async ctx =>
                {
                    var parent = ctx.Parent<JsonElement>();
                    if (!parent.TryGetProperty("resourceType", out var rtProp)
                        || rtProp.GetString() is not string rt)
                    {
                        return null;
                    }

                    if (!parent.TryGetProperty("id", out var idProp)
                        || idProp.GetString() is not string id)
                    {
                        return null;
                    }

                    var referenceParam = ctx.ArgumentValue<string>("_reference");
                    var resolver = ctx.Service<AppSearchResolver>();
                    return await resolver.SearchReverseListAsync(
                        capturedOtherType, referenceParam, rt, id, ctx, ctx.RequestAborted);
                });

                var connectionField = descriptor.Field($"{capturedOtherType}Connection")
                    .Type(new NamedTypeNode(GraphQlNamingHelper.ToConnectionTypeName(capturedOtherType)))
                    .Argument("_reference", a => a.Type<NonNullType<StringType>>()
                        .Description("Search parameter on the target type that references this resource"));
                AddSearchArguments(connectionField);
                AddResourceSearchArguments(connectionField, capturedOtherType);
                connectionField.Resolve(async ctx =>
                {
                    var parent = ctx.Parent<JsonElement>();
                    if (!parent.TryGetProperty("resourceType", out var rtProp)
                        || rtProp.GetString() is not string rt)
                    {
                            return null;
                        }

                        if (!parent.TryGetProperty("id", out var idProp)
                            || idProp.GetString() is not string id)
                        {
                            return null;
                        }

                        var referenceParam = ctx.ArgumentValue<string>("_reference");
                        var resolver = ctx.Service<AppSearchResolver>();
                        return await resolver.SearchReverseAsync(
                            capturedOtherType, referenceParam, rt, id, ctx, ctx.RequestAborted);
                    });
            }
        });
    }

    private void AddFieldForElement(
        IObjectTypeDescriptor descriptor,
        FhirIType child,
        string parentPath)
    {
        var elementName = child.Info.Name;

        if (child.Info.IsChoiceElement && child is FhirITypeExtended choiceExtended)
        {
            AddChoiceElementField(descriptor, choiceExtended, elementName);
            return;
        }

        if (child is FhirITypeExtended ext)
        {
            var typeName = ext.Types.Count > 0 ? ext.Types[0].Code : null;

            if (typeName == "Reference")
            {
                AddReferenceField(descriptor, child, elementName);
                return;
            }

            if (child.Info.IsPrimitive)
            {
                var graphQlTypeNode = FhirScalarMappings.GetGraphQlTypeNode(
                    child.Info.Primitive.ToTypeString());
                var primitiveField = descriptor.Field(GraphQlNamingHelper.ToCamelCase(elementName))
                    .Type(ApplyCardinality(graphQlTypeNode, child));
                primitiveField.Resolve(ctx => FhirFieldResolver.ResolveField(ctx, elementName));

                var companionFieldName = $"_{GraphQlNamingHelper.ToCamelCase(elementName)}";
                var companionField = descriptor.Field(companionFieldName)
                    .Type(ApplyCardinality(new NamedTypeNode("Element"), child));
                companionField.Resolve(ctx =>
                {
                    var parent = FhirFieldResolver.GetParentElement(ctx);
                    if (parent?.ValueKind != JsonValueKind.Object) return null;
                    return parent.Value.TryGetProperty($"_{elementName}", out var value) ? value : null;
                });

                return;
            }

            if (child.Children.Count > 0 && (typeName is null || IsAbstractBaseType(typeName) || !schemaProvider.IsKnownType(typeName)))
            {
                var nestedTypeName = GraphQlNamingHelper.ToBackboneTypeName(parentPath, elementName);

                var backboneField = descriptor.Field(GraphQlNamingHelper.ToCamelCase(elementName))
                    .Type(ApplyCardinality(new NamedTypeNode(nestedTypeName), child));
                if (child.IsCollection)
                {
                    backboneField.Resolve(ctx => FhirFieldResolver.ResolveFilteredList(ctx, elementName, nestedTypeName));
                    AddListNavigationArguments(backboneField);
                    AddSubPropertyFilterArguments(backboneField, (FhirITypeExtended)child);
                }
                else
                {
                    backboneField.Resolve(ctx => FhirFieldResolver.ResolveRawJsonField(ctx, elementName));
                }

                return;
            }

            if (typeName is not null && schemaProvider.IsKnownType(typeName))
            {
                var complexField = descriptor.Field(GraphQlNamingHelper.ToCamelCase(elementName))
                    .Type(ApplyCardinality(new NamedTypeNode(MapFhirTypeToGraphQl(typeName)), child));

                // Add url filter argument to extension fields (FHIR GraphQL spec: extension(url: "..."))
                if (elementName == "extension" && typeName == "Extension")
                {
                    complexField.Argument("url", a => a.Type<StringType>()
                        .Description("Filter extensions by their canonical URL"));
                    if (child.IsCollection)
                    {
                        complexField.Resolve(ctx =>
                        {
                            var urlFilter = ctx.ArgumentOptional<string?>("url");
                            var url = urlFilter.HasValue ? urlFilter.Value : null;
                            var parent = FhirFieldResolver.GetParentElement(ctx);
                            if (parent?.ValueKind != JsonValueKind.Object)
                                return [];
                            return FhirFieldResolver.FilterExtensionsByUrl(parent.Value, url);
                        });
                        AddListNavigationArguments(complexField);
                        AddSubPropertyFilterArguments(complexField, (FhirITypeExtended)child, new HashSet<string>(StringComparer.Ordinal) { "url" });
                    }
                    else
                    {
                        complexField.Resolve(ctx => FhirFieldResolver.ResolveRawJsonField(ctx, elementName));
                    }
                }
                else if (child.IsCollection)
                {
                    complexField.Resolve(ctx => FhirFieldResolver.ResolveFilteredList(ctx, elementName, typeName));
                    AddListNavigationArguments(complexField);
                    var filterTypeDef = child.Children.Count > 0
                        ? (FhirITypeExtended)child
                        : schemaProvider.GetTypeDefinition(typeName) as FhirITypeExtended;
                    if (filterTypeDef is not null)
                        AddSubPropertyFilterArguments(complexField, filterTypeDef);
                }
                else
                {
                    complexField.Resolve(ctx => FhirFieldResolver.ResolveRawJsonField(ctx, elementName));
                }

                return;
            }
        }
        else if (child.Info.IsPrimitive)
        {
            var graphQlTypeNode = FhirScalarMappings.GetGraphQlTypeNode(
                child.Info.Primitive.ToTypeString());
            var primitiveField = descriptor.Field(GraphQlNamingHelper.ToCamelCase(elementName))
                .Type(ApplyCardinality(graphQlTypeNode, child));
            primitiveField.Resolve(ctx => FhirFieldResolver.ResolveField(ctx, elementName));

            var companionFieldName = $"_{GraphQlNamingHelper.ToCamelCase(elementName)}";
            var companionField = descriptor.Field(companionFieldName)
                .Type(ApplyCardinality(new NamedTypeNode("Element"), child));
            companionField.Resolve(ctx =>
            {
                var parent = FhirFieldResolver.GetParentElement(ctx);
                if (parent?.ValueKind != JsonValueKind.Object) return null;
                return parent.Value.TryGetProperty($"_{elementName}", out var value) ? value : null;
            });
        }
    }

    private void AddChoiceElementField(
        IObjectTypeDescriptor descriptor,
        FhirITypeExtended element,
        string elementName)
    {
        var camelName = GraphQlNamingHelper.ToCamelCase(elementName);
        var emitted = new HashSet<string>(StringComparer.Ordinal);

        foreach (var memberType in element.Types)
        {
            var code = memberType.Code;
            if (string.IsNullOrEmpty(code) || !schemaProvider.IsKnownType(code))
                continue;

            var fieldName = $"{camelName}{GraphQlNamingHelper.ToPascalCase(code)}";
            if (!emitted.Add(fieldName))
                continue;

            AddChoiceMemberField(descriptor, fieldName, code);
        }
    }

    private void AddChoiceMemberField(
        IObjectTypeDescriptor descriptor,
        string fieldName,
        string typeCode)
    {
        if (IsPrimitiveTypeCode(typeCode))
        {
            var graphQlTypeNode = FhirScalarMappings.GetGraphQlTypeNode(typeCode);
            descriptor.Field(fieldName)
                .Type(graphQlTypeNode)
                .Resolve(ctx => FhirFieldResolver.ResolveField(ctx, fieldName));

            var companionFieldName = $"_{fieldName}";
            descriptor.Field(companionFieldName)
                .Type(new NamedTypeNode("Element"))
                .Resolve(ctx =>
                {
                    var parent = FhirFieldResolver.GetParentElement(ctx);
                    if (parent?.ValueKind != JsonValueKind.Object) return null;
                    return parent.Value.TryGetProperty(companionFieldName, out var value) ? value : null;
                });

            return;
        }

        descriptor.Field(fieldName)
            .Type(new NamedTypeNode(MapFhirTypeToGraphQl(typeCode)))
            .Resolve(ctx => FhirFieldResolver.ResolveRawJsonField(ctx, fieldName));
    }

    private bool IsPrimitiveTypeCode(string typeCode)
    {
        var def = schemaProvider.GetTypeDefinition(typeCode);
        return def is not null && def.Info.IsPrimitive;
    }

    private static void AddReferenceField(
        IObjectTypeDescriptor descriptor,
        FhirIType child,
        string elementName)
    {
        var field = descriptor.Field(GraphQlNamingHelper.ToCamelCase(elementName))
            .Type(ApplyCardinality(new NamedTypeNode("ResourceReference"), child));
        if (child.IsCollection)
        {
            field.Resolve(ctx => FhirFieldResolver.ResolveFilteredList(ctx, elementName, "Reference"));
            AddListNavigationArguments(field);
        }
        else
        {
            field.Resolve(ctx => FhirFieldResolver.ResolveRawJsonField(ctx, elementName));
        }
    }

    private ObjectType BuildDataTypeObjectType(
        string typeName,
        FhirIType fhirType)
    {
        return new ObjectType(descriptor =>
        {
            descriptor.Name(typeName);
            descriptor.Description($"FHIR {typeName} datatype");

            descriptor.IsOfType((_, obj) => obj is JsonElement);

            if (fhirType is FhirITypeExtended extended)
            {
                foreach (var child in extended.Children)
                    AddFieldForElement(descriptor, child, typeName);
            }
        });
    }

    private ObjectType BuildPreDiscoveredBackboneType(string typeName, FhirIType fhirType)
    {
        return new ObjectType(descriptor =>
        {
            descriptor.Name(typeName);

            if (fhirType is FhirITypeExtended extended)
            {
                foreach (var child in extended.Children)
                    AddFieldForElement(descriptor, child, typeName);
            }
        });
    }

    private ObjectType BuildResourceReferenceType()
    {
        return new ObjectType(descriptor =>
        {
            descriptor.Name("ResourceReference");

            descriptor.Field("reference").Type<StringType>()
                .Resolve(ctx => FhirFieldResolver.GetStringProperty(ctx.Parent<JsonElement>(), "reference"));

            descriptor.Field("type").Type<StringType>()
                .Resolve(ctx => FhirFieldResolver.GetStringProperty(ctx.Parent<JsonElement>(), "type"));

            descriptor.Field("display").Type<StringType>()
                .Resolve(ctx => FhirFieldResolver.GetStringProperty(ctx.Parent<JsonElement>(), "display"));

            descriptor.Field("resource").Type(new NamedTypeNode("Resource"))
                .Argument("optional", a => a.Type<BooleanType>().DefaultValue(false)
                    .Description("If true, unresolvable references return null instead of an error"))
                .Argument("type", a => a.Type<StringType>()
                    .Description("Only resolve if the referenced resource matches this type"))
                .Resolve(async ctx =>
                {
                    var parent = ctx.Parent<JsonElement>();
                    var reference = FhirFieldResolver.GetStringProperty(parent, "reference");

                    var typeFilterOpt = ctx.ArgumentOptional<string?>("type");
                    var typeFilter = typeFilterOpt.HasValue ? typeFilterOpt.Value : null;

                    var dataLoader = ctx.DataLoader<ResourceDataLoader>();

                    var isOptional = IsOptionalReference(ctx);

                    var resolution = await ReferenceResolver.ResolveAsync(
                        reference,
                        isOptional,
                        typeFilter,
                        _referenceParser,
                        key => dataLoader.LoadAsync(key, ctx.RequestAborted),
                        ctx.RequestAborted);

                    if (ReferenceResolver.ShouldReportError(resolution.Outcome, isOptional))
                    {
                        ctx.ReportError(resolution.Outcome switch
                        {
                            ReferenceResolver.Outcome.NotSupported => ErrorBuilder.New()
                                .SetMessage($"Reference '{(string.IsNullOrEmpty(reference) ? "<empty>" : reference)}' could not be resolved: {ReferenceResolver.DescribeUnsupported(reference)}")
                                .SetCode("FHIR_REFERENCE_NOT_SUPPORTED")
                                .SetPath(ctx.Path)
                                .Build(),
                            _ => ErrorBuilder.New()
                                .SetMessage($"Reference '{reference}' could not be resolved")
                                .SetCode("FHIR_REFERENCE_NOT_FOUND")
                                .SetPath(ctx.Path)
                                .Build(),
                        });
                    }

                    return resolution.Resource;
                });
        });
    }

    private static bool IsOptionalReference(IResolverContext ctx)
    {
        var isOptional = ctx.ArgumentOptional<bool?>("optional");
        return isOptional.HasValue && isOptional.Value == true;
    }

    private static UnionType BuildResourceUnionType(IReadOnlyList<string> resourceTypes)
    {
        return new UnionType(descriptor =>
        {
            descriptor.Name("Resource");
            descriptor.Description("Union of all concrete FHIR resource types");
            foreach (var resourceType in resourceTypes)
                descriptor.Type(new NamedTypeNode(resourceType));
        });
    }

    private static ObjectType BuildConnectionType(string resourceTypeName)
    {
        return new ObjectType(descriptor =>
        {
            descriptor.Name(GraphQlNamingHelper.ToConnectionTypeName(resourceTypeName));
            descriptor.Description($"Paginated connection result for {resourceTypeName}");

            descriptor.Field("count").Type<IntType>()
                .Resolve(ctx => ctx.Parent<SearchConnectionResult>().Count);

            descriptor.Field("offset").Type<IntType>()
                .Resolve(ctx => ctx.Parent<SearchConnectionResult>().Offset);

            descriptor.Field("pagesize").Type<IntType>()
                .Resolve(ctx => ctx.Parent<SearchConnectionResult>().Pagesize);

            descriptor.Field("edges")
                .Type(new ListTypeNode(new NonNullTypeNode(
                    new NamedTypeNode(GraphQlNamingHelper.ToEdgeTypeName(resourceTypeName)))))
                .Resolve(ctx => ctx.Parent<SearchConnectionResult>().Edges);

            descriptor.Field("first").Type<StringType>()
                .Resolve(ctx => ctx.Parent<SearchConnectionResult>().First);

            descriptor.Field("previous").Type<StringType>()
                .Resolve(ctx => ctx.Parent<SearchConnectionResult>().Previous);

            descriptor.Field("next").Type<StringType>()
                .Resolve(ctx => ctx.Parent<SearchConnectionResult>().Next);

            descriptor.Field("last").Type<StringType>()
                .Resolve(ctx => ctx.Parent<SearchConnectionResult>().Last);
        });
    }

    private static ObjectType BuildEdgeType(string resourceTypeName)
    {
        return new ObjectType(descriptor =>
        {
            descriptor.Name(GraphQlNamingHelper.ToEdgeTypeName(resourceTypeName));

            descriptor.Field("mode").Type<StringType>()
                .Resolve(ctx => ctx.Parent<SearchEdge>().Mode);

            descriptor.Field("score").Type<DecimalType>()
                .Resolve(ctx => ctx.Parent<SearchEdge>().Score);

            descriptor.Field("resource").Type(new NamedTypeNode(resourceTypeName))
                .Resolve(ctx => ctx.Parent<SearchEdge>().Resource);
        });
    }

    private ObjectType BuildQueryType(IReadOnlyList<string> concreteResourceTypes)
    {
        return new ObjectType(descriptor =>
        {
            descriptor.Name("Query");

            foreach (var resourceType in concreteResourceTypes)
            {
                var capturedType = resourceType;

                // Single resource read: Patient(_id: "p1")
                descriptor.Field(capturedType)
                    .Argument("_id", a => a.Type<NonNullType<IdType>>())
                    .Type(new NamedTypeNode(capturedType))
                    .Resolve(async ctx =>
                    {
                        var id = ctx.ArgumentValue<string>("_id");
                        var resolver = ctx.Service<AppResourceResolver>();
                        return await resolver.ResolveByIdAsync(capturedType, id, ctx.RequestAborted);
                    });

                // Simple list search: PatientList(name: "Smith") → [Patient]
                // Supports reverse reference: PatientList(_reference: "subject") at instance level
                var listFieldName = $"{capturedType}List";
                var listField = descriptor.Field(listFieldName)
                    .Type(new ListTypeNode(new NamedTypeNode(capturedType)));
                listField.Argument("_reference", a => a.Type<StringType>()
                    .Description("Reverse reference: search parameter on this type that references the instance resource"));
                AddSearchArguments(listField);
                AddResourceSearchArguments(listField, capturedType);
                listField.Resolve(async ctx =>
                {
                    var resolver = ctx.Service<AppSearchResolver>();
                    var referenceParam = ctx.ArgumentOptional<string?>("_reference");
                    if (referenceParam.HasValue && !string.IsNullOrEmpty(referenceParam.Value))
                    {
                        var instanceType = ctx.GetGlobalStateOrDefault<string?>("InstanceResourceType");
                        var instanceId = ctx.GetGlobalStateOrDefault<string?>("InstanceResourceId");
                        if (instanceType is not null && instanceId is not null)
                        {
                            return await resolver.SearchReverseListAsync(
                                capturedType, referenceParam.Value, instanceType, instanceId, ctx, ctx.RequestAborted);
                        }
                        else
                        {
                            ctx.ReportError(
                                ErrorBuilder.New()
                                    .SetMessage("_reference requires an instance-level query (e.g., /Patient/123/$graphql)")
                                    .SetCode("FHIR_REFERENCE_REQUIRES_INSTANCE")
                                    .SetPath(ctx.Path)
                                    .Build());
                            return null;
                        }
                    }

                    return await resolver.SearchListAsync(capturedType, ctx, ctx.RequestAborted);
                });

                // Connection search: PatientConnection(name: "Smith") → paginated
                // Supports reverse reference: PatientConnection(_reference: "subject") at instance level
                var connectionFieldName = $"{capturedType}Connection";
                var connectionField = descriptor.Field(connectionFieldName)
                    .Type(new NamedTypeNode(GraphQlNamingHelper.ToConnectionTypeName(capturedType)));
                connectionField.Argument("_reference", a => a.Type<StringType>()
                    .Description("Reverse reference: search parameter on this type that references the instance resource"));
                AddSearchArguments(connectionField);
                AddResourceSearchArguments(connectionField, capturedType);
                connectionField.Resolve(async ctx =>
                {
                    var resolver = ctx.Service<AppSearchResolver>();
                    var referenceParam = ctx.ArgumentOptional<string?>("_reference");
                    if (referenceParam.HasValue && !string.IsNullOrEmpty(referenceParam.Value))
                    {
                        var instanceType = ctx.GetGlobalStateOrDefault<string?>("InstanceResourceType");
                        var instanceId = ctx.GetGlobalStateOrDefault<string?>("InstanceResourceId");
                        if (instanceType is not null && instanceId is not null)
                        {
                            return await resolver.SearchReverseAsync(
                                capturedType, referenceParam.Value, instanceType, instanceId, ctx, ctx.RequestAborted);
                        }
                        else
                        {
                            ctx.ReportError(
                                ErrorBuilder.New()
                                    .SetMessage("_reference requires an instance-level query (e.g., /Patient/123/$graphql)")
                                    .SetCode("FHIR_REFERENCE_REQUIRES_INSTANCE")
                                    .SetPath(ctx.Path)
                                    .Build());
                            return null;
                        }
                    }

                    return await resolver.SearchAsync(capturedType, ctx, ctx.RequestAborted);
                });
            }
        });
    }

    private ObjectType BuildMutationType(IReadOnlyList<string> concreteResourceTypes)
    {
        return new ObjectType(descriptor =>
        {
            descriptor.Name("Mutation");

            foreach (var resourceType in concreteResourceTypes)
            {
                var capturedType = resourceType;

                // PatientCreate(res: String!) → Patient
                descriptor.Field($"{capturedType}Create")
                    .Argument("res", a => a.Type<NonNullType<StringType>>()
                        .Description("JSON representation of the FHIR resource to create"))
                    .Type(new NamedTypeNode(capturedType))
                    .Resolve(async ctx =>
                    {
                        var json = ctx.ArgumentValue<string>("res");
                        var resolver = ctx.Service<AppMutationResolver>();
                        return await resolver.CreateAsync(capturedType, json, ctx.RequestAborted);
                    });

                // PatientUpdate(id: ID!, res: String!) → Patient
                descriptor.Field($"{capturedType}Update")
                    .Argument("id", a => a.Type<NonNullType<IdType>>())
                    .Argument("res", a => a.Type<NonNullType<StringType>>()
                        .Description("JSON representation of the FHIR resource to update"))
                    .Type(new NamedTypeNode(capturedType))
                    .Resolve(async ctx =>
                    {
                        var id = ctx.ArgumentValue<string>("id");
                        var json = ctx.ArgumentValue<string>("res");
                        var resolver = ctx.Service<AppMutationResolver>();
                        return await resolver.UpdateAsync(capturedType, id, json, ctx.RequestAborted);
                    });

                // PatientDelete(id: ID!) → Boolean
                descriptor.Field($"{capturedType}Delete")
                    .Argument("id", a => a.Type<NonNullType<IdType>>())
                    .Type<BooleanType>()
                    .Resolve(async ctx =>
                    {
                        var id = ctx.ArgumentValue<string>("id");
                        var resolver = ctx.Service<AppMutationResolver>();
                        return await resolver.DeleteAsync(capturedType, id, ctx.RequestAborted);
                    });
            }
        });
    }

    private static void AddSearchArguments(IObjectFieldDescriptor fieldDescriptor)
    {
        fieldDescriptor.Argument("_count", a => a.Type<IntType>()
            .Description("Page size (default: 10, max: 1000)"));
        fieldDescriptor.Argument("_cursor", a => a.Type<StringType>()
            .Description("Continuation cursor from previous page's link.next"));
        fieldDescriptor.Argument("_sort", a => a.Type<ListType<StringType>>()
            .Description("Sort criteria (e.g., \"-date\", \"name\")"));
        fieldDescriptor.Argument("_total", a => a.Type<StringType>()
            .Description("Total count mode: none | estimate | accurate"));
    }

    private void AddResourceSearchArguments(
        IObjectFieldDescriptor fieldDescriptor,
        string resourceType)
    {
        if (!searchParameterManager.TryGetSearchParameters(resourceType, out var searchParams))
            return;

        var skipParams = new HashSet<string>(StringComparer.Ordinal)
        {
            "_count", "_cursor", "_sort", "_total",
            "_include", "_revinclude", "_contained", "_containedType",
        };

        foreach (var param in searchParams)
        {
            if (skipParams.Contains(param.Code))
                continue;

            var graphQlName = param.Code.Replace('-', '_');
            fieldDescriptor.Argument(graphQlName, a => a.Type<ListType<StringType>>()
                .Description(string.IsNullOrEmpty(param.Description)
                    ? $"FHIR search parameter: {param.Code}"
                    : param.Description));
        }
    }

    private static void AddListNavigationArguments(IObjectFieldDescriptor fieldDescriptor)
    {
        fieldDescriptor.Argument("fhirpath", a => a.Type<StringType>()
            .Description("FHIRPath expression to filter list elements"));
        fieldDescriptor.Argument("_offset", a => a.Type<IntType>()
            .Description("Number of elements to skip"));
        fieldDescriptor.Argument("_count", a => a.Type<IntType>()
            .Description("Maximum number of elements to return from this list"));
    }

    private static void AddSubPropertyFilterArguments(
        IObjectFieldDescriptor fieldDescriptor,
        FhirITypeExtended typeDefinition,
        IReadOnlySet<string>? existingArgumentNames = null)
    {
        var skip = new HashSet<string>(existingArgumentNames ?? new HashSet<string>(StringComparer.Ordinal), StringComparer.Ordinal)
        {
            "fhirpath", "_offset", "_count",
        };

        foreach (var subChild in typeDefinition.Children)
        {
            if (!subChild.Info.IsPrimitive) continue;
            var argName = GraphQlNamingHelper.ToCamelCase(subChild.Info.Name);
            if (skip.Contains(argName)) continue;
            fieldDescriptor.Argument(argName, a => a.Type<StringType>()
                .Description($"Filter by {subChild.Info.Name}"));
        }
    }

    private static ITypeNode ApplyCardinality(INullableTypeNode baseType, FhirIType child)
    {
        if (child.IsCollection)
            return new ListTypeNode(baseType);

        if (child.IsRequired)
            return new NonNullTypeNode(baseType);

        return baseType;
    }

    private static string MapFhirTypeToGraphQl(string fhirTypeCode) => fhirTypeCode switch
    {
        "Reference" => "ResourceReference",
        "BackboneElement" or "Element" or "Base" or "DataType" or "PrimitiveType" => "Element",
        "Resource" or "DomainResource" => "Element",
        "xhtml" => "String",
        _ => fhirTypeCode,
    };
}
