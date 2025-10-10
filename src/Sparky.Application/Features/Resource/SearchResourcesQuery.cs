// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Medino;
using Sparky.Search.Models;

namespace Sparky.Application.Features.Resource;

/// <summary>
/// Generic query to search for resources of any type.
/// Works for all FHIR resource types (Patient, Observation, Condition, etc.).
/// </summary>
/// <param name="ResourceType">The FHIR resource type (e.g., "Patient", "Observation").</param>
/// <param name="SearchOptions">The search options parsed from query parameters.</param>
public record SearchResourcesQuery(string ResourceType, SearchOptions SearchOptions) : IRequest<SearchResourcesResult>;
