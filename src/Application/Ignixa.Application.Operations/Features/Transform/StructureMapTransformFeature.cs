// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Domain.Abstractions;

namespace Ignixa.Application.Operations.Features.Transform;

/// <summary>
/// Implementation of IPackageFeature for the StructureMap $transform operation.
/// Declares the FHIR Mapping Language transformation operations supported by the server.
/// </summary>
public class StructureMapTransformFeature : IPackageFeature
{
    private static readonly List<string> TransformOperationsList = ["transform"];

    public string PackageId => "hl7.fhir.core";

    public IReadOnlyList<string> SystemOperations => [];

    public IReadOnlyDictionary<string, IReadOnlyList<string>> ResourceOperations =>
        new Dictionary<string, IReadOnlyList<string>>
        {
            { "StructureMap", TransformOperationsList },
        };

    public IReadOnlyList<string>? SupportedFhirVersions => null; // Supports all versions
}
