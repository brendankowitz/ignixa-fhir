// <copyright file="FhirCode.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

#nullable enable

namespace Ignixa.Abstractions;

/// <summary>
/// Represents a FHIR code with system, code, and display values.
/// </summary>
/// <param name="System">The code system URI (e.g., "http://hl7.org/fhir/administrative-gender").</param>
/// <param name="Code">The code value (e.g., "male").</param>
/// <param name="Display">The human-readable display text (e.g., "Male").</param>
public readonly record struct FhirCode(string System, string Code, string Display);
