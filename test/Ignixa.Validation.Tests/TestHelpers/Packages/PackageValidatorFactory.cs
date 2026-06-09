// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.PackageManagement.Infrastructure;
using Ignixa.Specification.Generated;
using Ignixa.Validation.Schema;
using Ignixa.Validation.Services;

namespace Ignixa.Validation.Tests.TestHelpers.Packages;

/// <summary>
/// Shared builder that composes a base FHIR schema with one or more loaded IG packages
/// into a <see cref="ProfileAwareValidationSchemaResolver"/>. Used by the per-IG
/// convenience factories (<c>CarinBbValidatorFactory</c>, <c>UsCoreValidatorFactory</c>, ...)
/// so the wiring is DRY across suites.
/// </summary>
internal static class PackageValidatorFactory
{
    /// <summary>
    /// Builds a profile-aware resolver wiring base R4 + the supplied packages.
    /// </summary>
    /// <param name="packages">Packages to layer (profiles + ValueSets/CodeSystems).</param>
    public static ProfileAwareValidationSchemaResolver BuildR4(params TestFhirPackage[] packages)
    {
        ArgumentNullException.ThrowIfNull(packages);

        var baseSchema = new R4CoreSchemaProvider();
        var packageSchema = new ProfileLayeredSchemaProvider(
            baseSchema,
            packages.SelectMany(p => p.Resources));

        var packageVs = new PackageValueSetSource(packages.SelectMany(p => p.Resources));
        var terminology = new InMemoryTerminologyService(
            primary: baseSchema.ValueSetProvider,
            additional: new[] { (IValueSetProvider)packageVs });

        var inner = new StructureDefinitionSchemaResolver(packageSchema, terminologyService: terminology);
        var cached = new CachedValidationSchemaResolver(inner);
        return new ProfileAwareValidationSchemaResolver(cached);
    }
}
