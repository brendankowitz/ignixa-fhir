// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Domain.Abstractions;

namespace Ignixa.Application.Features.BulkDelete;

/// <summary>
/// Implementation of <see cref="IPackageFeature"/> for the <c>$bulk-delete</c> operation. Declares it as
/// both a system-level operation and a resource-level operation on every resource type, matching
/// fhir-server's <c>DELETE /$bulk-delete</c> and <c>DELETE /{type}/$bulk-delete</c> routes.
/// </summary>
/// <remarks>
/// <c>bulk-delete</c> is not part of any published HL7 package the way <c>$export</c> belongs to
/// <c>hl7.fhir.uv.bulkdata</c>; fhir-server defines it itself. "ignixa.bulk-delete" names this server's
/// own declaration rather than claiming membership in a package this server does not implement.
/// </remarks>
public sealed class BulkDeleteFeature : IPackageFeature
{
    private static readonly string[] SystemOperationsList = ["bulk-delete"];
    private static readonly string[] AllResourcesOperationsList = ["bulk-delete"];

    public string PackageId => "ignixa.bulk-delete";

    public IReadOnlyList<string> SystemOperations => SystemOperationsList;

    public IReadOnlyDictionary<string, IReadOnlyList<string>> ResourceOperations =>
        new Dictionary<string, IReadOnlyList<string>>
        {
            { "*", AllResourcesOperationsList },
        };

    public IReadOnlyList<string>? SupportedFhirVersions => null;
}
