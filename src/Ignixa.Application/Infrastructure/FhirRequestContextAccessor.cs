// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Application.Features.Bundle;
using Ignixa.Application.Features.Search;
using Ignixa.Domain;
using Ignixa.Domain.Models;
using Ignixa.Serialization;

namespace Ignixa.Application.Infrastructure;

/// <summary>
/// Default implementation using AsyncLocal for thread-safe concurrent bundle processing.
/// Registered as SCOPED service in DI container.
/// </summary>
public class FhirRequestContextAccessor : IFhirRequestContextAccessor
{
    // AsyncLocal ensures each async context (bundle entry) has isolated state
    // This is the same pattern currently used for BundleEntryIndex in HttpContextExtensions.cs:68
    private static readonly AsyncLocal<IFhirRequestContext?> _context = new();

    /// <summary>
    /// Gets or sets the current FHIR request context.
    /// Uses AsyncLocal storage to ensure thread-safety for concurrent bundle entry processing.
    /// </summary>
    public IFhirRequestContext? RequestContext
    {
        get => _context.Value;
        set => _context.Value = value;
    }
}

/// <summary>
/// Concrete implementation of IFhirRequestContext.
/// Mutable properties allow middleware and bundle processor to populate incrementally.
/// </summary>
public class FhirRequestContext : IFhirRequestContext
{
    /// <summary>
    /// Tenant ID resolved by TenantResolutionMiddleware.
    /// </summary>
    public int TenantId { get; set; }

    /// <summary>
    /// Tenant configuration populated by TenantResolutionMiddleware.
    /// </summary>
    public TenantConfiguration? TenantConfiguration { get; set; }

    /// <summary>
    /// FHIR version extracted from Content-Type/Accept headers.
    /// Defaults to R4.
    /// </summary>
    public FhirSpecification FhirVersion { get; set; } = FhirSpecification.R4;

    /// <summary>
    /// Resource type from route parameters or bundle entry.
    /// </summary>
    public string? ResourceType { get; set; }

    /// <summary>
    /// Optional reference to IFhirVersionContext for convenience.
    /// </summary>
    public IFhirVersionContext? VersionContext { get; set; }

    /// <summary>
    /// Indicates whether executing within a batch or transaction bundle.
    /// </summary>
    public bool ExecutingBatchOrTransaction { get; set; }

    /// <summary>
    /// Current bundle entry index (0-based) during concurrent processing.
    /// </summary>
    public int? BundleEntryIndex { get; set; }

    /// <summary>
    /// Coordinator for deferred writes during bundle transaction processing.
    /// </summary>
    public DeferredWriteCoordinator? DeferredWriteCoordinator { get; set; }

    /// <summary>
    /// Pre-assigned resource ID for POST operations with urn:uuid fullUrls in bundles.
    /// </summary>
    public string? BundleAssignedResourceId { get; set; }

    /// <summary>
    /// List of issues to be returned in search bundle results.
    /// </summary>
    public IList<OperationOutcomeIssue> BundleIssues { get; } = new List<OperationOutcomeIssue>();

    /// <summary>
    /// Indicates whether running as background task.
    /// </summary>
    public bool IsBackgroundTask { get; set; }

    /// <summary>
    /// Weakly-typed property bag for extensibility.
    /// </summary>
    public IDictionary<string, object> Properties { get; } = new Dictionary<string, object>();
}
