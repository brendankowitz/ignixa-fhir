// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Collections.Frozen;
using Ignixa.Abstractions;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Abstractions.Terminology;

namespace Ignixa.Validation.Services.Terminology;

/// <summary>
/// Terminology answered from loaded package content: base-spec and IG ValueSets plus optional CodeSystem
/// content. Never authoritative for a CodeSystem, because package content may be a subset of the real system.
/// Package CodeSystem content is unversioned, so a lookup or subsumption that names an explicit CodeSystem
/// version is always undetermined (<see cref="UnknownKind.SystemVersionNotAvailable"/>).
/// </summary>
public sealed class PackageTerminologySource : ITerminologySource, ILegacyValueSetOperations
{
    /// <summary>
    /// Code systems that cannot be completely enumerated from local content (large external terminologies).
    /// A miss there is undecidable offline, not an absence.
    /// </summary>
    internal static readonly FrozenSet<string> NonEnumerableSystems = new[]
    {
        "http://snomed.info/sct",
        "http://loinc.org",
        "http://unitsofmeasure.org",
        "http://www.nlm.nih.gov/research/umls/rxnorm",
        "http://hl7.org/fhir/sid/icd-10",
        "http://hl7.org/fhir/sid/icd-10-cm",
        "http://hl7.org/fhir/sid/icd-9-cm",
        "http://hl7.org/fhir/sid/cvx",
        "http://www.ama-assn.org/go/cpt",
    }.ToFrozenSet(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<string, ValueSetMembership> _valueSets = new(StringComparer.Ordinal);
    private readonly IValueSetProvider _valueSetProvider;
    private readonly ICodeSystemProvider? _codeSystemProvider;

    public PackageTerminologySource(IValueSetProvider valueSetProvider, ICodeSystemProvider? codeSystemProvider = null)
    {
        ArgumentNullException.ThrowIfNull(valueSetProvider);
        _valueSetProvider = valueSetProvider;
        _codeSystemProvider = codeSystemProvider;
    }

    public PackageTerminologySource(IValueSetProvider primary, IEnumerable<IValueSetProvider> additional, ICodeSystemProvider? codeSystemProvider = null)
        : this(Layer(primary, additional), codeSystemProvider)
    {
    }

    public string Name => "package";

    public ValueTask<SourceCoverage> GetCoverageAsync(CanonicalKind kind, CanonicalRef canonical, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(canonical);
        var coverage = kind switch
        {
            CanonicalKind.CodeSystem => SourceCoverage.Partial,
            CanonicalKind.ValueSet when _valueSetProvider.IsKnownValueSet(canonical.Url) => SourceCoverage.Authoritative,
            _ => SourceCoverage.None,
        };
        return ValueTask.FromResult(coverage);
    }

    public Task<ConceptLookup> LookupAsync(CodeRef code, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(code);
        if (code.Version is not null)
        {
            return Task.FromResult(ConceptLookup.Undetermined(VersionNotProvable(code.CodeSystem)));
        }

        var lookup = _codeSystemProvider?.ContainsCode(code.System, code.Code) switch
        {
            true => ConceptLookup.Found(new ConceptDetails(null, null, _codeSystemProvider!.GetDisplay(code.System, code.Code), null, null, null)),
            false when NonEnumerableSystems.Contains(code.System) => ConceptLookup.Undetermined(new UnknownReason(
                UnknownKind.NonEnumerableSystem, code.System, $"'{code.System}' cannot be enumerated from package content")),
            false => ConceptLookup.NotFound,
            null => ConceptLookup.Undetermined(new UnknownReason(
                UnknownKind.SystemNotAvailable, code.System, $"Package content does not include CodeSystem '{code.System}'")),
        };
        return Task.FromResult(lookup);
    }

    public Task<SubsumesOutcome> SubsumesAsync(CodeRef codeA, CodeRef codeB, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(codeA);
        ArgumentNullException.ThrowIfNull(codeB);
        UnknownReason reason;
        if (codeA.Version is not null)
        {
            reason = VersionNotProvable(codeA.CodeSystem);
        }
        else if (codeB.Version is not null)
        {
            reason = VersionNotProvable(codeB.CodeSystem);
        }
        else
        {
            reason = new UnknownReason(UnknownKind.UnsupportedFilter, codeA.System, "Package terminology content carries no concept hierarchy");
        }

        return Task.FromResult<SubsumesOutcome>(new SubsumesOutcome.Undetermined(reason));
    }

    private static LayeredValueSetProvider Layer(IValueSetProvider primary, IEnumerable<IValueSetProvider> additional)
    {
        ArgumentNullException.ThrowIfNull(primary);
        ArgumentNullException.ThrowIfNull(additional);
        return new LayeredValueSetProvider(primary, additional);
    }

    /// <summary>
    /// Package content carries no CodeSystem version, so it cannot prove that any explicitly requested version
    /// is the one it holds; answering from it would turn an unloaded version into a definitive answer.
    /// </summary>
    private static UnknownReason VersionNotProvable(CanonicalRef codeSystem) =>
        new(
            UnknownKind.SystemVersionNotAvailable,
            codeSystem.ToString(),
            $"Package content is unversioned and cannot answer for CodeSystem version '{codeSystem.Version}'");

    /// <summary>
    /// Validates a code against a locally-expanded ValueSet, distinguishing three outcomes:
    /// verified-in-valueset (valid), verified-not-in-valueset (Error), and unverifiable (non-failing Warning).
    /// Membership is system-aware: when a system is supplied, matching is on the (system, code) pair, and a
    /// code whose system is not enumerated by the local expansion is reported as unverifiable rather than absent.
    /// </summary>
    /// <param name="system">The code system URL.</param>
    /// <param name="code">The code to validate.</param>
    /// <param name="display">The display text (not validated in this implementation).</param>
    /// <param name="valueSetUrl">The ValueSet canonical URL.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Validation result with severity and message.</returns>
    public Task<TerminologyValidationResult> ValidateCodeAsync(
        string? system,
        string? code,
        string? display,
        string? valueSetUrl,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return Task.FromResult(new TerminologyValidationResult(
                IsValid: false,
                Severity: IssueSeverity.Error,
                Message: "Code is required for terminology validation"));
        }

        if (string.IsNullOrWhiteSpace(valueSetUrl))
        {
            return Task.FromResult(new TerminologyValidationResult(
                IsValid: true,
                Severity: IssueSeverity.Warning,
                Message: "No ValueSet URL provided - skipping terminology validation"));
        }

        var normalizedUrl = valueSetUrl.Contains('|', StringComparison.Ordinal)
            ? valueSetUrl[..valueSetUrl.LastIndexOf('|')]
            : valueSetUrl;

        if (!_valueSets.TryGetValue(normalizedUrl, out var membership))
        {
            var providerCodes = _valueSetProvider.GetCodes(normalizedUrl);
            if (providerCodes is null)
            {
                return Task.FromResult(new TerminologyValidationResult(
                    IsValid: true,
                    Severity: IssueSeverity.Warning,
                    Message: $"Terminology validation unavailable for ValueSet '{valueSetUrl}' - provider does not contain this ValueSet"));
            }
            membership = _valueSets.GetOrAdd(
                normalizedUrl,
                static (_, codes) => new ValueSetMembership(codes),
                providerCodes);
        }

        var hasSystem = !string.IsNullOrWhiteSpace(system);

        if (hasSystem)
        {
            if (membership.ContainsPair(system!, code))
            {
                return Verified();
            }

            // A miss is only an authoritative "not in value set" (Error) when the local expansion can
            // COMPLETELY enumerate the code's system. Well-known external terminologies (SNOMED, LOINC,
            // UCUM, ...) can never be fully expanded offline — even when the value set enumerates a
            // subset of their codes — so a miss there is unverifiable (Warning), matching a validator
            // with no terminology server, rather than a false "not found" error.
            if (NonEnumerableSystems.Contains(system!) || !membership.EnumeratesSystem(system!))
            {
                return Task.FromResult(new TerminologyValidationResult(
                    IsValid: true,
                    Severity: IssueSeverity.Warning,
                    Message: $"Unable to verify code '{system}#{code}' against value set '{valueSetUrl}' - the system is not locally enumerable"));
            }

            return Task.FromResult(new TerminologyValidationResult(
                IsValid: false,
                Severity: IssueSeverity.Error,
                Message: $"The provided code '{system}#{code}' was not found in the value set '{valueSetUrl}'"));
        }

        if (membership.ContainsCode(code))
        {
            return Verified();
        }

        return Task.FromResult(new TerminologyValidationResult(
            IsValid: false,
            Severity: IssueSeverity.Error,
            Message: $"The provided code '{code}' was not found in the value set '{valueSetUrl}'"));
    }

