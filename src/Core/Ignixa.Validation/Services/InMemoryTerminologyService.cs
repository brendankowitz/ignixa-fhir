// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Abstractions.Terminology;
using Ignixa.Validation.Services.Terminology;

namespace Ignixa.Validation.Services;

/// <summary>
/// Package source behind <see cref="ComposingTerminologyService"/> for standalone, serverless validation.
/// </summary>
public class InMemoryTerminologyService : ITerminologyService
{
    private readonly PackageTerminologySource _source;
    private readonly ComposingTerminologyService _composer;

    /// <summary>
    /// Initializes a new instance of the InMemoryTerminologyService using a ValueSet provider.
    /// </summary>
    /// <param name="valueSetProvider">The value set provider to use for terminology validation.</param>
    /// <param name="codeSystemProvider">
    /// Optional CodeSystem content surface consulted by package terminology lookup for
    /// code&#8594;display resolution. Does not affect value-set binding validation.
    /// </param>
    public InMemoryTerminologyService(
        IValueSetProvider valueSetProvider,
        ICodeSystemProvider? codeSystemProvider = null)
        : this(new PackageTerminologySource(valueSetProvider, codeSystemProvider))
    {
    }

    /// <summary>
    /// Initializes the service with a primary ValueSet provider plus additional sources
    /// (e.g. package-loaded IG ValueSets). On lookup the additional sources are queried
    /// in order before falling back to the primary provider, so IG-defined ValueSets
    /// override base-spec ones with the same canonical.
    /// </summary>
    /// <param name="primary">Base-spec ValueSet provider (e.g. <c>R4ValueSetProvider</c>).</param>
    /// <param name="additional">Additional sources, queried first.</param>
    /// <param name="codeSystemProvider">
    /// Optional CodeSystem content surface consulted by package terminology lookup for
    /// code&#8594;display resolution. Does not affect value-set binding validation.
    /// </param>
    public InMemoryTerminologyService(
        IValueSetProvider primary,
        IEnumerable<IValueSetProvider> additional,
        ICodeSystemProvider? codeSystemProvider = null)
        : this(new PackageTerminologySource(primary, additional, codeSystemProvider))
    {
    }

    private InMemoryTerminologyService(PackageTerminologySource source)
    {
        _source = source;
        _composer = new ComposingTerminologyService([source]);
    }

    public Task<ConceptLookup> LookupAsync(LookupRequest request, CancellationToken cancellationToken) =>
        _composer.LookupAsync(request, cancellationToken);

    public Task<ValidateCodeResult> ValidateCodeInCodeSystemAsync(
        ValidateCodeRequest request,
        CancellationToken cancellationToken) =>
        _composer.ValidateCodeInCodeSystemAsync(request, cancellationToken);

    public Task<SubsumesOutcome> SubsumesAsync(SubsumesRequest request, CancellationToken cancellationToken) =>
        _composer.SubsumesAsync(request, cancellationToken);

    // Direct to the source: Hybrid's empty-URL error is server behavior, while standalone validation has
    // always warned and skipped when the binding omits a ValueSet.
    public Task<TerminologyValidationResult> ValidateCodeAsync(
        string? system,
        string? code,
        string? display,
        string? valueSetUrl,
        CancellationToken cancellationToken) =>
        _source.ValidateCodeAsync(system, code, display, valueSetUrl, cancellationToken);

    public Task<BindingValidationResult> ValidateBindingAsync(
        string valueSetUrl,
        BindingStrength strength,
        string? system,
        string? code,
        string? display,
        string? version,
        CancellationToken cancellationToken) =>
        _source.ValidateBindingAsync(valueSetUrl, strength, system, code, display, version, cancellationToken);

    public Task<ExpandResult?> ExpandValueSetAsync(
        ExpansionParameters parameters,
        CancellationToken cancellationToken) =>
        _source.ExpandValueSetAsync(parameters, cancellationToken);

    public Task<TranslateResult> TranslateCodeAsync(
        TranslateParameters parameters,
        CancellationToken cancellationToken) =>
        _source.TranslateCodeAsync(parameters, cancellationToken);
}
