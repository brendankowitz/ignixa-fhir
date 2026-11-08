// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Validation.Abstractions;

namespace Ignixa.Validation.Checks;

/// <summary>
/// The element an extension is used on, as an extension definition's contexts name it: its type, its element path from the
/// resource (<c>Patient.contact.name</c>, a choice element as <c>value[x]</c>), its path within the nearest enclosing
/// datatype (<c>HumanName.given</c>), or, for a sub-extension, the url of the extension it is nested in.
/// </summary>
/// <param name="InstanceType">The element's type (<c>Patient</c>, <c>HumanName</c>, <c>Patient.Contact</c>, <c>date</c>).</param>
/// <param name="DefinitionPath">The element path from the resource, as its definition names it; for an element that
/// reuses another's definition, the reused element (a nested <c>Questionnaire.item</c> is <c>Questionnaire.item</c>, not
/// <c>Questionnaire.item.item</c>), since the snapshot defines its children only there.</param>
/// <param name="AliasPath">Another path a context may name the element by: for a choice variant, the path with its concrete
/// name (<c>Observation.valueQuantity</c>); for an element that reuses another's definition, its own path
/// (<c>ValueSet.compose.exclude</c>, where <see cref="DefinitionPath"/> is <c>ValueSet.compose.include</c>).</param>
/// <param name="TypeRelativePath">The path within the nearest enclosing complex datatype, or null outside one.</param>
/// <param name="IsResource">Whether the element is the resource itself.</param>
/// <param name="IsExtension">Whether the element is an extension (so its extensions are sub-extensions).</param>
/// <param name="ExtensionUrl">For an extension, its url.</param>
/// <param name="InDefinedExtension">For an extension, whether it was validated against a loaded definition.</param>
internal sealed record ExtensionHost(
    string? InstanceType,
    string DefinitionPath,
    string? AliasPath,
    string? TypeRelativePath,
    bool IsResource,
    bool IsExtension,
    string? ExtensionUrl,
    bool InDefinedExtension)
{
    // Each concrete datatype that specialises another concrete one, with that base, so a context naming the base allows
    // it. The schema carries no base type, and these are the only such pairs (one level deep in STU3 to R5); abstract
    // bases (DataType, PrimitiveType, CanonicalResource) are not resolved, so see Decides for when they are reported unchecked.
    private static readonly Dictionary<string, string> Specializations = new(StringComparer.Ordinal)
    {
        ["Age"] = "Quantity", ["Count"] = "Quantity", ["Distance"] = "Quantity", ["Duration"] = "Quantity", ["MoneyQuantity"] = "Quantity", ["SimpleQuantity"] = "Quantity",
        ["code"] = "string", ["id"] = "string", ["markdown"] = "string",
        ["canonical"] = "uri", ["oid"] = "uri", ["url"] = "uri", ["uuid"] = "uri",
        ["positiveInt"] = "integer", ["unsignedInt"] = "integer",
    };

    // The abstract types an element context may name that Allows resolves on any element.
    private static readonly HashSet<string> ResolvedAbstractTypes = new(StringComparer.Ordinal) { "Base", "Element", "Resource", "DomainResource", "BackboneElement" };

    // Unresolved abstract types whose members are all resources, so they can allow only a resource root.
    private static readonly HashSet<string> ResourceAbstractTypes = new(StringComparer.Ordinal) { "CanonicalResource", "MetadataResource" };

    // Unresolved abstract types whose members are all datatypes, so they never allow a resource root.
    private static readonly HashSet<string> DataTypeAbstractTypes = new(StringComparer.Ordinal) { "DataType", "PrimitiveType", "BackboneType" };

    // The resources that are not DomainResources.
    private static readonly HashSet<string> NonDomainResources = new(StringComparer.Ordinal) { "Bundle", "Binary", "Parameters" };

    /// <summary>The resource itself.</summary>
    public static ExtensionHost ForResource(IElement resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        var type = resource.InstanceType ?? resource.Name;
        return new ExtensionHost(type, type, null, null, IsResource: true, IsExtension: false, ExtensionUrl: null, InDefinedExtension: false);
    }

    /// <summary>A child element of this one (not an extension).</summary>
    public ExtensionHost Child(IElement child)
    {
        var definition = child.Type;
        var name = definition is { Info.IsChoiceElement: true }
            ? $"{(definition.Info.Name.EndsWith("[x]", StringComparison.Ordinal) ? definition.Info.Name[..^3] : definition.Info.Name)}[x]"
            : child.Name;
        var ownPath = $"{DefinitionPath}.{name}";
        var reused = ContentReferenceTarget(definition);
        var datatype = child.InstanceType is { Length: > 0 } type && char.IsUpper(type[0]) && !type.Contains('.', StringComparison.Ordinal)
            && definition is not { Info.IsPrimitive: true } && type != "BackboneElement"
            ? type
            : null;
        return new ExtensionHost(
            child.InstanceType,
            reused ?? ownPath,
            reused is not null ? ownPath : name == child.Name ? null : $"{DefinitionPath}.{child.Name}",
            datatype ?? (TypeRelativePath is null ? null : $"{TypeRelativePath}.{name}"),
            IsResource: false,
            IsExtension: false,
            ExtensionUrl: null,
            InDefinedExtension: false);
    }

    /// <summary>An extension used on this element, as the host of its own sub-extensions.</summary>
    public ExtensionHost Extension(IElement extension, string? url, bool defined) =>
        new("Extension", $"{DefinitionPath}.{extension.Name}", null, "Extension", IsResource: false, IsExtension: true, url, defined);

    /// <summary>Whether <paramref name="context"/> allows an extension on this element; a FHIRPath context never does here.</summary>
    public bool Allows(ExtensionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var expression = context.Expression.Trim();
        return context.Type switch
        {
            ExtensionContextType.Element => AllowsElement(expression),
            ExtensionContextType.Extension => IsExtension && ExtensionUrl is { } url && Canonical(url) == Canonical(expression),
            _ => false,
        };
    }

    private bool AllowsElement(string expression) =>
        expression is "Element" or "Base"
        || expression == InstanceType
        || expression == DefinitionPath
        || expression == AliasPath
        || expression == TypeRelativePath
        || (expression == "Resource" && IsResource)
        || (expression == "DomainResource" && IsResource && InstanceType is { } resourceType && !NonDomainResources.Contains(resourceType))
        || (expression == "BackboneElement" && InstanceType is { } backbone && backbone.Contains('.', StringComparison.Ordinal))
        || (InstanceType is { } type && Specializations.TryGetValue(type, out var baseType) && expression == baseType);

    /// <summary>
    /// Whether <see cref="Allows"/> decides a context naming the abstract type <paramref name="expression"/> on this element:
    /// one it resolves, a resource-only one off a resource root (never allowed there), or a datatype-only one on a resource
    /// root (never allowed there). Otherwise the schema, which lists no members of an abstract type, cannot tell.
    /// </summary>
    public bool Decides(string expression) =>
        ResolvedAbstractTypes.Contains(expression)
        || (ResourceAbstractTypes.Contains(expression) && !IsResource)
        || (DataTypeAbstractTypes.Contains(expression) && IsResource);

    /// <summary>
    /// The element whose definition this one reuses through a contentReference: <c>#Questionnaire.item</c> for the recursive
    /// <c>Questionnaire.item.item</c>, <c>#ValueSet.compose.include</c> for <c>ValueSet.compose.exclude</c>.
    /// </summary>
    private static string? ContentReferenceTarget(IType? definition) =>
        definition is ITypeExtended { ContentReference: { Length: > 1 } reference }
            ? reference[(reference.IndexOf('#', StringComparison.Ordinal) + 1)..]
            : null;

    private static string Canonical(string url) => url.IndexOf('|', StringComparison.Ordinal) is var pipe and >= 0 ? url[..pipe] : url;
}
