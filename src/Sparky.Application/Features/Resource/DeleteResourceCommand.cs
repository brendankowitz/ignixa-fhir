// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Medino;

namespace Sparky.Application.Features.Resource;

/// <summary>
/// Generic command to delete any FHIR resource.
/// Works for all resource types (Patient, Observation, Condition, etc.).
/// </summary>
/// <param name="ResourceType">The FHIR resource type (e.g., "Patient", "Observation").</param>
/// <param name="Id">The resource ID.</param>
public record DeleteResourceCommand(string ResourceType, string Id) : IRequest<bool>;
