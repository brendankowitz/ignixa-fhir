// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Session;
using Ignixa.CliOnFhir.Terminology.Tools;
using Ignixa.Validation;
using Ignixa.Validation.Abstractions;

namespace Ignixa.CliOnFhir.Terminology;

/// <summary>
/// The validator's <see cref="ITerminologyService"/> over the session's terminology store (and the opt-in <c>--tx-server</c>
/// for content the store does not have). It never answers success for a check it could not evaluate: a ValueSet or code
/// system that is not loaded, an incomplete expansion, an ambiguous system or an unavailable server all answer
/// <see cref="NotCheckedRule"/>, which the validator reports as a non-failing warning and the caller counts as not checked.
/// Every message it gives starts with one of the rule tokens below and a colon, and names only the ValueSet (shape-checked),
/// never the code or anything else from the resource. Reads the session's current terminology state on each call, so a
/// package loaded later is used. Displays are not checked. Safe to use concurrently.
/// </summary>
internal sealed class StoreTerminologyService(FolderSession session) : ITerminologyService
{
    /// <summary>A required binding whose membership could not be decided.</summary>
    public const string NotCheckedRule = "binding-not-checked";

    /// <summary>An extensible binding whose membership could not be decided; it can never make a resource invalid.</summary>
    public const string ExtensibleNotCheckedRule = "extensible-binding-not-checked";

    /// <summary>A code that is not in the ValueSet of a required binding (an error).</summary>
    public const string NotInValueSetRule = "not-in-value-set";

    /// <summary>A code that is not in the ValueSet of an extensible binding (a warning).</summary>
    public const string NotInExtensibleValueSetRule = "not-in-extensible-value-set";

    public const string TerminologyNotLoadedReason = "terminology-not-loaded";

    public const string TxUnavailableReason = "tx-server-unavailable";

    private readonly RemoteBindingAnswers _remote = new();

    /// <summary>Starts a validate call: terminology server questions that failed transiently may be asked again.</summary>
    public void BeginCall() => _remote.BeginCall();

    /// <summary>Every rule token this adapter's messages start with.</summary>
    public static IReadOnlyList<string> Rules { get; } = [NotCheckedRule, ExtensibleNotCheckedRule, NotInValueSetRule, NotInExtensibleValueSetRule];

    public async Task<BindingValidationResult> ValidateBindingAsync(string valueSetUrl, BindingStrength strength, string? system, string? code, string? display, string? version, CancellationToken cancellationToken)
    {
        // FHIR sets no conformance expectation on preferred and example bindings: whatever the code, the resource conforms.
        if (strength is BindingStrength.Preferred or BindingStrength.Example)
        {
            return new BindingValidationResult(true, strength, IssueSeverity.Information, Message: null, SuggestedDisplay: null);
        }

        (string result, string reason) = await CheckAsync(valueSetUrl, system, code, version, cancellationToken);
        bool required = strength == BindingStrength.Required;
        string valueSet = Named(valueSetUrl);
        return result switch
        {
            TermValidate.Valid => new BindingValidationResult(true, strength, IssueSeverity.Information, Message: null, SuggestedDisplay: null),
            TermValidate.Invalid when required => new BindingValidationResult(false, strength, IssueSeverity.Error, $"{NotInValueSetRule}: the code is not in the required ValueSet {valueSet} ({reason})", null),
            TermValidate.Invalid => new BindingValidationResult(true, strength, IssueSeverity.Warning, $"{NotInExtensibleValueSetRule}: the code is not in the extensible ValueSet {valueSet} ({reason})", null),
            _ => new BindingValidationResult(true, strength, IssueSeverity.Warning, $"{(required ? NotCheckedRule : ExtensibleNotCheckedRule)}: {reason}; membership in ValueSet {valueSet} was not checked", null),
        };
    }

    public async Task<TerminologyValidationResult> ValidateCodeAsync(string? system, string? code, string? display, string? valueSetUrl, CancellationToken cancellationToken)
    {
        (string result, string reason) = await CheckAsync(valueSetUrl, system, code, version: null, cancellationToken);
        string valueSet = Named(valueSetUrl);
        return result switch
        {
            TermValidate.Valid => new TerminologyValidationResult(true, IssueSeverity.Information, Message: null),
            TermValidate.Invalid => new TerminologyValidationResult(false, IssueSeverity.Error, $"{NotInValueSetRule}: the code is not in ValueSet {valueSet} ({reason})"),
            _ => new TerminologyValidationResult(true, IssueSeverity.Warning, $"{NotCheckedRule}: {reason}; membership in ValueSet {valueSet} was not checked"),
        };
    }

