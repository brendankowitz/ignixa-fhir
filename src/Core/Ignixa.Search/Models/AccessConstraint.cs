// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Search.Expressions;

namespace Ignixa.Search.Models;

/// <summary>
/// A restriction on which resources of one type a caller may see, independent of what they searched for.
/// Expressed as an ordinary <see cref="Expression"/> so both the SQL compiler and a document-store query
/// builder enforce identical semantics from one source, rather than each re-deriving the rule from claims.
/// </summary>
/// <remarks>
/// The compiler applies constraints to every stage that produces rows — the match set, each include and
/// :iterate stage, and each chain target — not only the match set. Applying them at the match set alone
/// would let an _include reach a resource the caller may not see, which is the failure mode an
/// expression-rewriting approach is prone to.
/// </remarks>
/// <param name="ResourceType">The resource type the constraint governs.</param>
/// <param name="Predicate">What must hold for a resource of that type to be visible.</param>
public sealed record AccessConstraint(string ResourceType, Expression Predicate);
