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
    private ValidationState(ResourceScope scope)
    {
        Global = new GlobalState();
        Instance = new InstanceState();
        Location = new LocationState();
        Scope = scope;
    }

    /// <summary>
    /// Creates a state rooted at <paramref name="resource"/>: the only way to obtain a
    /// <see cref="ValidationState"/>. Both <c>%resource</c> and <c>%rootResource</c> point at the
    /// resource itself, which is correct for a standalone resource and for an independent Bundle entry
    /// (a Bundle entry's resource is not "contained" in the Bundle in the FHIRPath sense).
    /// </summary>
    /// <param name="resource">The resource element this validation is rooted at.</param>
    /// <returns>A validation state scoped to <paramref name="resource"/>.</returns>
    /// <remarks>
    /// <para>
    /// There is deliberately no parameterless constructor. A state with no root is not a weaker state,
    /// it is a broken one: <c>%resource</c> would be empty, a constraint like <c>%resource.id = 'x'</c>
    /// would evaluate to empty, and <c>FhirPathInvariantCheck</c> reads empty as a failed constraint —
    /// so a conformant resource is rejected for a defect in the caller, and only at <c>Full</c> depth,
    /// where invariants actually run. That bug shipped once because seeding was a separate step a caller
    /// had to remember; making the root a construction parameter is what stops it recurring.
    /// </para>
    /// <para>
    /// Seeding costs nothing: it records two element references and walks nothing. Reference resolution
    /// is not set up here — see <see cref="ResourceScope"/> for where the <see cref="ReferenceIndex"/>
    /// that backs <c>resolve()</c> and <c>ReferenceResolutionCheck</c> is actually built, and why this
    /// type no longer builds one of its own.
    /// </para>
    /// </remarks>
    public static ValidationState ForRoot(IElement resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        return new ValidationState(new ResourceScope
        {
            Resource = resource,
            RootResource = resource
        });
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
    /// resource currently being validated. Established by <see cref="ForRoot"/> at construction and
    /// re-pointed at resource boundaries by <see cref="EnterContainedResource"/>; never absent.
    /// </summary>
    public ResourceScope Scope { get; init; }

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
    /// Enters a contained resource C inside the current parent resource P: %resource becomes C and
    /// %rootResource becomes P (the containing resource).
    /// </summary>
    /// <param name="contained">The contained resource element being entered.</param>
    /// <returns>A new validation state scoped to the contained resource.</returns>
    /// <remarks>
    /// Re-pointing %rootResource at P is what keeps contained-peer references (<c>#id</c> from one
    /// contained resource to another) resolvable: both consumers index
    /// <c>RootResource ?? Resource</c>, so from inside C they index P and therefore see P's whole
    /// contained pool. C's own pool is always empty - FHIR forbids nested contained - so indexing C
    /// would resolve nothing.
    /// </remarks>
    public ValidationState EnterContainedResource(IElement contained)
    {
        ArgumentNullException.ThrowIfNull(contained);

        var parentScope = Scope;

        return this with
        {
            Scope = parentScope with
            {
                Resource = contained,
                RootResource = parentScope.Resource
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
