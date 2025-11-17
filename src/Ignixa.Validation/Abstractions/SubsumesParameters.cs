// <copyright file="SubsumesParameters.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

namespace Ignixa.Validation.Abstractions;

/// <summary>
/// Parameters for CodeSystem $subsumes operation.
/// See https://hl7.org/fhir/R4/codesystem-operation-subsumes.html
/// </summary>
/// <param name="CodeA">The first code to compare.</param>
/// <param name="CodeB">The second code to compare.</param>
/// <param name="System">Code system URL (required for both codes).</param>
/// <param name="Version">Code system version (optional).</param>
public record SubsumesParameters(
    string CodeA,
    string CodeB,
    string System,
    string? Version);
