// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Terminology.DrugClasses;

/// <summary>terminology class-members: the RxNorm codes of one drug class, by default only those the folder uses.</summary>
/// <param name="Scope"><c>folder</c> or <c>all</c>.</param>
/// <param name="Rule">Where product and ingredient members come from, and what the folder scope compares.</param>
/// <param name="ProductMembers">Product-level members RxClass lists for the class.</param>
/// <param name="IngredientMembers">Ingredient-level members RxClass lists for the class.</param>
/// <param name="FolderCodes">Distinct RxNorm codes of the folder's medication resources; null when the scope is <c>all</c>.</param>
/// <param name="Total">Members in scope.</param>
public sealed record DrugClassMembersFacts(
    string ClassId,
    string ClassSource,
    string Scope,
    string Rule,
    int ProductMembers,
    int IngredientMembers,
    int? FolderCodes,
    int Total,
    int Offset,
    int Returned,
    IReadOnlyList<DrugClassMember> Members,
    RxClassSource Source);
