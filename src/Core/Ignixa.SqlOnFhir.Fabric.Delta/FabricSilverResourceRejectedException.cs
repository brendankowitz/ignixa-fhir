// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Ignixa.SqlOnFhir.Fabric.Delta;

/// <summary>
/// A resource in a batch cannot be projected to its table's Silver schema. Thrown before any table is created or
/// changed, so the whole batch is rejected.
/// </summary>
/// <remarks>
/// The message names the table, the resource's position in that table's batch and the projection's FHIR path and
/// reason; it never contains a value read from the resource. <see cref="SourceId"/> carries the resource id only when it
/// is a well-formed FHIR id.
/// </remarks>
[SuppressMessage(
    "Design",
    "CA1032:Implement standard exception constructors",
    Justification = "Every rejection names its table and position; a parameterless or message-only constructor would create one that does not.")]
public sealed partial class FabricSilverResourceRejectedException : Exception
{
    internal FabricSilverResourceRejectedException(
        string tablePath,
        int position,
        string resourceType,
        string? rawId,
        FabricSilverProjectionException innerException)
        : base(
            $"Resource {position} of the batch for table '{tablePath}' cannot be written. {innerException.Message}",
            innerException)
    {
        TablePath = tablePath;
        Position = position;
        ResourceType = resourceType;
        SourceId = rawId is not null && WellFormedFhirId().IsMatch(rawId) ? rawId : null;
        FieldPath = innerException.Path;
    }

    /// <summary>The full path of the table whose batch holds the resource.</summary>
    public string TablePath { get; }

    /// <summary>The zero-based position of the resource in its table batch.</summary>
    public int Position { get; }

    /// <summary>The resource type of the table's contract.</summary>
    public string ResourceType { get; }

    /// <summary>The resource's <c>id</c> when it is a well-formed FHIR id; otherwise <see langword="null"/>.</summary>
    public string? SourceId { get; }

    /// <summary>Where in the resource the projection failed, for example <c>identifier[2].assigner.identifier.type</c>.</summary>
    public string FieldPath { get; }

    [GeneratedRegex("^[A-Za-z0-9\\-.]{1,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex WellFormedFhirId();
}
