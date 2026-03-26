// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using HotChocolate.Execution.Configuration;
using HotChocolate.Language;
using HotChocolate.Resolvers;
using HotChocolate.Types;
using HotChocolate.Types.Descriptors;
using Ignixa.Abstractions;
using Ignixa.Application.Features.Experimental.GraphQl.Contracts;
using Ignixa.Application.Features.Experimental.GraphQl.Models;
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

        types.Add(BuildPaginationLinksType());
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
                .Resolve(_ => (object?)null);
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

            descriptor.Field("entry")
                .Type(new ListTypeNode(new NonNullTypeNode(new NamedTypeNode(resourceTypeName))))
                .Resolve(ctx => ctx.Parent<SearchConnectionResult>().Entries);

            descriptor.Field("total").Type<IntType>()
                .Resolve(ctx => ctx.Parent<SearchConnectionResult>().Total);

            descriptor.Field("link").Type(new NamedTypeNode("PaginationLinks"))
                .Resolve(ctx => ctx.Parent<SearchConnectionResult>().Links);
        });
    }

    private static ObjectType BuildPaginationLinksType()
    {
        return new ObjectType(descriptor =>
        {
            descriptor.Name("PaginationLinks");

            descriptor.Field("next").Type<StringType>()
                .Resolve(ctx => ctx.Parent<PaginationLinks?>()?.Next);

            descriptor.Field("previous").Type<StringType>()
                .Resolve(ctx => ctx.Parent<PaginationLinks?>()?.Previous);

            descriptor.Field("self").Type<StringType>()
                .Resolve(ctx => ctx.Parent<PaginationLinks?>()?.Self);
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

                descriptor.Field(capturedType)
                    .Argument("id", a => a.Type<NonNullType<IdType>>())
                    .Type(new NamedTypeNode(capturedType))
                    .Resolve(async ctx =>
                    {
                        var id = ctx.ArgumentValue<string>("id");
                        var resolver = ctx.Service<AppResourceResolver>();
                        return await resolver.ResolveByIdAsync(capturedType, id, ctx.RequestAborted);
                    });

                var listFieldName = $"{capturedType}List";
                var listField = descriptor.Field(listFieldName)
                    .Type(new NamedTypeNode(GraphQlNamingHelper.ToConnectionTypeName(capturedType)));
                AddSearchArguments(listField);
                listField.Resolve(async ctx =>
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
        fieldDescriptor.Argument("_sort", a => a.Type<StringType>()
            .Description("Sort criteria (e.g., \"-date,name\")"));
        fieldDescriptor.Argument("_total", a => a.Type<StringType>()
            .Description("Total count mode: none | estimate | accurate"));
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
