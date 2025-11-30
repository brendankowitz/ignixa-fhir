// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;

namespace Ignixa.Application.Operations.Features.Validate;

/// <summary>
/// Result of a FHIR $validate operation.
/// Contains the OperationOutcome resource with validation issues.
/// </summary>
/// <param name="OperationOutcome">The FHIR OperationOutcome resource with validation results.</param>
public record ValidateResourceResult(
    JsonNode OperationOutcome);