    /// <summary>
    /// Whether <paramref name="code"/> is in <paramref name="valueSetUrl"/>: <c>valid</c>, <c>invalid</c> or <c>unknown</c>
    /// (with the reason), from the store, or from the terminology server when the store lacks the content and one is configured.
    /// </summary>
    public async Task<(string Result, string Reason)> CheckAsync(string? valueSetUrl, string? system, string? code, string? version, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(valueSetUrl))
        {
            return (TermValidate.Unknown, "binding-without-value-set");
        }

        if (string.IsNullOrEmpty(code))
        {
            return (TermValidate.Unknown, "no-code");
        }

        // The caller loads the store first; reading the current state keeps a later package load in effect.
        if (session.Terminology.Current is not { } state)
        {
            return (TermValidate.Unknown, TerminologyNotLoadedReason);
        }

        TerminologyAnswer<TermValidateFacts> local = TermValidate.AgainstValueSet(state.Store, valueSetUrl, system, code, display: null, version, cancellationToken);
        if (session.Terminology.Options.TxServer is not { } tx || !TxFallback.ValidationMissing(local.Facts.Reason))
        {
            return (local.Facts.Result, local.Facts.Reason);
        }

        // One remote question per ValueSet, system and code: see RemoteBindingAnswers for what is kept and what is asked again.
        string why = TxFallback.Why(local.Absent, local.Facts.Reason);
        return await _remote.AnswerAsync(new BindingQuestion(valueSetUrl, system, code, version), token => AskRemoteAsync(tx, valueSetUrl, system, code, version, why, token), cancellationToken);
    }

    /// <summary>
    /// <see cref="CheckAsync"/> against <paramref name="store"/> alone, never the terminology server: <c>valid</c>,
    /// <c>invalid</c>, or <c>unknown</c> with the reason.
    /// </summary>
    public static (string Result, string Reason) CheckLocal(TerminologyStore store, string valueSetUrl, string? system, string code)
    {
        ArgumentNullException.ThrowIfNull(store);

        TerminologyAnswer<TermValidateFacts> local = TermValidate.AgainstValueSet(store, valueSetUrl, system, code, display: null, version: null, CancellationToken.None);
        return (local.Facts.Result, local.Facts.Reason);
    }

    public Task<LookupResult> LookupCodeAsync(string system, string code, string? version, CancellationToken cancellationToken) => throw NotProvided("$lookup");

    public Task<ExpandResult?> ExpandValueSetAsync(ExpansionParameters parameters, CancellationToken cancellationToken) => throw NotProvided("$expand");

    public Task<TranslateResult> TranslateCodeAsync(TranslateParameters parameters, CancellationToken cancellationToken) => throw NotProvided("$translate");

    public Task<SubsumesResult> SubsumesAsync(SubsumesParameters parameters, CancellationToken cancellationToken) => throw NotProvided("$subsumes");

    private async Task<RemoteBindingAnswer> AskRemoteAsync(Remote.TxServerClient tx, string valueSet, string? system, string code, string? version, string why, CancellationToken cancellationToken)
    {
        try
        {
            TerminologyAnswer<TermValidateFacts> remote = await TxAnswers.ValidateAsync(tx, code, system, display: null, valueSet, version, why, session.UtcNow(), cancellationToken);
            return new RemoteBindingAnswer(remote.Facts.Result, remote.Facts.Reason, TransientFailure.None);
        }
        catch (ToolRequestException ex)
        {
            // An unreachable or failing server decides nothing; the binding stays unchecked, with the server's issue code.
            return new RemoteBindingAnswer(TermValidate.Unknown, $"{TxUnavailableReason} ({TerminologyNames.Token(ex.Issue.Code)})", ex.Transient);
        }
    }

    private static string Named(string? valueSetUrl)
    {
        if (string.IsNullOrEmpty(valueSetUrl))
        {
            return "(none given)";
        }

        (string url, string? version) = TerminologyStore.SplitCanonical(valueSetUrl);
        return TerminologyNames.Canonical(url, version);
    }

    private static TerminologyOperationNotSupportedException NotProvided(string operation) =>
        new($"The validation terminology adapter does not provide {operation}; use the terminology tools.");
}
