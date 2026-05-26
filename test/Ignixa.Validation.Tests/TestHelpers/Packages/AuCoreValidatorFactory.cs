// <copyright file="AuCoreValidatorFactory.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using Ignixa.Validation.Schema;

namespace Ignixa.Validation.Tests.TestHelpers.Packages;

/// <summary>
/// Builds a fully-wired validator chain for AU Core scenarios:
/// base R4 schema + AU Core + AU Base + HL7 Terminology + UV Extensions.
/// <para>
/// Unlike CARIN-BB and US Core, AU Core has substantial transitive package
/// dependencies that must all be loaded for profile validation to be meaningful.
/// This factory loads the four core dependencies in parallel; SMART App Launch
/// and IPA are deliberately omitted as they're not required for typical
/// AU Core Patient/Observation/Condition validation.
/// </para>
/// <para>
/// First run downloads ~10 MB across the four packages; subsequent runs hit cache.
/// </para>
/// </summary>
internal static class AuCoreValidatorFactory
{
    /// <summary>
    /// Builds the resolver and underlying schema provider for AU Core 1.0.0
    /// with AU Base 5.0.0, HL7 Terminology R4 6.2.0, and UV Extensions R4 5.1.0
    /// layered on top of the base R4 spec.
    /// </summary>
    public static async Task<ProfileAwareValidationSchemaResolver> BuildAsync(
        CancellationToken cancellationToken = default)
    {
        // Load all four packages in parallel - they're independent network requests.
        var auCoreT = TestFhirPackageLoader.LoadAuCoreAsync(cancellationToken);
        var auBaseT = TestFhirPackageLoader.LoadAuBaseAsync(cancellationToken);
        var terminologyT = TestFhirPackageLoader.LoadHl7TerminologyR4Async(cancellationToken);
        var extensionsT = TestFhirPackageLoader.LoadUvExtensionsR4Async(cancellationToken);

        await Task.WhenAll(auCoreT, auBaseT, terminologyT, extensionsT);

        return PackageValidatorFactory.BuildR4(
            await auCoreT,
            await auBaseT,
            await terminologyT,
            await extensionsT);
    }
}
