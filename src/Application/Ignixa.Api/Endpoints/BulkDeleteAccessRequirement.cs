// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Application.Features.Authorization.Models;

namespace Ignixa.Api.Endpoints;

/// <summary>
/// One interaction a <c>$bulk-delete</c> kickoff must be authorized for before the job may start.
/// </summary>
/// <param name="Interaction">The interaction the job performs (<see cref="FhirInteraction.Delete"/> or <see cref="FhirInteraction.Update"/>).</param>
/// <param name="ResourceType">
/// The resource type it is performed on, or null when the job can reach every resource type; null is
/// authorized as the wildcard (<c>*</c>) grant by both RBAC and SMART.
/// </param>
/// <param name="Reason">Why the job needs this access, reported in the 403 diagnostics.</param>
internal sealed record BulkDeleteAccessRequirement(FhirInteraction Interaction, string? ResourceType, string Reason);
