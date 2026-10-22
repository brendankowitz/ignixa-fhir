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
/// <param name="DefinitionPath">The element path from the resource.</param>
/// <param name="ConcretePath">For a choice variant, the path with its concrete name (<c>Observation.valueQuantity</c>).</param>
/// <param name="TypeRelativePath">The path within the nearest enclosing complex datatype, or null outside one.</param>
/// <param name="IsResource">Whether the element is the resource itself.</param>
/// <param name="IsExtension">Whether the element is an extension (so its extensions are sub-extensions).</param>
/// <param name="ExtensionUrl">For an extension, its url.</param>
/// <param name="InDefinedExtension">For an extension, whether it was validated against a loaded definition.</param>
internal sealed record ExtensionHost(
    string? InstanceType,
    string DefinitionPath,
    string? ConcretePath,
    string? TypeRelativePath,
    bool IsResource,
    bool IsExtension,
    string? ExtensionUrl,
    bool InDefinedExtension)
{
    // R4 datatypes that specialise Quantity, so a context naming Quantity allows them.
    private static readonly HashSet<string> QuantitySpecializations = new(StringComparer.Ordinal) { "Age", "Count", "Distance", "Duration", "MoneyQuantity", "SimpleQuantity" };

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
        var datatype = child.InstanceType is { Length: > 0 } type && char.IsUpper(type[0]) && !type.Contains('.', StringComparison.Ordinal)
            && definition is not { Info.IsPrimitive: true } && type != "BackboneElement"
            ? type
            : null;
        return new ExtensionHost(
            child.InstanceType,
            $"{DefinitionPath}.{name}",
            name == child.Name ? null : $"{DefinitionPath}.{child.Name}",
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
        || expression == ConcretePath
        || expression == TypeRelativePath
        || (expression == "Resource" && IsResource)
        || (expression == "DomainResource" && IsResource && InstanceType is { } resourceType && !NonDomainResources.Contains(resourceType))
        || (expression == "BackboneElement" && InstanceType is { } backbone && backbone.Contains('.', StringComparison.Ordinal))
        || (expression == "Quantity" && InstanceType is { } quantity && QuantitySpecializations.Contains(quantity));

    private static string Canonical(string url) => url.IndexOf('|', StringComparison.Ordinal) is var pipe and >= 0 ? url[..pipe] : url;
}
