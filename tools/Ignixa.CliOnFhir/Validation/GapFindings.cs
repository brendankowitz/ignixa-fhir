// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Validation;

namespace Ignixa.CliOnFhir.Validation;

/// <summary>What <see cref="ValidatorGapDetectors"/> found in one resource.</summary>
/// <param name="Issues">The skipped-check issues, each with its summary form.</param>
/// <param name="UndeterminableReferences">References whose target type could not be told, so were not checked.</param>
/// <param name="DisplayOnlyReferences">References with only a display, so nothing to check.</param>
internal sealed record GapFindings(IReadOnlyList<(ValidationIssue Raw, ClassifiedIssue Classified)> Issues, int UndeterminableReferences, int DisplayOnlyReferences);
