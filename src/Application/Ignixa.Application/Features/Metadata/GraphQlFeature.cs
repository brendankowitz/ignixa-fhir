// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Domain.Abstractions;

namespace Ignixa.Application.Features.Metadata;

/// <summary>
/// Implementation of <see cref="IPackageFeature"/> for the FHIR $graphql operation.
/// Advertises the $graphql system-level operation in the CapabilityStatement
/// when GraphQL is enabled, per the FHIR specification requirement that servers
/// SHALL indicate GraphQL support in their conformance statement.
/// </summary>
public class GraphQlFeature : IPackageFeature
{
    private static readonly List<string> GraphQlOperationsList = ["graphql"];

    public string PackageId => "hl7.fhir.core";

    public IReadOnlyList<string> SystemOperations => GraphQlOperationsList;

    public IReadOnlyDictionary<string, IReadOnlyList<string>> ResourceOperations =>
        new Dictionary<string, IReadOnlyList<string>>();

    public IReadOnlyList<string>? SupportedFhirVersions => null; // Supports all FHIR versions
}
