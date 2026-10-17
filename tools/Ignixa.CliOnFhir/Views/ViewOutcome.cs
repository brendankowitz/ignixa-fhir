// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Session;

namespace Ignixa.CliOnFhir.Views;

/// <summary>Everything a run worked out, from which its response is built.</summary>
/// <param name="Session">The folder.</param>
/// <param name="Request">The call.</param>
/// <param name="Loaded">The ViewDefinition as the call named it.</param>
/// <param name="View">The validated view.</param>
/// <param name="Scope">The resources the view read.</param>
/// <param name="Pass">What the pass over the rows produced.</param>
/// <param name="Paging">The rows asked for in the response.</param>
/// <param name="Budget">The response budget in bytes.</param>
/// <param name="Variables">The variables applied.</param>
/// <param name="AsOf">The session's instant, the view's <c>%asOf</c> unless it declares its own.</param>
/// <param name="Unreadable">Resources in scope that could not be read.</param>
/// <param name="Distinct">The distinct count, when asked for.</param>
/// <param name="Issues">Problems and notes so far.</param>
/// <param name="Omitted">What was left out so far.</param>
internal sealed record ViewOutcome(
    FolderSession Session,
    ViewRequest Request,
    LoadedView Loaded,
    CompiledView View,
    ViewScope Scope,
    ViewPass Pass,
    ViewPaging Paging,
    int Budget,
    IReadOnlyDictionary<string, string> Variables,
    Clock.AsOfFacts AsOf,
    int Unreadable,
    DistinctFacts? Distinct,
    List<IssueItem> Issues,
    List<OmittedItem> Omitted);
