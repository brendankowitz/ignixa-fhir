// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Serialization;

namespace Ignixa.CliOnFhir.Views;

/// <summary>What a run of a view produced.</summary>
/// <param name="View">The ViewDefinition's source and resource type.</param>
/// <param name="Columns">The output columns, in order; <paramref name="Rows"/> hold one cell per column in this order.</param>
/// <param name="Total">Every row the view produced over the resources in scope, whatever was returned.</param>
/// <param name="Resources">Resources of the view's type in scope (the folder's, or the compartment's).</param>
/// <param name="Provenance">How far evidence can say which resource rows came from.</param>
/// <param name="Unreadable">Resources in scope that could not be read and so produced no rows; absent when none (each is an issue).</param>
/// <param name="Where">The extra filter that was applied, as given.</param>
/// <param name="Variables">The variables that were applied, as given.</param>
/// <param name="Compartment">The compartment the resources were limited to.</param>
/// <param name="Offset">Rows skipped before <paramref name="Rows"/>; absent when rows were written to a file.</param>
/// <param name="Returned">Rows in <paramref name="Rows"/>; absent when rows were written to a file.</param>
/// <param name="Rows">The rows from <paramref name="Offset"/>, one array of cells per row; absent when rows were written to a file.</param>
/// <param name="Distinct">The distinct count that was asked for.</param>
/// <param name="File">The file every row was written to, when <c>out</c> was given.</param>
public sealed record ViewFacts(
    ViewSourceFacts View,
    IReadOnlyList<ViewColumn> Columns,
    long Total,
    int Resources,
    ViewProvenance Provenance,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Unreadable = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Where = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, string>? Variables = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Compartment = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Offset = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Returned = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<IReadOnlyList<object?>>? Rows = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DistinctFacts? Distinct = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ViewFileFacts? File = null);
