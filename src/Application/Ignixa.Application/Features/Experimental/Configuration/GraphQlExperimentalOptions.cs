// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;

namespace Ignixa.Application.Features.Experimental.Configuration;

public sealed class GraphQlExperimentalOptions
{
    public bool Enabled { get; set; } = true;
    public int MaxQueryDepth { get; set; } = 15;
    public bool EnableIntrospection { get; set; } = true;

    /// <summary>
    /// When true, mounts the interactive Banana Cake Pop GraphQL IDE at /graphql.
    /// Defaults to false: the IDE is an unauthenticated query surface and should be
    /// opt-in per deployment, independent of <see cref="EnableIntrospection"/>.
    /// </summary>
    public bool EnableGraphQlIde { get; set; }
    public int MaxPageSize { get; set; } = 1000;
    public int DefaultPageSize { get; set; } = 10;
    public bool EnableGetRequests { get; set; } = true;
    public int ExecutionTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// When true, GraphQL errors include the underlying exception type and message in the
    /// FHIR OperationOutcome diagnostics. Leave false in production to avoid leaking internals.
    /// </summary>
    public bool IncludeExceptionDetails { get; set; }

    public ICollection<FhirVersion> WarmupVersions { get; } = [FhirVersion.R4];
}
