// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;

namespace Ignixa.Api.Endpoints;

/// <summary>
/// The HTTP shape of a bulk-delete status response: the status code fhir-server uses for this job
/// state, and the FHIR <c>Parameters</c> body to serialize. The <c>application/fhir+json</c> content
/// type, and the <c>Progress</c>/<c>Retry-After</c> headers that accompany a 202, are the endpoint's
/// responsibility, not this builder's.
/// </summary>
/// <param name="StatusCode">The HTTP status code: 202 (queued/running), 200 (completed/cancelled), or 500 (failed).</param>
/// <param name="Body">The FHIR <c>Parameters</c> resource, as a mutable JSON tree ready to serialize.</param>
public sealed record BulkDeleteStatusResponse(int StatusCode, JsonObject Body);
