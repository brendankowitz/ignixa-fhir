// <copyright file="ValidationState.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

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
