// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Domain.Abstractions;

namespace Ignixa.Application.Features.Export;

/// <summary>
/// Implementation of IPackageFeature for the bulk data export functionality.
/// Declares the hl7.fhir.uv.bulkdata package operations supported by the server.
/// </summary>
public class BulkDataExportFeature : IPackageFeature
{
    private static readonly string[] SystemOperationsList = new[] { "export" };
    private static readonly string[] PatientOperationsList = new[] { "export" };
    private static readonly string[] GroupOperationsList = new[] { "export" };

    public string PackageId => "hl7.fhir.uv.bulkdata";

    public IReadOnlyList<string> SystemOperations => SystemOperationsList;

    public IReadOnlyDictionary<string, IReadOnlyList<string>> ResourceOperations =>
        new Dictionary<string, IReadOnlyList<string>>
        {
            { "Patient", PatientOperationsList },
            { "Group", GroupOperationsList },
        };

    public IReadOnlyList<string>? SupportedFhirVersions => null; // Supports all versions
}
