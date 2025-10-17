// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.AspNetCore.Http;

namespace Ignixa.Application.Infrastructure;

/// <summary>
/// Provides access to the ASP.NET Core request pipeline for executing synthetic HttpContext objects.
/// Implemented in the API layer and injected into Application layer services.
/// </summary>
public interface IPipelineExecutor
{
    /// <summary>
    /// Executes a request through the ASP.NET Core pipeline.
    /// </summary>
    /// <param name="context">The HttpContext to execute.</param>
    /// <returns>A task that represents the asynchronous execution.</returns>
    Task ExecuteAsync(HttpContext context);
}
