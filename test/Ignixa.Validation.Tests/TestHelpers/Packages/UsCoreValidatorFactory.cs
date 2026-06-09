// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Validation.Schema;

namespace Ignixa.Validation.Tests.TestHelpers.Packages;

/// <summary>
/// Builds a fully-wired validator chain for US Core scenarios:
/// base R4 schema + US Core profile StructureDefinitions + US Core ValueSets.
/// Returns a <see cref="ProfileAwareValidationSchemaResolver"/> ready to call
/// <c>ResolveForElement(...)</c> on a resource that declares
/// <c>meta.profile</c> = a us-core-* canonical.
/// </summary>
internal static class UsCoreValidatorFactory
{
    /// <summary>
    /// Builds the resolver and underlying schema provider for US Core 6.1.0
    /// layered on top of the base R4 spec.
    /// </summary>
    public static async Task<ProfileAwareValidationSchemaResolver> BuildAsync(
        CancellationToken cancellationToken = default)
    {
        var pkg = await TestFhirPackageLoader.LoadUsCoreAsync(cancellationToken);
        return PackageValidatorFactory.BuildR4(pkg);
    }
}
