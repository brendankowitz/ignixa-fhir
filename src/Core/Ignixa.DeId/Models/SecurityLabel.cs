// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
namespace Ignixa.DeId.Models;

/// <summary>
/// Represents a FHIR security label with a coding system, code, and display text.
/// </summary>
public record SecurityLabel(string? System, string Code, string Display);
