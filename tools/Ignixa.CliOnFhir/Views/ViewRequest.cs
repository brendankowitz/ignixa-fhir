// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Views;

/// <summary>One run of a SQL-on-FHIR ViewDefinition over the folder (<c>sql_view</c>, CLI <c>view</c>).</summary>
/// <param name="View">
/// The ViewDefinition: inline JSON (text starting with <c>{</c>), the path of a file holding one, or the id
/// (<c>vd1</c> or <c>ViewDefinition/vd1</c>) or url (<c>url</c> or <c>url|version</c>) of a ViewDefinition in the folder.
/// </param>
/// <param name="Where">An extra FHIRPath boolean a resource must satisfy, ANDed with the view's own <c>where</c> clauses.</param>
/// <param name="Variables"><c>name=value</c> FHIRPath variables; each overrides the value of the ViewDefinition constant of that name.</param>
/// <param name="Compartment">Only the view's resources in this Patient compartment, <c>Patient/&lt;id&gt;</c>.</param>
/// <param name="Count">Rows returned in the response (default <see cref="ViewPaging.DefaultCount"/>, at most <see cref="ViewPaging.MaxCount"/>, 0 for none); not with <paramref name="Out"/>.</param>
/// <param name="Offset">Rows to skip before the returned ones; not with <paramref name="Out"/>.</param>
/// <param name="Distinct">A column whose distinct values are counted over every row (a cohort size is the distinct patients of a view).</param>
/// <param name="Out">Write every row to this file, outside the folder, instead of returning rows.</param>
/// <param name="Format"><c>csv</c>, <c>ndjson</c> or <c>parquet</c>; with <paramref name="Out"/>, else taken from its extension.</param>
/// <param name="Overwrite">Replace <paramref name="Out"/> when it exists; otherwise that is refused.</param>
/// <param name="MaxBytes">The response budget, as for the other tools that return resources.</param>
public sealed record ViewRequest(
    string View,
    string? Where = null,
    IReadOnlyList<string>? Variables = null,
    string? Compartment = null,
    int? Count = null,
    int Offset = 0,
    string? Distinct = null,
    string? Out = null,
    string? Format = null,
    bool Overwrite = false,
    int? MaxBytes = null);
