// <copyright file="ProfileAwareValidationSchemaResolver.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using Ignixa.Abstractions;
using Ignixa.Validation.Abstractions;

namespace Ignixa.Validation.Schema;

/// <summary>
/// Decorator-style resolver that reads <c>Resource.meta.profile</c> from an
/// <see cref="IElement"/>, looks up each profile URL via an inner
/// <see cref="IValidationSchemaResolver"/>, and composes the results with the
/// base StructureDefinition schema into a single <see cref="ValidationSchema"/>.
/// <para>
/// Profile URLs with a <c>|version</c> suffix are stripped before lookup; the inner
/// resolver is expected to key on unversioned canonicals (matching how
/// <c>StructureDefinitionSchemaResolver</c> and <c>PackageResourceRepository</c>
/// index profiles).
/// </para>
/// <para>
/// Unresolvable profile URLs are silently skipped (return value just omits them
/// from the composed schema). The validator should not fail catastrophically just
/// because a referenced profile package isn't loaded; this is consistent with how
/// the legacy MS FHIR Server treated missing profiles (warnings, not errors).
/// </para>
/// <para>
/// Also implements <see cref="IValidationSchemaResolver"/> by delegating
/// <see cref="GetSchema"/> to the wrapped inner resolver, so existing consumers
/// that resolve schemas by canonical URL keep working - they just lose the
/// profile-composition behaviour. Consumers that have access to the resource
/// element should prefer <see cref="ResolveForElement"/>.
/// </para>
/// </summary>
public sealed class ProfileAwareValidationSchemaResolver : IValidationSchemaResolver
{
    private readonly IValidationSchemaResolver _inner;

    public ProfileAwareValidationSchemaResolver(IValidationSchemaResolver inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    /// <summary>
    /// Delegates to the wrapped inner resolver. Provided so this class is a drop-in
    /// replacement for <see cref="IValidationSchemaResolver"/> in DI; callers that
    /// also need <c>meta.profile</c> composition should call <see cref="ResolveForElement"/>.
    /// </summary>
    public ValidationSchema? GetSchema(string canonicalUrl) => _inner.GetSchema(canonicalUrl);

    /// <summary>
    /// Resolves the validation schema for an element by walking its
    /// <c>resourceType</c> and <c>meta.profile</c>.
    /// </summary>
    /// <param name="element">The root resource element to inspect.</param>
    /// <returns>A composed schema, or <c>null</c> if the element has no resolvable resource type.</returns>
    public ValidationSchema? ResolveForElement(IElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        var resourceType = ExtractResourceType(element);
        if (string.IsNullOrEmpty(resourceType))
        {
            return null;
        }

        var baseSchema = _inner.GetSchema($"http://hl7.org/fhir/StructureDefinition/{resourceType}");
        if (baseSchema == null)
        {
            return null;
        }

        var schemas = new List<ValidationSchema> { baseSchema };
        foreach (var profileUrl in ExtractProfileUrls(element))
        {
            var canonical = StripVersionSuffix(profileUrl);
            var profileSchema = _inner.GetSchema(canonical);
            if (profileSchema != null)
            {
                schemas.Add(profileSchema);
            }
        }

        return ValidationSchema.Compose(schemas);
    }

    private static string? ExtractResourceType(IElement element)
    {
        // FHIR root element's InstanceType holds the resource type name (e.g. "Patient").
        // Fallback to the element name in case the source node doesn't populate InstanceType.
        if (!string.IsNullOrEmpty(element.InstanceType))
        {
            return element.InstanceType;
        }
        return element.Name;
    }

    private static IEnumerable<string> ExtractProfileUrls(IElement element)
    {
        var metaList = element.Children("meta");
        if (metaList.Count == 0)
        {
            yield break;
        }
        var meta = metaList[0];

        foreach (var profile in meta.Children("profile"))
        {
            var value = profile.Value?.ToString();
            if (!string.IsNullOrEmpty(value))
            {
                yield return value!;
            }
        }
    }

    private static string StripVersionSuffix(string canonicalUrl)
    {
        var pipe = canonicalUrl.IndexOf('|', StringComparison.Ordinal);
        return pipe >= 0 ? canonicalUrl[..pipe] : canonicalUrl;
    }
}
