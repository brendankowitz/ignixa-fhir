// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;

namespace Ignixa.PackageManagement.Infrastructure.Snapshot;

/// <summary>
/// Generates a StructureDefinition <c>snapshot.element</c> list from a differential + base
/// chain. Pure orchestration over raw FHIR JSON — resolves the <c>baseDefinition</c> to a base
/// snapshot (recursively), then delegates the field-level merge to <see cref="ElementMerger"/>.
/// <para>
/// Parity with the Rust <c>rh-foundation</c> generator: if the StructureDefinition already
/// carries a <c>snapshot</c> it is used as-is (never regenerated); otherwise the base chain is
/// walked with cycle detection over canonical URLs.
/// </para>
/// </summary>
/// <remarks>
/// Base resolution is delegated to an <see cref="ISnapshotBaseResolver"/> so the generator stays
/// a pure function: unit tests and the shipped-snapshot oracle inject base JSON directly, while
/// production wiring (<see cref="PackageSnapshotBaseResolver"/>) resolves package profiles and
/// projects core types.
/// </remarks>
public sealed class SnapshotGenerator
{
    private const string SnapshotProperty = "snapshot";
    private const string DifferentialProperty = "differential";
    private const string ElementProperty = "element";
    private const string UrlProperty = "url";
    private const string BaseDefinitionProperty = "baseDefinition";

    /// <summary>
    /// Produces the snapshot <c>element</c> array for <paramref name="structureDefinition"/>, or
    /// <c>null</c> when it cannot be generated (no snapshot, and no resolvable base + differential).
    /// </summary>
    /// <param name="structureDefinition">The StructureDefinition JSON to snapshot.</param>
    /// <param name="resolver">Resolves <c>baseDefinition</c> canonical URLs to base JSON.</param>
    /// <returns>A fresh, parentless snapshot <c>element</c> array, or <c>null</c>.</returns>
    /// <exception cref="SnapshotGenerationException">A circular <c>baseDefinition</c> chain was detected.</exception>
    public JsonArray? GenerateSnapshotElements(JsonObject structureDefinition, ISnapshotBaseResolver resolver)
        => GenerateSnapshotElements(structureDefinition, resolver, out _);

    /// <summary>
    /// Produces the snapshot <c>element</c> array, additionally reporting the <c>baseDefinition</c>
    /// canonical URL that could not be resolved when generation fails for that reason, so callers can
    /// log an observable, actionable degrade instead of a silent null.
    /// </summary>
    /// <param name="structureDefinition">The StructureDefinition JSON to snapshot.</param>
    /// <param name="resolver">Resolves <c>baseDefinition</c> canonical URLs to base JSON.</param>
    /// <param name="unresolvedBaseDefinition">
    /// On a <c>null</c> return caused by an unresolvable base, the canonical URL of the base that
    /// could not be resolved; otherwise <c>null</c>.
    /// </param>
    /// <returns>A fresh, parentless snapshot <c>element</c> array, or <c>null</c>.</returns>
    /// <exception cref="SnapshotGenerationException">A circular <c>baseDefinition</c> chain was detected.</exception>
    public JsonArray? GenerateSnapshotElements(
        JsonObject structureDefinition,
        ISnapshotBaseResolver resolver,
        out string? unresolvedBaseDefinition)
    {
        ArgumentNullException.ThrowIfNull(structureDefinition);
        ArgumentNullException.ThrowIfNull(resolver);

        var visited = new HashSet<string>(StringComparer.Ordinal);
        unresolvedBaseDefinition = null;
        return Generate(structureDefinition, resolver, visited, ref unresolvedBaseDefinition);
    }

    private JsonArray? Generate(
        JsonObject sd,
        ISnapshotBaseResolver resolver,
        HashSet<string> visited,
        ref string? unresolvedBaseDefinition)
    {
        var snapshot = SnapshotJson.GetObject(sd, SnapshotProperty);
        if (snapshot is not null && SnapshotJson.GetArray(snapshot, ElementProperty) is { Count: > 0 } existing)
        {
            return SnapshotJson.CloneElements(existing);
        }

        var url = SnapshotJson.GetString(sd, UrlProperty);
        if (url is not null && !visited.Add(url))
        {
            throw new SnapshotGenerationException(
                $"Circular baseDefinition chain detected while generating snapshot for '{url}'.");
        }

        var differential = SnapshotJson.GetObject(sd, DifferentialProperty) is { } diff
            ? SnapshotJson.GetArray(diff, ElementProperty)
            : null;

        var baseUrl = SnapshotJson.GetString(sd, BaseDefinitionProperty);
        if (baseUrl is null)
        {
            return differential is null ? null : SnapshotJson.CloneElements(differential);
        }

        var baseDefinition = resolver.ResolveStructureDefinition(baseUrl);
        if (baseDefinition is null)
        {
            unresolvedBaseDefinition = baseUrl;
            return null;
        }

        var baseElements = Generate(baseDefinition, resolver, visited, ref unresolvedBaseDefinition);
        if (baseElements is null)
        {
            return null;
        }

        if (differential is null || differential.Count == 0)
        {
            return baseElements;
        }

        return ElementMerger.Merge(baseElements, differential);
    }
}
