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
/// In-memory terminology service with version-specific ValueSets.
/// Returns warnings for unknown ValueSets to enable graceful degradation.
/// Intended for testing and prototype scenarios - production systems should use external terminology servers.
/// </summary>
public class InMemoryTerminologyService : ITerminologyService
{
    private readonly PackageTerminologySource _source;

    /// <summary>
    /// Initializes a new instance of the InMemoryTerminologyService using a ValueSet provider.
    /// </summary>
    /// <param name="valueSetProvider">The value set provider to use for terminology validation.</param>
    /// <param name="codeSystemProvider">
    /// Optional CodeSystem content surface consulted by <see cref="LookupCodeAsync"/> for
    /// code&#8594;display resolution. Does not affect value-set binding validation.
    /// </param>
    public InMemoryTerminologyService(
        IValueSetProvider valueSetProvider,
        ICodeSystemProvider? codeSystemProvider = null)
    {
        _source = new PackageTerminologySource(valueSetProvider, codeSystemProvider);
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
    /// Optional CodeSystem content surface consulted by <see cref="LookupCodeAsync"/> for
    /// code&#8594;display resolution. Does not affect value-set binding validation.
    /// </param>
    public InMemoryTerminologyService(
        IValueSetProvider primary,
        IEnumerable<IValueSetProvider> additional,
        ICodeSystemProvider? codeSystemProvider = null)
    {
        ArgumentNullException.ThrowIfNull(primary);
        ArgumentNullException.ThrowIfNull(additional);
        _source = new PackageTerminologySource(primary, additional, codeSystemProvider);
    }

    public Task<TerminologyValidationResult> ValidateCodeAsync(
        string? system,
        string? code,
        string? display,
        string? valueSetUrl,
        CancellationToken cancellationToken) =>
        _source.ValidateCodeAsync(system, code, display, valueSetUrl, cancellationToken);

    /// <summary>
    /// $lookup against locally-loaded CodeSystem content. When a <see cref="ICodeSystemProvider"/>
    /// was supplied and it enumerates the (system, code), returns the concept with its display;
    /// otherwise reports not found (the in-memory service has no external terminology server).
    /// </summary>
    public async Task<LookupResult> LookupCodeAsync(
        string system,
        string code,
        string? version,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(system) || string.IsNullOrEmpty(code))
        {
            return new LookupResult(false, null, null, null, null, null, null);
        }

        var lookup = await _source.LookupAsync(new CodeRef(system, code, version), cancellationToken);
        return lookup.Concept is { } concept
            ? new LookupResult(true, concept.Name, concept.Version, concept.Display, concept.Definition, concept.Properties, concept.Designations)
            : new LookupResult(false, null, null, null, null, null, null);
    }

    /// <summary>
    /// $expand operation is not supported by the in-memory implementation.
    /// Always returns null (expansion not available).
    /// </summary>
    public Task<ExpandResult?> ExpandValueSetAsync(
        ExpansionParameters parameters,
        CancellationToken cancellationToken) =>
        _source.ExpandValueSetAsync(parameters, cancellationToken);

    /// <summary>
    /// Validates a coded element against a terminology binding.
    /// In-memory implementation uses hardcoded ValueSets for common bindings.
    /// </summary>
    public Task<BindingValidationResult> ValidateBindingAsync(
        string valueSetUrl,
        BindingStrength strength,
        string? system,
        string? code,
        string? display,
        string? version,
        CancellationToken cancellationToken) =>
        _source.ValidateBindingAsync(valueSetUrl, strength, system, code, display, version, cancellationToken);

    /// <summary>
    /// $translate operation is not supported by the in-memory implementation.
    /// Returns no matches.
    /// </summary>
    public Task<TranslateResult> TranslateCodeAsync(
        TranslateParameters parameters,
        CancellationToken cancellationToken) =>
        _source.TranslateCodeAsync(parameters, cancellationToken);

    /// <summary>
    /// $subsumes operation is not supported by the in-memory implementation.
    /// Returns not-subsumed.
    /// </summary>
    public Task<SubsumesResult> SubsumesAsync(
        SubsumesParameters parameters,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(new SubsumesResult("not-subsumed"));
    }
}
