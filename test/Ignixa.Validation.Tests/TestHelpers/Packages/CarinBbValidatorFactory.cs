// <copyright file="CarinBbValidatorFactory.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using Ignixa.Abstractions;
using Ignixa.PackageManagement.Infrastructure;
using Ignixa.PackageManagement.Models;
using Ignixa.Specification.Generated;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Schema;
using Ignixa.Validation.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ignixa.Validation.Tests.TestHelpers.Packages;

/// <summary>
/// Builds a fully-wired validator chain for the CARIN BlueButton scenario:
/// base R4 schema + CARIN-BB profile StructureDefinitions + CARIN-BB ValueSets.
/// Returns a <see cref="ProfileAwareValidationSchemaResolver"/> ready to call
/// <c>ResolveForElement(...)</c> on the customer's EOB instance.
/// </summary>
internal static class CarinBbValidatorFactory
{
    /// <summary>
    /// Builds the resolver and underlying schema provider for CARIN BlueButton 2.1.0
    /// layered on top of the base R4 spec.
    /// </summary>
    public static async Task<ProfileAwareValidationSchemaResolver> BuildAsync(
        CancellationToken cancellationToken = default)
    {
        var pkg = await TestFhirPackageLoader.LoadCarinBlueButtonAsync(cancellationToken);
        var baseSchema = new R4CoreSchemaProvider();

        var profileProvider = new PackageResourceProvider(NullLogger<PackageResourceProvider>.Instance);
        var packageSchema = new ProfileLayeredSchemaProvider(baseSchema, pkg.Resources, profileProvider);

        var packageVs = new PackageValueSetSource(pkg.Resources);
        var terminology = new InMemoryTerminologyService(
            primary: baseSchema.ValueSetProvider,
            additional: new[] { (IValueSetProvider)packageVs });

        var inner = new StructureDefinitionSchemaResolver(packageSchema, terminologyService: terminology);
        var cached = new CachedValidationSchemaResolver(inner);
        return new ProfileAwareValidationSchemaResolver(cached);
    }

    /// <summary>
    /// Schema provider that delegates to the base FHIR provider, and additionally exposes
    /// profile StructureDefinitions from a package by their <c>id</c> (which is what
    /// <see cref="StructureDefinitionSchemaResolver"/> extracts from a canonical URL).
    /// </summary>
    private sealed class ProfileLayeredSchemaProvider : IFhirSchemaProvider
    {
        private readonly IFhirSchemaProvider _base;
        private readonly Dictionary<string, IType> _profileTypes;

        public ProfileLayeredSchemaProvider(
            IFhirSchemaProvider baseProvider,
            IReadOnlyList<ExtractedResource> packageResources,
            PackageResourceProvider provider)
        {
            _base = baseProvider;
            _profileTypes = new Dictionary<string, IType>(StringComparer.Ordinal);
            foreach (var res in packageResources)
            {
                if (res.ResourceType != "StructureDefinition")
                {
                    continue;
                }
                var type = provider.ToTypeDefinition(res.ResourceJson, baseProvider.FullVersion);
                if (type != null)
                {
                    // Index by resource id (last segment of canonical URL is typically the id)
                    _profileTypes[res.ResourceId] = type;
                }
            }
        }

        public FhirVersion Version => _base.Version;
        public string FullVersion => _base.FullVersion;
        public IReadOnlySet<string> ResourceTypeNames => _base.ResourceTypeNames;
        public IReferenceMetadataProvider ReferenceMetadataProvider => _base.ReferenceMetadataProvider;
        public IValueSetProvider ValueSetProvider => _base.ValueSetProvider;

        public IType? GetTypeDefinition(string typeName)
            => _profileTypes.TryGetValue(typeName, out var profile) ? profile : _base.GetTypeDefinition(typeName);

        public bool IsKnownType(string typeName)
            => _profileTypes.ContainsKey(typeName) || _base.IsKnownType(typeName);
    }
}
