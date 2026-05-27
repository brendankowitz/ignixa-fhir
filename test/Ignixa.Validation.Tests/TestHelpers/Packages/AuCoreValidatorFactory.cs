// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Validation.Schema;

namespace Ignixa.Validation.Tests.TestHelpers.Packages;

/// <summary>
/// Builds a fully-wired validator chain for AU Core scenarios:
/// base R4 schema + AU Core + every transitive dependency declared in AU Core's package.json
/// (currently AU Base, HL7 Terminology R4, UV Extensions R4, SMART App Launch, IPA).
/// <para>
/// Uses <see cref="TestFhirPackageLoader.LoadWithDependenciesAsync"/> so the dependency
/// closure is discovered from manifest data rather than hand-listed - any new transitive
/// dep added to a future AU Core release is picked up automatically.
/// </para>
/// </summary>
internal static class AuCoreValidatorFactory
{
    /// <summary>
    /// Builds the resolver and underlying schema provider for AU Core 1.0.0 plus its full
    /// transitive dependency closure, layered on top of the base R4 spec.
    /// </summary>
    public static async Task<ProfileAwareValidationSchemaResolver> BuildAsync(
        CancellationToken cancellationToken = default)
    {
        var closure = await TestFhirPackageLoader.LoadWithDependenciesAsync(
            "hl7.fhir.au.core", "1.0.0", cancellationToken);

        return PackageValidatorFactory.BuildR4(closure.ToArray());
    }
}

