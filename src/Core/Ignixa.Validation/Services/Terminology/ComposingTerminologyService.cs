// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Abstractions.Terminology;

namespace Ignixa.Validation.Services.Terminology;

/// <summary>
/// Routes each terminology operation to sources that hold its canonical and combines their three-state answers.
/// Never answers for content that no source holds.
/// </summary>
public sealed class ComposingTerminologyService : ITerminologyService
{
    private const string ValueSetUrlRequired = "ValueSet URL is required";

    private readonly SourceRouter _router;

    public ComposingTerminologyService(IEnumerable<ITerminologySource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        var list = sources.ToList();
        if (list.Count == 0)
        {
            throw new ArgumentException("At least one terminology source is required.", nameof(sources));
        }

        _router = new SourceRouter(list);
    }

    public async Task<ConceptLookup> LookupAsync(LookupRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var routed = await RoutedLookupAsync(request.Code, cancellationToken);
        return routed.Answer.Result == Determination.Unknown
            ? ConceptLookup.Undetermined(routed.Unknowns)
            : routed.Answer;
    }

    public async Task<ValidateCodeResult> ValidateCodeInCodeSystemAsync(
        ValidateCodeRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var routed = await RoutedLookupAsync(request.Code, cancellationToken);
        return routed.Answer.Result switch
        {
            Determination.True => Valid(request, routed.Answer.Concept!.Display),
            Determination.False => new ValidateCodeResult(
                Determination.False,
                null,
                [new TerminologyIssue(
                    IssueSeverity.Error,
                    TerminologyIssueKind.CodeInvalid,
                    $"Code '{request.Code.Code}' is not defined in '{request.Code.CodeSystem}'")],
                []),
            _ => new ValidateCodeResult(Determination.Unknown, null, [], routed.Unknowns),
        };
    }

    public async Task<SubsumesOutcome> SubsumesAsync(SubsumesRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var routed = await _router.AskAsync(
            CanonicalKind.CodeSystem,
            request.CodeSystem,
            (source, ct) => source.SubsumesAsync(request.CodeA, request.CodeB, ct),
            reason => new SubsumesOutcome.Undetermined(reason),
            cancellationToken);
        return routed.Answer is SubsumesOutcome.Undetermined
            ? new SubsumesOutcome.Undetermined(routed.Unknowns)
            : routed.Answer;
    }

    public async Task<TerminologyValidationResult> ValidateCodeAsync(
        string? system,
        string? code,
        string? display,
        string? valueSetUrl,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(valueSetUrl))
        {
            return new TerminologyValidationResult(false, IssueSeverity.Error, ValueSetUrlRequired);
        }

        var legacy = await _router.SelectLegacyAsync(new CanonicalRef(valueSetUrl), cancellationToken);
        return await legacy.ValidateCodeAsync(system, code, display, valueSetUrl, cancellationToken);
    }

    public async Task<BindingValidationResult> ValidateBindingAsync(
        string valueSetUrl,
        BindingStrength strength,
        string? system,
        string? code,
        string? display,
        string? version,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(valueSetUrl))
        {
            return new BindingValidationResult(false, strength, IssueSeverity.Error, ValueSetUrlRequired, null);
        }

        var legacy = await _router.SelectLegacyAsync(new CanonicalRef(valueSetUrl), cancellationToken);
        return await legacy.ValidateBindingAsync(
            valueSetUrl,
            strength,
            system,
            code,
            display,
            version,
            cancellationToken);
    }

    public async Task<ExpandResult?> ExpandValueSetAsync(
        ExpansionParameters parameters,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var legacy = await _router.SelectLegacyAsync(new CanonicalRef(parameters.Url), cancellationToken);
        return await legacy.ExpandValueSetAsync(parameters, cancellationToken);
    }

    public Task<TranslateResult> TranslateCodeAsync(TranslateParameters parameters, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        return _router.FirstLegacy.TranslateCodeAsync(parameters, cancellationToken);
    }

    private Task<RoutedAnswer<ConceptLookup>> RoutedLookupAsync(CodeRef code, CancellationToken cancellationToken) =>
        _router.AskAsync(
            CanonicalKind.CodeSystem,
            code.CodeSystem,
            (source, ct) => source.LookupAsync(code, ct),
            ConceptLookup.Undetermined,
            cancellationToken);

    private static ValidateCodeResult Valid(ValidateCodeRequest request, string? display)
    {
        var mismatch = request.Display is not null
            && !string.IsNullOrEmpty(display)
            && !string.Equals(request.Display, display, StringComparison.Ordinal);
        IReadOnlyList<TerminologyIssue> issues = mismatch
            ? [new TerminologyIssue(
                IssueSeverity.Warning,
                TerminologyIssueKind.DisplayMismatch,
                $"Display '{request.Display}' does not match expected '{display}'")]
            : [];
        return new ValidateCodeResult(Determination.True, display, issues, []);
    }
}
