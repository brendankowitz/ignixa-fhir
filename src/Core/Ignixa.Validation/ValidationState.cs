// <copyright file="ValidationState.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;

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
    /// The maximum number of nested element/contained-resource descents the validator will make
    /// before it stops and reports rather than recursing further.
    /// <para>
    /// The compiled schema graph is already finite — <c>StructureDefinitionSchemaBuilder</c> cycle-guards
    /// type recursion at build time, and the deepest nesting the R4 core schema produces is a handful
    /// of element levels. Runtime depth is therefore bounded by the document, not the schema, and the
    /// only ways to exceed this are contained-within-contained nesting (which dom-2 forbids) or a
    /// hostile instance.
    /// </para>
    /// <para>
    /// Note this is the inner of two guards: System.Text.Json's own <c>MaxDepth</c> also defaults to
    /// 64 and, since every nesting level costs at least one JSON level, usually rejects such a document
    /// before the validator sees it. This limit is what holds when a caller raises that ceiling or
    /// builds the element tree from a non-JSON source.
    /// </para>
    /// </summary>
    public const int MaxNestingDepth = 64;

    /// <summary>
    /// Gets how many nested element or contained-resource levels below the validation root this state
    /// sits. Incremented by <see cref="TryDescend"/> at each descent.
    /// </summary>
    public int NestingDepth { get; init; }

    /// <summary>
    /// Attempts to descend one nesting level. Returns false once <see cref="MaxNestingDepth"/> is
    /// reached, so the caller can report the truncation instead of recursing further; a validator that
    /// silently stopped walking would report a clean result for a subtree it never looked at.
    /// </summary>
    /// <param name="descended">The state one level deeper, when the limit has not been reached.</param>
    /// <returns>True if the descent is permitted; otherwise, false.</returns>
    public bool TryDescend(out ValidationState descended)
    {
        if (NestingDepth >= MaxNestingDepth)
        {
            descended = this;
            return false;
        }

        descended = this with { NestingDepth = NestingDepth + 1 };
        return true;
    }

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
    /// <remarks>
    /// The reference index is built on the resolver's first call, not here. Seeding a scope is now the
    /// default for every <c>ValidationSchema.Validate</c>, including the resource-write path, but the
    /// index has exactly one consumer - <c>resolve()</c> - so the common resource (no <c>contained</c>,
    /// not a Bundle or Parameters, no invariant that resolves) paid a whole-tree walk for an index
    /// nobody read. Deferring is a pure timing change: the index is still built at most once per scope,
    /// and <see cref="ResourceScope.Resolver"/> is still non-null exactly when a scope is seeded, which
    /// is the signal <c>ReferenceResolutionCheck</c> and <c>FhirPathInvariantCheck</c> switch on.
    /// </remarks>
    public ValidationState EnterRootResource(IElement resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        ReferenceIndex? index = null;
        return this with
        {
            Scope = new ResourceScope
            {
                Resource = resource,
                RootResource = resource,
                Resolver = reference => (index ??= ReferenceIndex.Build(resource)).Resolve(reference)
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
    /// <remarks>
    /// Deferred like <see cref="EnterRootResource"/>, and the chain composes: the parent resolver is
    /// only invoked on a local miss, so entering a contained resource does not force the parent's index.
    /// The captured cell is written at most once with an already-constructed immutable
    /// <see cref="ReferenceIndex"/>, so a concurrent first call can at worst duplicate the build - it
    /// cannot observe a half-built index. That is deliberate: no <c>ValidationState</c> in the codebase
    /// crosses a thread (the whole validation walk is synchronous and every state is created inside the
    /// call that consumes it), so paying for synchronisation here would buy nothing.
    /// </remarks>
    public ValidationState EnterContainedResource(IElement contained)
    {
        ArgumentNullException.ThrowIfNull(contained);

        var parentScope = Scope;
        var parentResolver = parentScope.Resolver;
        ReferenceIndex? index = null;

        return this with
        {
            Scope = parentScope with
            {
                Resource = contained,
                RootResource = parentScope.Resource,
                Resolver = reference =>
                    (index ??= ReferenceIndex.Build(contained)).Resolve(reference)
                    ?? parentResolver?.Invoke(reference)
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
