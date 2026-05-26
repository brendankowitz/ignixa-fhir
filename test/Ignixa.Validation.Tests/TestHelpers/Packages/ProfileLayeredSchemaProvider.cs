// <copyright file="ProfileLayeredSchemaProvider.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using Ignixa.Abstractions;
using Ignixa.PackageManagement.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ignixa.Validation.Tests.TestHelpers.Packages;

/// <summary>
/// Test schema provider that delegates to a base <see cref="IFhirSchemaProvider"/>
/// and additionally exposes profile <c>StructureDefinition</c>s from one or more
/// loaded IG packages.
/// <para>
/// Profiles are indexed by their resource <c>id</c> - the last URL segment that
/// <see cref="Ignixa.Validation.Schema.StructureDefinitionSchemaResolver"/> extracts
/// from a canonical URL to look up via <see cref="ISchema.GetTypeDefinition(string)"/>.
/// Multiple packages can be layered; later packages with the same profile id win.
/// </para>
/// </summary>
internal sealed class ProfileLayeredSchemaProvider : IFhirSchemaProvider
{
    private readonly IFhirSchemaProvider _base;
    private readonly Dictionary<string, IType> _profileTypes;

    public ProfileLayeredSchemaProvider(
        IFhirSchemaProvider baseProvider,
        IEnumerable<TestFhirPackage> packages)
    {
        ArgumentNullException.ThrowIfNull(baseProvider);
        ArgumentNullException.ThrowIfNull(packages);

        _base = baseProvider;
        _profileTypes = new Dictionary<string, IType>(StringComparer.Ordinal);

        var provider = new PackageResourceProvider(NullLogger<PackageResourceProvider>.Instance);
        foreach (var pkg in packages)
        {
            foreach (var res in pkg.Resources)
            {
                if (res.ResourceType != "StructureDefinition")
                {
                    continue;
                }
                var type = provider.ToTypeDefinition(res.ResourceJson, baseProvider.FullVersion);
                if (type != null)
                {
                    _profileTypes[res.ResourceId] = type;
                }
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
