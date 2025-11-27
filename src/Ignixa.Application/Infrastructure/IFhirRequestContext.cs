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
/// Centralized request context for FHIR operations.
/// Contains tenant, FHIR version, bundle processing state, and extensibility hooks.
/// Inspired by Microsoft FHIR Server's IFhirRequestContext pattern.
/// </summary>
public interface IFhirRequestContext
{
    // ========== Core Request Metadata ==========

    /// <summary>
    /// Tenant ID resolved by TenantResolutionMiddleware.
    /// For multi-tenant scenarios, identifies which partition to use.
    /// </summary>
    int TenantId { get; set; }

    /// <summary>
    /// Tenant configuration (display name, settings, capabilities).
    /// Populated by TenantResolutionMiddleware after validating tenant exists and is active.
    /// </summary>
    TenantConfiguration? TenantConfiguration { get; set; }

    /// <summary>
    /// FHIR version extracted from Content-Type/Accept headers.
    /// Defaults to R4 if not specified in headers.
    /// Examples: FhirSpecification.R4, FhirSpecification.R5
    /// </summary>
    FhirSpecification FhirVersion { get; set; }

    /// <summary>
    /// Resource type for the current request (e.g., "Patient", "Observation").
    /// Extracted from route parameters or bundle entry.
    /// Null for system-level operations (e.g., /metadata, /$export).
    /// </summary>
    string? ResourceType { get; set; }

    // ========== Version Context Integration (Option A: Reference) ==========

    /// <summary>
    /// Optional reference to IFhirVersionContext for convenience.
    /// Allows context.VersionContext.GetSchemaProvider(...) instead of injecting separately.
    /// Set by middleware during initialization.
    /// Handlers can still inject IFhirVersionContext directly if preferred (both patterns supported).
    /// </summary>
    IFhirVersionContext? VersionContext { get; set; }

    // ========== Bundle Processing State ==========

    /// <summary>
    /// Indicates whether this request is executing within a batch or transaction bundle.
    /// Used to control deferred writes and error handling.
    /// Set by BundleProcessor when processing bundle entries.
    /// </summary>
    bool ExecutingBatchOrTransaction { get; set; }

    /// <summary>
    /// Current bundle entry index (0-based) during concurrent bundle processing.
    /// Used for error reporting and surrogate ID calculation.
    /// Each concurrent bundle entry has an isolated context with unique index.
    /// Null for non-bundle requests.
    /// </summary>
    int? BundleEntryIndex { get; set; }

    /// <summary>
    /// Coordinator for deferred writes during bundle transaction processing.
    /// Allows handlers to queue writes instead of executing immediately.
    /// All writes are committed atomically at the end of transaction bundle processing.
    /// Null for non-transaction requests and batch bundles.
    /// </summary>
    DeferredWriteCoordinator? DeferredWriteCoordinator { get; set; }

    /// <summary>
    /// Pre-assigned resource ID for POST operations with urn:uuid fullUrls in bundles.
    /// Used for reference resolution in bundle processing - ensures consistent ID assignment
    /// across bundle entries that reference each other via urn:uuid.
    /// Null for regular POST operations outside bundles.
    /// </summary>
    string? BundleAssignedResourceId { get; set; }

    // ========== Operation Outcome Tracking ==========

    /// <summary>
    /// List of issues to be returned in search bundle results or operation outcomes.
    /// Allows handlers to add warnings/informational messages without failing the request.
    /// Example: "Search parameter 'custom-param' is not yet indexed (partial results)"
    /// </summary>
    IList<OperationOutcomeIssue> BundleIssues { get; }

    // ========== Background Task Support ==========

    /// <summary>
    /// Indicates whether this context is running as part of a background task
    /// (DurableTask orchestration, subscription engine, bulk export, etc.) instead of an HTTP request.
    /// When true, HTTP-specific operations (e.g., response header manipulation) should be skipped.
    /// </summary>
    bool IsBackgroundTask { get; set; }

    // ========== Extensibility ==========

    /// <summary>
    /// Weakly-typed property bag for communication between pipeline components.
    /// Use for cross-cutting concerns (audit context, feature flags, correlation IDs, etc.).
    /// Example: Properties["CorrelationId"] = Guid.NewGuid().ToString();
    /// </summary>
    IDictionary<string, object> Properties { get; }
}

/// <summary>
/// Represents an issue to be included in an OperationOutcome.
/// Simplified version of FHIR OperationOutcome.issue element.
/// </summary>
public class OperationOutcomeIssue
{
    /// <summary>
    /// Severity: fatal | error | warning | information
    /// </summary>
    public required string Severity { get; set; }

    /// <summary>
    /// Error code from IssueType value set (e.g., "business-rule", "not-found", "processing")
    /// </summary>
    public required string Code { get; set; }

    /// <summary>
    /// Human-readable description of the issue
    /// </summary>
    public required string Diagnostics { get; set; }

    /// <summary>
    /// FHIRPath expression indicating where the issue occurred (optional)
    /// </summary>
    public string? Expression { get; set; }
}
