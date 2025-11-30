// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Validation.Abstractions;

/// <summary>
/// Parameters for ConceptMap $translate operation.
/// See https://hl7.org/fhir/R4/conceptmap-operation-translate.html
/// </summary>
/// <param name="Url">Canonical URL of the ConceptMap to use for translation (optional if source/target specified).</param>
/// <param name="ConceptMapVersion">ConceptMap version (optional).</param>
/// <param name="Code">The code to translate.</param>
/// <param name="System">Code system of the source code.</param>
/// <param name="Version">Version of the source code system (optional).</param>
/// <param name="Source">Source ValueSet URL (alternative to specifying ConceptMap URL).</param>
/// <param name="Target">Target ValueSet URL (used with source or to filter results).</param>
/// <param name="TargetSystem">Target code system URL to filter results (optional).</param>
/// <param name="Reverse">If true, translate from target to source instead of source to target.</param>
public record TranslateParameters(
    string? Url,
    string? ConceptMapVersion,
    string Code,
    string System,
    string? Version,
    string? Source,
    string? Target,
    string? TargetSystem,
    bool Reverse = false);
