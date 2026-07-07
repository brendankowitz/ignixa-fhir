// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.PackageManagement.Models;
using Ignixa.PackageManagement.Validation;
using Ignixa.Specification.Generated;
using Ignixa.Validation.Schema;

namespace Ignixa.Validation.Tests.TestHelpers.Packages;

/// <summary>
/// Shared builder that composes a base FHIR schema with one or more loaded IG packages
/// into a <see cref="ProfileAwareValidationSchemaResolver"/>. Used by the per-IG
/// convenience factories (<c>CarinBbValidatorFactory</c>, <c>UsCoreValidatorFactory</c>, ...)
/// so the wiring is DRY across suites. Delegates to the product surface
/// <see cref="PackageBackedValidator"/>.
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

        var resources = packages.SelectMany(p => p.Resources).ToList();
        return BuildR4(resources);
    }

    /// <summary>
    /// Builds a profile-aware resolver wiring base R4 + the supplied package resources.
    /// </summary>
    /// <param name="resources">Conformance resources to layer (profiles + ValueSets/CodeSystems).</param>
    public static ProfileAwareValidationSchemaResolver BuildR4(IReadOnlyList<ExtractedResource> resources)
    {
        ArgumentNullException.ThrowIfNull(resources);

        var setup = PackageBackedValidator.Create(new PackageValidationOptions
        {
            BaseSchemaProvider = new R4CoreSchemaProvider(),
            PackageResources = resources,
        });
        return setup.SchemaResolver;
    }
}