    private static Task<TerminologyValidationResult> Verified() =>
        Task.FromResult(new TerminologyValidationResult(
            IsValid: true,
            Severity: IssueSeverity.Information,
            Message: null));

    /// <summary>
    /// Validates a coded element against a terminology binding.
    /// In-memory implementation uses hardcoded ValueSets for common bindings.
    /// </summary>
    public async Task<BindingValidationResult> ValidateBindingAsync(
        string valueSetUrl,
        BindingStrength strength,
        string? system,
        string? code,
        string? display,
        string? version,
        CancellationToken cancellationToken)
    {
        var codeValidation = await ValidateCodeAsync(system, code, display, valueSetUrl, cancellationToken);
        var (isValid, severity, message) = DetermineSeverity(strength, codeValidation);
        return new BindingValidationResult(
            IsValid: isValid,
            Strength: strength,
            Severity: severity,
            Message: message,
            SuggestedDisplay: null);
    }

    /// <summary>
    /// $expand operation is not supported by the in-memory implementation.
    /// Always returns null (expansion not available).
    /// </summary>
    public Task<ExpandResult?> ExpandValueSetAsync(
        ExpansionParameters parameters,
        CancellationToken cancellationToken)
    {
        return Task.FromResult<ExpandResult?>(null);
    }

    /// <summary>
    /// $translate operation is not supported by the in-memory implementation.
    /// Returns no matches.
    /// </summary>
    public Task<TranslateResult> TranslateCodeAsync(
        TranslateParameters parameters,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(new TranslateResult(
            Result: false,
            Message: "ConceptMap translation not supported in in-memory terminology service",
            Matches: Array.Empty<TranslateMatch>()));
    }

    /// <summary>
    /// Determines validation result severity based on binding strength and code validation outcome.
    /// </summary>
    private static (bool IsValid, IssueSeverity Severity, string? Message) DetermineSeverity(
        BindingStrength strength,
        TerminologyValidationResult codeValidation)
    {
        return strength switch
        {
            BindingStrength.Required => codeValidation.IsValid
                ? (true, codeValidation.Severity, codeValidation.Message)
                : (false, IssueSeverity.Error, codeValidation.Message),

            BindingStrength.Extensible => codeValidation.IsValid
                ? (true, codeValidation.Severity, codeValidation.Message)
                : (true, IssueSeverity.Warning, codeValidation.Message),

            BindingStrength.Preferred => (true, codeValidation.Severity, codeValidation.Message),

            BindingStrength.Example => (true, codeValidation.Severity, codeValidation.Message),

            _ => (true, IssueSeverity.Warning, "Unknown binding strength")
        };
    }
}
