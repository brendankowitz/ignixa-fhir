// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using HotChocolate.Execution;
using Ignixa.Abstractions;
using Ignixa.Application.Features.Experimental.GraphQl.Models;

namespace Ignixa.Application.Features.Experimental.GraphQl.Contracts;

public interface IGraphQlExecutionService
{
    Task<IExecutionResult> ExecuteAsync(
        GraphQlRequestBody request,
        FhirVersion version,
        CancellationToken cancellationToken);

    Task<IExecutionResult> ExecuteInstanceAsync(
        GraphQlRequestBody request,
        FhirVersion version,
        string resourceType,
        string resourceId,
        CancellationToken cancellationToken);
}
