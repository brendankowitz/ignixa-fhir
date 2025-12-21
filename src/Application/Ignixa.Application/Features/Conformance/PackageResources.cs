// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.Features.Conformance;

/// <summary>
/// Container for all conformance resources being activated from a package.
/// </summary>
public record PackageResources(
    IReadOnlyList<SearchParameterInfo> SearchParameters,
    IReadOnlyList<StructureDefinitionInfo> StructureDefinitions)
{
    /// <summary>
    /// All resources (SearchParameters and StructureDefinitions) as a single enumerable.
    /// </summary>
    public IEnumerable<object> All => SearchParameters.Cast<object>()
        .Concat(StructureDefinitions);
}

/// <summary>
/// DTO representing a StructureDefinition being activated from a package.
/// </summary>
public record StructureDefinitionInfo(
    string Canonical,
    string Type,
    string Kind,
    string SnapshotJson);
