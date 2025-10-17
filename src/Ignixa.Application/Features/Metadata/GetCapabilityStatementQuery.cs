// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Medino;
using Ignixa.Application.Features.Metadata.Models;

namespace Ignixa.Application.Features.Metadata;

/// <summary>
/// Query to retrieve the server's FHIR CapabilityStatement.
/// </summary>
/// <param name="TenantId">Optional tenant ID for tenant-specific capability statement.</param>
public record GetCapabilityStatementQuery(int? TenantId)
    : IRequest<CapabilityStatementJsonNode>;
