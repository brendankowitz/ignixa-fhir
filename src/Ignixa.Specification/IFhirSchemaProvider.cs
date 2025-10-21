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
}
