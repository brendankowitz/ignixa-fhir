// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Medino;
using Sparky.Domain.Models;

namespace Sparky.Application.Features.Resource;

/// <summary>
/// Generic query to retrieve a resource by type and ID.
/// Works for all FHIR resource types (Patient, Observation, Condition, etc.).
/// </summary>
/// <param name="ResourceType">The FHIR resource type (e.g., "Patient", "Observation").</param>
/// <param name="Id">The resource ID.</param>
public record GetResourceQuery(string ResourceType, string Id) : IRequest<ResourceWrapper?>;
