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
using Microsoft.Extensions.Logging;
using FhirIType = Ignixa.Abstractions.IType;
using FhirITypeExtended = Ignixa.Abstractions.ITypeExtended;
using FhirITypeReference = Ignixa.Abstractions.ITypeReference;
using FhirIFhirSchemaProvider = Ignixa.Abstractions.IFhirSchemaProvider;
using FhirFieldResolver = Ignixa.Application.Features.Experimental.GraphQl.Resolvers.FieldResolver;
using AppResourceResolver = Ignixa.Application.Features.Experimental.GraphQl.Resolvers.ResourceResolver;
using AppSearchResolver = Ignixa.Application.Features.Experimental.GraphQl.Resolvers.SearchResolver;

namespace Ignixa.Application.Features.Experimental.GraphQl.Schema;

public sealed class FhirTypeModule(
    FhirIFhirSchemaProvider schemaProvider,
    ISearchParameterDefinitionManager searchParameterManager,
    ILogger<FhirTypeModule> logger) : ITypeModule, IFhirTypeModule
{
    public event EventHandler<EventArgs>? TypesChanged;

    public ValueTask<IReadOnlyCollection<ITypeSystemMember>> CreateTypesAsync(
        IDescriptorContext context,
        CancellationToken cancellationToken)
    {
        var nestedTypes = new List<ITypeSystemMember>();
        var types = new List<ITypeSystemMember>();

        EmitFhirScalars(types);

        var concreteResourceTypes = GetConcreteResourceTypes();

        foreach (var resourceTypeName in concreteResourceTypes)
        {
            var fhirType = schemaProvider.GetTypeDefinition(resourceTypeName);
            if (fhirType is null) continue;
            types.Add(BuildResourceObjectType(resourceTypeName, fhirType, nestedTypes));
        }

        types.Add(BuildResourceReferenceType());
        types.Add(BuildResourceUnionType(concreteResourceTypes));

        foreach (var resourceTypeName in concreteResourceTypes)
            types.Add(BuildConnectionType(resourceTypeName));

        foreach (var resourceTypeName in concreteResourceTypes)
            types.Add(BuildEdgeType(resourceTypeName));

        types.Add(BuildQueryType(concreteResourceTypes));

        types.AddRange(nestedTypes);

        logger.LogInformation(
            "FhirTypeModule generated {TypeCount} GraphQL types for FHIR {Version}",
            types.Count, schemaProvider.Version);

        return ValueTask.FromResult<IReadOnlyCollection<ITypeSystemMember>>(types);
    }

    public void NotifyTypesChanged() => TypesChanged?.Invoke(this, EventArgs.Empty);

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

    private ObjectType BuildResourceObjectType(
        string resourceTypeName,
        FhirIType fhirType,
        List<ITypeSystemMember> nestedTypes)
    {
        return new ObjectType(descriptor =>
        {
            descriptor.Name(resourceTypeName);
            descriptor.Description($"FHIR {resourceTypeName} resource");

            descriptor.Field("resourceType")
                .Type<NonNullType<StringType>>()
                .Resolve(_ => resourceTypeName);

            descriptor.IsOfType((ctx, obj) =>
                obj is ChoiceElementValue cv && cv.TypeName == resourceTypeName
                || obj is JsonElement je && je.TryGetProperty("resourceType", out var rt)
                    && rt.GetString() == resourceTypeName);

            if (fhirType is FhirITypeExtended extended)
            {
                foreach (var child in extended.Children)
                    AddFieldForElement(descriptor, child, resourceTypeName, nestedTypes);
            }
        });
    }

    private void AddFieldForElement(
        IObjectTypeDescriptor descriptor,
        FhirIType child,
        string parentPath,
        List<ITypeSystemMember> nestedTypes)
    {
        var elementName = child.Info.Name;

        if (child.Info.IsChoiceElement && child is FhirITypeExtended choiceExtended)
        {
            AddChoiceElementField(descriptor, choiceExtended, elementName, parentPath, nestedTypes);
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
                return;
            }

            if (child.Children.Count > 0 && (typeName is null || !schemaProvider.IsKnownType(typeName)))
            {
                var nestedTypeName = GraphQlNamingHelper.ToBackboneTypeName(parentPath, elementName);
                var nestedType = BuildNestedObjectType(nestedTypeName, child, nestedTypes);
                nestedTypes.Add(nestedType);

                var backboneField = descriptor.Field(GraphQlNamingHelper.ToCamelCase(elementName))
                    .Type(ApplyCardinality(new NamedTypeNode(nestedTypeName), child));
                backboneField.Resolve(ctx => FhirFieldResolver.ResolveRawJsonField(ctx, elementName));
                return;
            }

            if (typeName is not null && schemaProvider.IsKnownType(typeName))
            {
                var complexField = descriptor.Field(GraphQlNamingHelper.ToCamelCase(elementName))
                    .Type(ApplyCardinality(new NamedTypeNode(typeName), child));
                complexField.Resolve(ctx => FhirFieldResolver.ResolveRawJsonField(ctx, elementName));
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
        }
    }

    private void AddChoiceElementField(
        IObjectTypeDescriptor descriptor,
        FhirITypeExtended element,
        string elementName,
        string parentPath,
        List<ITypeSystemMember> nestedTypes)
    {
        var unionName = GraphQlNamingHelper.ToUnionTypeName(parentPath, elementName);

        var memberTypeCodes = element.Types
            .Select(t => t.Code)
            .Where(schemaProvider.IsKnownType)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (memberTypeCodes.Count == 0) return;

        var unionType = new UnionType(ud =>
        {
            ud.Name(unionName);
            ud.Description($"Choice type for {parentPath}.{elementName}[x]");
            foreach (var code in memberTypeCodes)
                ud.Type(new NamedTypeNode(code));
        });
        nestedTypes.Add(unionType);

        var memberTypes = element.Types.ToList();
        var fieldName = GraphQlNamingHelper.ToCamelCase(elementName);
        var field = descriptor.Field(fieldName).Type(new NamedTypeNode(unionName));
        field.Resolve(ctx => ResolveChoiceElement(ctx, elementName, memberTypes));
    }

    private static void AddReferenceField(
        IObjectTypeDescriptor descriptor,
        FhirIType child,
        string elementName)
    {
        var field = descriptor.Field(GraphQlNamingHelper.ToCamelCase(elementName))
            .Type(ApplyCardinality(new NamedTypeNode("ResourceReference"), child));
        field.Resolve(ctx => FhirFieldResolver.ResolveRawJsonField(ctx, elementName));
    }

    private ObjectType BuildNestedObjectType(
        string typeName,
        FhirIType fhirType,
        List<ITypeSystemMember> nestedTypes)
    {
        return new ObjectType(descriptor =>
        {
            descriptor.Name(typeName);

            if (fhirType is FhirITypeExtended extended)
            {
                foreach (var child in extended.Children)
                    AddFieldForElement(descriptor, child, typeName, nestedTypes);
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
                    var key = FhirFieldResolver.ParseFhirReference(reference);
                    if (key is null)
                        return null;

                    var typeFilter = ctx.ArgumentOptional<string?>("type");
                    if (typeFilter.HasValue && typeFilter.Value is not null
                        && !string.Equals(key.ResourceType, typeFilter.Value, StringComparison.Ordinal))
                    {
                        return null;
                    }

                    var dataLoader = ctx.DataLoader<ResourceDataLoader>();
                    var result = await dataLoader.LoadAsync(key, ctx.RequestAborted);

                    if (result is null)
                    {
                        var isOptional = ctx.ArgumentOptional<bool?>("optional");
                        if (!isOptional.HasValue || isOptional.Value != true)
                        {
                            ctx.ReportError(
                                ErrorBuilder.New()
                                    .SetMessage($"Reference '{reference}' could not be resolved")
                                    .SetCode("FHIR_REFERENCE_NOT_FOUND")
                                    .SetPath(ctx.Path)
                                    .Build());
                        }
                    }

                    return result;
                });
        });
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

                // Single resource read: Patient(id: "p1")
                descriptor.Field(capturedType)
                    .Argument("id", a => a.Type<NonNullType<IdType>>())
                    .Type(new NamedTypeNode(capturedType))
                    .Resolve(async ctx =>
                    {
                        var id = ctx.ArgumentValue<string>("id");
                        var resolver = ctx.Service<AppResourceResolver>();
                        return await resolver.ResolveByIdAsync(capturedType, id, ctx.RequestAborted);
                    });

                // Simple list search: PatientList(name: "Smith") → [Patient]
                var listFieldName = $"{capturedType}List";
                var listField = descriptor.Field(listFieldName)
                    .Type(new ListTypeNode(new NamedTypeNode(capturedType)));
                AddSearchArguments(listField);
                AddResourceSearchArguments(listField, capturedType);
                listField.Resolve(async ctx =>
                {
                    var resolver = ctx.Service<AppSearchResolver>();
                    return await resolver.SearchListAsync(capturedType, ctx, ctx.RequestAborted);
                });

                // Connection search: PatientConnection(name: "Smith") → paginated
                var connectionFieldName = $"{capturedType}Connection";
                var connectionField = descriptor.Field(connectionFieldName)
                    .Type(new NamedTypeNode(GraphQlNamingHelper.ToConnectionTypeName(capturedType)));
                AddSearchArguments(connectionField);
                AddResourceSearchArguments(connectionField, capturedType);
                connectionField.Resolve(async ctx =>
                {
                    var resolver = ctx.Service<AppSearchResolver>();
                    return await resolver.SearchAsync(capturedType, ctx, ctx.RequestAborted);
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
            fieldDescriptor.Argument(graphQlName, a => a.Type<StringType>()
                .Description(string.IsNullOrEmpty(param.Description)
                    ? $"FHIR search parameter: {param.Code}"
                    : param.Description));
        }
    }

    private static ChoiceElementValue? ResolveChoiceElement(
        IResolverContext ctx,
        string elementName,
        IReadOnlyList<FhirITypeReference> memberTypes)
    {
        var parent = ctx.Parent<JsonElement>();
        if (parent.ValueKind != JsonValueKind.Object) return null;

        var camelName = GraphQlNamingHelper.ToCamelCase(elementName);

        foreach (var memberType in memberTypes)
        {
            var code = memberType.Code;
            if (string.IsNullOrEmpty(code)) continue;

            var propertyName = $"{camelName}{char.ToUpperInvariant(code[0])}{code[1..]}";
            if (parent.TryGetProperty(propertyName, out var value) &&
                value.ValueKind != JsonValueKind.Null)
            {
                return new ChoiceElementValue(code, value);
            }
        }

        return null;
    }

    private static ITypeNode ApplyCardinality(INullableTypeNode baseType, FhirIType child)
    {
        if (child.IsCollection)
            return new ListTypeNode(baseType);

        if (child.IsRequired)
            return new NonNullTypeNode(baseType);

        return baseType;
    }
}
