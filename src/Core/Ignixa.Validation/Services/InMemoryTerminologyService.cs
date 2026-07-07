// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Collections.Frozen;
using Ignixa.Abstractions;
using Ignixa.Validation.Abstractions;

namespace Ignixa.Validation.Services;

/// <summary>
/// In-memory terminology service with version-specific ValueSets.
/// Returns warnings for unknown ValueSets to enable graceful degradation.
/// Intended for testing and prototype scenarios - production systems should use external terminology servers.
/// </summary>
public class InMemoryTerminologyService : ITerminologyService
{
    private readonly ConcurrentDictionary<string, ValueSetMembership> _valueSets = new(StringComparer.Ordinal);
    private readonly IValueSetProvider _valueSetProvider;

    /// <summary>
    /// Code systems that cannot be completely enumerated from local expansions (large external
    /// terminologies). A code from one of these that is absent from a local value-set expansion is
    /// reported as unverifiable (Warning) rather than absent (Error), since offline membership is
    /// undecidable — matching the behaviour of a validator with no terminology server.
    /// </summary>
    private static readonly FrozenSet<string> NonEnumerableSystems = new[]
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

    /// <summary>
    /// Initializes a new instance of the InMemoryTerminologyService using a ValueSet provider.
    /// </summary>
    /// <param name="valueSetProvider">The value set provider to use for terminology validation.</param>
    public InMemoryTerminologyService(IValueSetProvider valueSetProvider)
    {
        _valueSetProvider = valueSetProvider ?? throw new ArgumentNullException(nameof(valueSetProvider));
    }

    /// <summary>
    /// Initializes the service with a primary ValueSet provider plus additional sources
    /// (e.g. package-loaded IG ValueSets). On lookup the additional sources are queried
    /// in order before falling back to the primary provider, so IG-defined ValueSets
    /// override base-spec ones with the same canonical.
    /// </summary>
    /// <param name="primary">Base-spec ValueSet provider (e.g. <c>R4ValueSetProvider</c>).</param>
    /// <param name="additional">Additional sources, queried first.</param>
    public InMemoryTerminologyService(IValueSetProvider primary, IEnumerable<IValueSetProvider> additional)
    {
        ArgumentNullException.ThrowIfNull(primary);
        ArgumentNullException.ThrowIfNull(additional);
        _valueSetProvider = new LayeredValueSetProvider(primary, additional);
    }

    /// <summary>
    /// IValueSetProvider decorator that queries additional providers (in declared order)
    /// before falling back to the primary. Returns null only when no provider in the
    /// chain knows the ValueSet.
    /// </summary>
    private sealed class LayeredValueSetProvider : IValueSetProvider
    {
        private readonly IValueSetProvider _primary;
        private readonly IReadOnlyList<IValueSetProvider> _additional;

        public LayeredValueSetProvider(IValueSetProvider primary, IEnumerable<IValueSetProvider> additional)
        {
            _primary = primary;
            _additional = additional.ToArray();
        }

        public IReadOnlyList<FhirCode>? GetCodes(string valueSetUrl)
        {
            foreach (var src in _additional)
            {
                var codes = src.GetCodes(valueSetUrl);
                if (codes != null)
                {
                    return codes;
                }
            }
            return _primary.GetCodes(valueSetUrl);
        }

        public bool IsKnownValueSet(string valueSetUrl)
        {
            foreach (var src in _additional)
            {
                if (src.IsKnownValueSet(valueSetUrl))
                {
                    return true;
                }
            }
            return _primary.IsKnownValueSet(valueSetUrl);
        }

        public bool? IsValidCode(string valueSetUrl, string code)
        {
            foreach (var src in _additional)
            {
                var result = src.IsValidCode(valueSetUrl, code);
                if (result.HasValue)
                {
                    return result;
                }
            }
            return _primary.IsValidCode(valueSetUrl, code);
        }
    }

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
            membership = _valueSets.GetOrAdd(normalizedUrl, new ValueSetMembership(providerCodes));
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
    /// $lookup operation is not supported by the in-memory implementation.
    /// Returns not found for all lookups.
    /// </summary>
    public Task<LookupResult> LookupCodeAsync(
        string system,
        string code,
        string? version,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(new LookupResult(
            Found: false,
            Name: null,
            Version: null,
            Display: null,
            Definition: null,
            Properties: null,
            Designations: null));
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
    /// $subsumes operation is not supported by the in-memory implementation.
    /// Returns not-subsumed.
    /// </summary>
    public Task<SubsumesResult> SubsumesAsync(
        SubsumesParameters parameters,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(new SubsumesResult("not-subsumed"));
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

    /// <summary>
    /// System-aware membership snapshot of a locally-expanded ValueSet. Tracks which (system, code)
    /// pairs the expansion enumerates, the bare code strings (for systemless <c>code</c>-typed bindings),
    /// and the set of systems the expansion covers so that a code carrying an unenumerated system can be
    /// reported as unverifiable rather than absent.
    /// </summary>
    private sealed class ValueSetMembership
    {
        private readonly HashSet<(string System, string Code)> _pairs = new();
        private readonly HashSet<string> _codes = new(StringComparer.Ordinal);
        private readonly HashSet<string> _systems = new(StringComparer.Ordinal);

        public ValueSetMembership(IReadOnlyList<FhirCode> codes)
        {
            foreach (var entry in codes)
            {
                _codes.Add(entry.Code);
                if (!string.IsNullOrWhiteSpace(entry.System))
                {
                    _systems.Add(entry.System);
                    _pairs.Add((entry.System, entry.Code));
                }
            }
        }

        public bool ContainsPair(string system, string code) => _pairs.Contains((system, code));

        public bool ContainsCode(string code) => _codes.Contains(code);

        public bool EnumeratesSystem(string system) => _systems.Contains(system);
    }
}
