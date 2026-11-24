// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;

namespace Ignixa.Application.Features.Experimental.Mcp.Tools;

/// <summary>
/// A resource type that <see cref="TenantAwareMcpTool.RequireKnownResourceTypeAsync"/> has confirmed the
/// tenant serves, in the schema's canonical casing, with the tenant's FHIR version.
/// </summary>
/// <param name="ResourceType">Canonical resource type name.</param>
/// <param name="FhirVersion">The tenant's FHIR version.</param>
public readonly record struct KnownResourceType(string ResourceType, FhirVersion FhirVersion);
