// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Medino;
using Ignixa.Serialization.Models;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Application.Features.Experimental.Transform;

/// <summary>
/// Command for StructureMap $transform operation.
/// Transforms a FHIR resource using mapping rules defined in a StructureMap.
///
/// Parameters follow FHIR R6 $transform operation specification:
/// - source: Canonical URL of the StructureMap to apply
/// - sourceMap: Inline StructureMap resource (alternative to source)
/// - srcMap: FML text format (R6+, alternative to source)
/// - supportingMap: Additional maps for dependencies/imports
/// - content: The resource to transform (required)
/// </summary>
/// <param name="Source">Canonical URL of the StructureMap (e.g., "http://hl7.org/fhir/StructureMap/Patient4to5")</param>
/// <param name="SourceMap">Inline StructureMap resource (alternative to Source URL)</param>
/// <param name="SrcMaps">FML text format maps (R6+, alternative to Source URL)</param>
/// <param name="SupportingMaps">Additional StructureMaps for dependencies/imports</param>
/// <param name="Content">Input resource to transform (required)</param>
public record TransformResourceCommand(
    string? Source = null,
    StructureMapJsonNode? SourceMap = null,
    IReadOnlyList<string>? SrcMaps = null,
    IReadOnlyList<StructureMapJsonNode>? SupportingMaps = null,
    ResourceJsonNode? Content = null) : IRequest<ResourceJsonNode>;
