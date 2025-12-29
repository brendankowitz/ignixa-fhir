// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Domain.Abstractions;

namespace Ignixa.Application.Features.Resource;

/// <summary>
/// Implementation of IPackageFeature for the $includes operation.
/// Declares the includes operation as a system-level operation for independent pagination
/// of _include/_revinclude results.
/// </summary>
public class IncludesOperationFeature : IPackageFeature
{
    private static readonly string[] EmptyOperations = [];
    private static readonly string[] AllResourcesOperations = new[] { "includes" };

    public string PackageId => "hl7.fhir.core.search";

    public IReadOnlyList<string> SystemOperations => EmptyOperations;

    public IReadOnlyDictionary<string, IReadOnlyList<string>> ResourceOperations =>
        new Dictionary<string, IReadOnlyList<string>>
        {
            { "*", AllResourcesOperations },
        };

    public IReadOnlyList<string>? SupportedFhirVersions => null;
}
