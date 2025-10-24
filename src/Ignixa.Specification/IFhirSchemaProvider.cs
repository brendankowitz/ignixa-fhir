// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Domain;
using Ignixa.SourceNodeSerialization;
using Ignixa.SourceNodeSerialization.Abstractions;
using Ignixa.SourceNodeSerialization.Specification;

namespace Ignixa.Specification;

public interface IFhirSchemaProvider : IStructureDefinitionSummaryProvider
{
    FhirSpecification Version { get; }

    IReadOnlySet<string> ResourceTypeNames { get; }

    /// <summary>
    /// Gets the full version string including patch and pre-release versions.
    /// Examples: "3.0.2", "4.0.1", "4.3.0", "5.0.0", "6.0.0-ballot2"
    /// </summary>
    string FullVersion { get; }
}
