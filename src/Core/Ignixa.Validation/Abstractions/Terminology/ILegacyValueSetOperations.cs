// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Validation.Abstractions;

namespace Ignixa.Validation.Abstractions.Terminology;

/// <summary>
/// Transitional: whole-call ValueSet and ConceptMap operations that are routed by canonical until their
/// composed replacements land (validate/binding in Phase 2, expand in Phase 4, translate in Phase 5).
/// Deleted in Phase 5.
/// </summary>
public interface ILegacyValueSetOperations
{
    Task<TerminologyValidationResult> ValidateCodeAsync(string? system, string? code, string? display, string? valueSetUrl, CancellationToken cancellationToken);

    Task<BindingValidationResult> ValidateBindingAsync(string valueSetUrl, BindingStrength strength, string? system, string? code, string? display, string? version, CancellationToken cancellationToken);

    Task<ExpandResult?> ExpandValueSetAsync(ExpansionParameters parameters, CancellationToken cancellationToken);

    Task<TranslateResult> TranslateCodeAsync(TranslateParameters parameters, CancellationToken cancellationToken);
}
