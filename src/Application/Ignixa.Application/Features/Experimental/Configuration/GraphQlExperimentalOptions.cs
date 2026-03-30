// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;

namespace Ignixa.Application.Features.Experimental.Configuration;

public class GraphQlExperimentalOptions
{
    public bool Enabled { get; set; } = true;
    public int MaxQueryDepth { get; set; } = 15;
    public bool EnableIntrospection { get; set; } = true;
    /// <summary>Reserved for future use. HC v15 does not expose a built-in complexity rule via AddGraphQLServer.</summary>
    public int MaxQueryComplexity { get; set; } = 500;
    public int MaxPageSize { get; set; } = 1000;
    public int DefaultPageSize { get; set; } = 10;
    public bool EnableGetRequests { get; set; } = true;
    public int ExecutionTimeoutSeconds { get; set; } = 30;
    public ICollection<FhirVersion> WarmupVersions { get; } = [FhirVersion.R4];
}
