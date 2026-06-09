// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.PackageManagement.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ignixa.PackageManagement.Infrastructure;

/// <summary>
/// <see cref="IFhirSchemaProvider"/> that delegates to a base FHIR-version schema provider
/// and additionally exposes profile <c>StructureDefinition</c>s extracted from one or more
/// loaded IG packages. Profiles are indexed by their resource <c>id</c> - the last URL
/// segment that <c>StructureDefinitionSchemaResolver</c> uses to look up via
/// <see cref="ISchema.GetTypeDefinition(string)"/>.
/// <para>
/// When multiple packages declare a profile with the same id, the last one added wins.
/// Use ordering to express precedence (e.g. layer IG-specific profiles after their
/// base IG so the IG wins).
/// </para>
/// </summary>
public sealed class ProfileLayeredSchemaProvider : IFhirSchemaProvider
{
    private readonly IFhirSchemaProvider _base;
    private readonly Dictionary<string, IType> _profileTypes;

    /// <summary>
    /// Initializes a new instance with the given base provider and a collection of
    /// extracted package resources whose <c>StructureDefinition</c> entries are added
    /// to the profile index.
    /// </summary>
    /// <param name="baseProvider">Base FHIR schema provider (R4/R4B/R5/STU3 core).</param>
    /// <param name="packageResources">Conformance resources extracted from one or more IG packages.</param>
    /// <param name="logger">
    /// Optional logger. When a package <c>StructureDefinition</c> cannot be adapted (malformed
    /// JSON, differential-only definition with no snapshot, or missing id), the profile is dropped
    /// from the index and a warning is logged so the silent downgrade to base-only validation is
    /// observable. Defaults to <see cref="NullLogger{T}"/>.
    /// </param>
    public ProfileLayeredSchemaProvider(
        IFhirSchemaProvider baseProvider,
        IEnumerable<ExtractedResource> packageResources,
        ILogger<ProfileLayeredSchemaProvider>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(baseProvider);
        ArgumentNullException.ThrowIfNull(packageResources);

        _base = baseProvider;
        _profileTypes = new Dictionary<string, IType>(StringComparer.Ordinal);
        var log = logger ?? NullLogger<ProfileLayeredSchemaProvider>.Instance;

        var provider = new PackageResourceProvider(NullLogger<PackageResourceProvider>.Instance);
        foreach (var res in packageResources)
        {
            if (res.ResourceType != "StructureDefinition")
            {
                continue;
            }
            var type = provider.ToTypeDefinition(res.ResourceJson, baseProvider.FullVersion);
            if (type != null && !string.IsNullOrEmpty(res.ResourceId))
            {
                _profileTypes[res.ResourceId] = type;
            }
            else
            {
                log.LogWarning(
                    "Profile StructureDefinition (id='{ProfileId}', canonical='{Canonical}') could not be adapted and will not be available for profile validation. Resources declaring this profile validate against the base resource definition only.",
                    res.ResourceId,
                    res.Canonical);
            }
        }
    }

    /// <inheritdoc/>
    public FhirVersion Version => _base.Version;

    /// <inheritdoc/>
    public string FullVersion => _base.FullVersion;

    /// <inheritdoc/>
    public IReadOnlySet<string> ResourceTypeNames => _base.ResourceTypeNames;

    /// <inheritdoc/>
    public IReferenceMetadataProvider ReferenceMetadataProvider => _base.ReferenceMetadataProvider;

    /// <inheritdoc/>
    public IValueSetProvider ValueSetProvider => _base.ValueSetProvider;

    /// <inheritdoc/>
    public IType? GetTypeDefinition(string typeName)
        => _profileTypes.TryGetValue(typeName, out var profile) ? profile : _base.GetTypeDefinition(typeName);

    /// <inheritdoc/>
    public bool IsKnownType(string typeName)
        => _profileTypes.ContainsKey(typeName) || _base.IsKnownType(typeName);
}
