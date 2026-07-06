// <copyright file="ValidationState.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using Ignixa.Abstractions;

namespace Ignixa.Validation;

/// <summary>
/// Immutable validation state threaded through the validation pipeline.
/// Provides context at three levels: Global (run), Instance (resource), Location (element).
/// </summary>
public record ValidationState
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ValidationState"/> class.
    /// </summary>
    public ValidationState()
    {
        Global = new GlobalState();
        Instance = new InstanceState();
        Location = new LocationState();
    }

    private ValidationState(GlobalState global, InstanceState instance, LocationState location)
    {
        Global = global;
        Instance = instance;
        Location = location;
    }

    /// <summary>
    /// Gets the global state shared across the entire validation run.
    /// </summary>
    public GlobalState Global { get; init; }

    /// <summary>
    /// Gets the instance-level state for the current resource being validated.
    /// </summary>
    public InstanceState Instance { get; init; }

    /// <summary>
    /// Gets the location state for the current element being validated.
    /// </summary>
    public LocationState Location { get; init; }

    /// <summary>
    /// Gets the FHIRPath tree-context scope (%resource, %rootResource, resolve()) for the
    /// resource currently being validated. Seeded at resource boundaries via
    /// <see cref="EnterRootResource"/> / <see cref="EnterContainedResource"/>.
    /// </summary>
    public ResourceScope Scope { get; init; } = new();

    /// <summary>
    /// Creates a new state with updated instance information.
    /// </summary>
    /// <param name="resourceType">The resource type being validated (e.g., "Patient").</param>
    /// <param name="resourceId">The resource ID being validated (optional).</param>
    /// <returns>A new validation state with updated instance information.</returns>
    public ValidationState WithInstance(string resourceType, string? resourceId)
    {
        return this with
        {
            Instance = new InstanceState
            {
                ResourceType = resourceType,
                ResourceId = resourceId
            }
        };
    }

    /// <summary>
    /// Creates a new state with updated location information.
    /// </summary>
    /// <param name="instancePath">The FHIRPath expression for the current element.</param>
    /// <param name="definitionPath">The StructureDefinition path (optional).</param>
    /// <returns>A new validation state with updated location information.</returns>
    public ValidationState WithLocation(string instancePath, string? definitionPath = null)
    {
        return this with
        {
            Location = new LocationState
            {
                InstancePath = instancePath,
                DefinitionPath = definitionPath
            }
        };
    }

    /// <summary>
    /// Enters a resource that becomes a validation root: a standalone resource or an independent
    /// Bundle entry. Both %resource and %rootResource point at the resource itself (a Bundle entry's
    /// resource is not "contained" in the Bundle in the FHIRPath sense). Builds a fresh resolver
    /// rooted at this resource.
    /// </summary>
    /// <param name="resource">The resource element becoming the validation root.</param>
    /// <returns>A new validation state scoped to this resource.</returns>
    public ValidationState EnterRootResource(IElement resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        var index = ReferenceIndex.Build(resource);
        return this with
        {
            Scope = new ResourceScope
            {
                Resource = resource,
                RootResource = resource,
                Resolver = index.Resolve
            }
        };
    }

    /// <summary>
    /// Enters a contained resource C inside the current parent resource P: %resource becomes C and
    /// %rootResource becomes P (the containing resource). The resolver chains C's own contained set
    /// to the parent scope's resolver, matching FHIR resolution order (contained-of-current then
    /// bundle/contained-of-root).
    /// </summary>
    /// <param name="contained">The contained resource element being entered.</param>
    /// <returns>A new validation state scoped to the contained resource.</returns>
    public ValidationState EnterContainedResource(IElement contained)
    {
        ArgumentNullException.ThrowIfNull(contained);

        var parentScope = Scope;
        var index = ReferenceIndex.Build(contained);
        var parentResolver = parentScope.Resolver;

        return this with
        {
            Scope = parentScope with
            {
                Resource = contained,
                RootResource = parentScope.Resource,
                Resolver = reference => index.Resolve(reference) ?? parentResolver?.Invoke(reference)
            }
        };
    }

    /// <summary>
    /// Global state shared across all validations in a run.
    /// </summary>
    public class GlobalState
    {
        /// <summary>
        /// Gets or sets the number of resources validated in this run.
        /// </summary>
        public int ResourcesValidated { get; set; }

        /// <summary>
        /// Gets a cache for expensive computations (e.g., compiled FHIRPath expressions).
        /// </summary>
        public Dictionary<string, object> Cache { get; } = new();
    }

    /// <summary>
    /// Instance-level state for the current resource.
    /// </summary>
    public class InstanceState
    {
        /// <summary>
        /// Gets or sets the resource type being validated (e.g., "Patient").
        /// </summary>
        public string? ResourceType { get; set; }

        /// <summary>
        /// Gets or sets the resource ID being validated.
        /// </summary>
        public string? ResourceId { get; set; }
    }

    /// <summary>
    /// Location state for the current element.
    /// </summary>
    public class LocationState
    {
        /// <summary>
        /// Gets or sets the FHIRPath expression for the current element (e.g., "Patient.name[0]").
        /// </summary>
        public string InstancePath { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the StructureDefinition path (e.g., "Patient.name").
        /// </summary>
        public string? DefinitionPath { get; set; }
    }
}
