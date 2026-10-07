// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Domain.Models;

namespace Ignixa.Domain.Abstractions;

/// <summary>
/// A job definition that persists the audit attribution of the request that started it.
/// </summary>
public interface IAuditedJobDefinition : IJobDefinition
{
    /// <summary>
    /// Audit attribution captured at kick-off; null for jobs created before audit capture existed
    /// or started outside an HTTP request.
    /// </summary>
    BackgroundJobAuditContext? AuditContext { get; }
}
