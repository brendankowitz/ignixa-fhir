// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Validation.Schema;

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
        return PackageValidatorFactory.BuildR4(pkg);
    }
}

