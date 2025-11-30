// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Domain.Models;
using Ignixa.Serialization;

namespace Ignixa.Application.Infrastructure;

/// <summary>
/// Factory for creating IFhirRequestContext instances for background tasks and orchestrations.
/// Use this when executing FHIR operations outside of HTTP request context (DurableTask, subscription engine).
/// </summary>
public static class FhirRequestContextFactory
{
    /// <summary>
    /// Creates a background task context for DurableTask orchestrations, subscription engine, or other non-HTTP scenarios.
    /// </summary>
    /// <param name="tenantId">Tenant ID for the background operation.</param>
    /// <param name="tenantConfiguration">Optional tenant configuration (null will trigger lazy load).</param>
    /// <param name="fhirVersion">FHIR version to use (defaults to R4).</param>
    /// <param name="resourceType">Optional resource type context.</param>
    /// <returns>Configured IFhirRequestContext for background operations.</returns>
    public static IFhirRequestContext CreateBackgroundContext(
        int tenantId,
        TenantConfiguration? tenantConfiguration = null,
        FhirSpecification fhirVersion = FhirSpecification.R4,
        string? resourceType = null)
    {
        return new FhirRequestContext
        {
            TenantId = tenantId,
            TenantConfiguration = tenantConfiguration,
            FhirVersion = fhirVersion,
            ResourceType = resourceType,
            IsBackgroundTask = true,
            ExecutingBatchOrTransaction = false,
            BundleEntryIndex = null,
            DeferredWriteCoordinator = null,
            BundleAssignedResourceId = null
        };
    }

    /// <summary>
    /// Creates a bundle entry context (isolated via AsyncLocal) for concurrent bundle processing.
    /// Inherits tenant/version from parent context but isolates bundle-specific state.
    /// </summary>
    /// <param name="parentContext">Parent request context (from HTTP request or background task).</param>
    /// <param name="entryIndex">Zero-based bundle entry index.</param>
    /// <param name="resourceType">Resource type for this entry.</param>
    /// <param name="assignedResourceId">Pre-assigned resource ID for urn:uuid fullUrls.</param>
    /// <returns>Isolated IFhirRequestContext for bundle entry processing.</returns>
    public static IFhirRequestContext CreateBundleEntryContext(
        IFhirRequestContext parentContext,
        int entryIndex,
        string? resourceType = null,
        string? assignedResourceId = null)
    {
        return new FhirRequestContext
        {
            TenantId = parentContext.TenantId,
            TenantConfiguration = parentContext.TenantConfiguration,
            FhirVersion = parentContext.FhirVersion,
            VersionContext = parentContext.VersionContext,
            ResourceType = resourceType,
            ExecutingBatchOrTransaction = true,
            BundleEntryIndex = entryIndex,
            DeferredWriteCoordinator = parentContext.DeferredWriteCoordinator,
            BundleAssignedResourceId = assignedResourceId,
            IsBackgroundTask = parentContext.IsBackgroundTask
        };
    }
}
