// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Validation;

/// <summary>Checks of one kind that did not run to a conclusion.</summary>
/// <param name="Rule">
/// <c>binding-not-checked</c> (a required binding the loaded terminology could not decide), <c>extensible-binding-not-checked</c>
/// (cannot change a verdict), <c>profile-not-loaded</c>, <c>constraint-not-evaluated</c>, <c>slicing-deferred</c>,
/// <c>validation-nesting-limit</c>, <c>terminology-error</c> or <c>schema-not-found</c>.
/// </param>
/// <param name="Checks">How many such checks.</param>
/// <param name="Resources">In how many resources.</param>
public sealed record NotCheckedCount(string Rule, int Checks, int Resources);
