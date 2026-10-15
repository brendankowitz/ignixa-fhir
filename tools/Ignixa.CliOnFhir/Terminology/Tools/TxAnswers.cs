// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Terminology.Remote;

namespace Ignixa.CliOnFhir.Terminology.Tools;

/// <summary>
/// The terminology tools' answers from a remote terminology server, used only when the local store cannot answer. Each
/// request carries the caller's codes, systems, versions, display, canonical and filter text, and paging, nothing else.
/// The response's displays and designations go into facts as terminology content; its messages and diagnostics are not
/// used; a response that is not the expected shape fails the call as <c>tx-server-error</c>.
/// </summary>
internal static class TxAnswers
{
    public const string UsedCode = "tx-server-answered";
    public const string NotFoundOutcome = "not-found-by-tx-server";

    private static readonly string[] SubsumptionOutcomes = [TermSubsumes.Equivalent, TermSubsumes.Subsumes, TermSubsumes.SubsumedBy, TermSubsumes.NotSubsumed];

    public static async Task<TerminologyAnswer<TermLookupFacts>> LookupAsync(TxServerClient tx, string system, string code, string? version, string why, DateTimeOffset checkedAt, CancellationToken cancellationToken)
    {
        JsonObject? found = await tx.GetOrNotFoundAsync("CodeSystem/$lookup", With([("system", system), ("code", code)], ("version", version)), "Parameters", notFoundIsAnswer: true, cancellationToken);
        if (found is not { } parameters)
        {
            var unknown = new TermLookupFacts(NotFoundOutcome, system, version, code, AnswerSource.Remote(tx.BaseUri, null), null, null, null, null, null, null, null, null, null, null, null);
            return new TerminologyAnswer<TermLookupFacts>(unknown, [], [], [NotFoundAbsent(tx, system, code, checkedAt)], [Used(tx, why), NotFound(tx, system, code)]);
        }

        string? reported = TxParameters.Value(parameters, "version");
        List<ConceptProperty> properties = [];
        foreach (JsonObject property in TxParameters.All(parameters, "property"))
        {
            if (TxParameters.Value(property, "code", parts: true) is { } propertyCode
                && TxParameters.Parts(property, "value").FirstOrDefault() is { } value
                && (TxParameters.PrimitiveValue(value) ?? Coding(value["valueCoding"] as JsonObject)) is { } text)
            {
                properties.Add(new ConceptProperty(propertyCode, text, ValueType(value)));
            }
        }

        List<ConceptDesignation> designations = [.. TxParameters.All(parameters, "designation")
            .Select(designation => (Value: TxParameters.Value(designation, "value", parts: true), Designation: designation))
            .Where(item => item.Value is not null)
            .Select(item => new ConceptDesignation(TxParameters.Value(item.Designation, "language", parts: true), Coding(TxParameters.Coding(item.Designation, "use")), item.Value!))];

        string? status = properties.FirstOrDefault(property => property.Code == "status")?.Value;
        bool? inactive = properties.FirstOrDefault(property => property.Code == "inactive") is { } flag
            ? flag.Value == "true"
            : status is null ? null : status is "retired" or "inactive";
        List<OmittedItem> omitted = [];
        var facts = new TermLookupFacts(
            ConceptQuery.Found,
            system,
            reported,
            code,
            AnswerSource.Remote(tx.BaseUri, reported),
            null,
            TermText.Clip(TxParameters.Value(parameters, "display"), TermText.MaxShortText, "display", omitted),
            TermText.Clip(TxParameters.Value(parameters, "definition"), TermText.MaxLongText, "definition", omitted),
            status,
            inactive,
            TermText.Take(properties, "properties", omitted),
            TermText.Take(designations, "designations", omitted),
            null,
            null,
            null,
            null);
        return new TerminologyAnswer<TermLookupFacts>(facts, [], omitted, [], [Used(tx, why)]);
    }

    public static async Task<TerminologyAnswer<TermValidateFacts>> ValidateAsync(TxServerClient tx, string code, string? system, string? display, string? valueSet, string? version, string why, DateTimeOffset checkedAt, CancellationToken cancellationToken)
    {
        (string Name, string Value)[] request = valueSet is null
            ? With([("url", system!), ("code", code)], ("version", version), ("display", display))
            : With([("url", valueSet), ("code", code)], ("system", system), ("version", version), ("display", display));
        JsonObject? found = await tx.GetOrNotFoundAsync(valueSet is null ? "CodeSystem/$validate-code" : "ValueSet/$validate-code", request, "Parameters", notFoundIsAnswer: true, cancellationToken);
        if (found is not { } parameters)
        {
            var unknown = new TermValidateFacts(TermValidate.Unknown, NotFoundOutcome, system, version, code, valueSet, null, AnswerSource.Remote(tx.BaseUri, null), null, display, null, null);
            return new TerminologyAnswer<TermValidateFacts>(unknown, [], [], [NotFoundAbsent(tx, system ?? valueSet!, code, checkedAt)], [Used(tx, why), NotFound(tx, system ?? valueSet!, code)]);
        }

        if (TxParameters.All(parameters, "result").FirstOrDefault()?["valueBoolean"] is not JsonValue result || result.GetValueKind() is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw ToolRequestException.Error(TxServerClient.ErrorCode, $"The terminology server's $validate-code answer has no boolean 'result'; nothing was concluded from it.");
        }

        bool valid = result.GetValue<bool>();
        string? reported = TxParameters.Value(parameters, "version");
        string? inactive = TxParameters.Value(parameters, "inactive");
        string? expected = TxParameters.Value(parameters, "display");

        // Aligned with the local answer: a rejection where the server's display differs from the one given is a display mismatch.
        bool? displayMatches = display is null || expected is null ? null : string.Equals(TextFolding.Fold(display), TextFolding.Fold(expected), StringComparison.Ordinal);
        string reason = valid
            ? (valueSet is null ? "code-in-system" : "code-in-value-set")
            : displayMatches == false ? TermValidate.DisplayMismatch : "rejected-by-tx-server";
        var facts = new TermValidateFacts(
            valid ? TermValidate.Valid : TermValidate.Invalid,
            reason,
            TxParameters.Value(parameters, "system") ?? system,
            reported,
            code,
            valueSet,
            null,
            AnswerSource.Remote(tx.BaseUri, reported),
            expected is null ? null : expected[..Math.Min(expected.Length, TermText.MaxShortText)],
            display,
            displayMatches,
            inactive is null ? null : inactive == "true");
        return new TerminologyAnswer<TermValidateFacts>(facts, [], [], [], [Used(tx, why)]);
    }

    public static async Task<TerminologyAnswer<TermSubsumesFacts>> SubsumesAsync(TxServerClient tx, string system, string codeA, string codeB, string? version, string why, CancellationToken cancellationToken)
    {
        JsonObject parameters = await tx.GetAsync("CodeSystem/$subsumes", With([("system", system), ("codeA", codeA), ("codeB", codeB)], ("version", version)), "Parameters", cancellationToken);
        string outcome = TxParameters.Value(parameters, "outcome") is { } value && SubsumptionOutcomes.Contains(value, StringComparer.Ordinal)
            ? value
            : throw ToolRequestException.Error(TxServerClient.ErrorCode, "The terminology server's $subsumes answer has no outcome of equivalent, subsumes, subsumed-by or not-subsumed; nothing was concluded from it.");
        var facts = new TermSubsumesFacts(outcome, system, version, codeA, codeB, AnswerSource.Remote(tx.BaseUri, version), null, []);
        return new TerminologyAnswer<TermSubsumesFacts>(facts, [], [], [], [Used(tx, why)]);
    }

    public static async Task<TerminologyAnswer<TermExpandFacts>> ExpandAsync(TxServerClient tx, string valueSet, string? filter, int pageSize, int offset, string why, CancellationToken cancellationToken)
    {
        JsonObject expanded = await tx.GetAsync(
            "ValueSet/$expand",
            With([("url", valueSet), ("count", pageSize.ToString(System.Globalization.CultureInfo.InvariantCulture)), ("offset", offset.ToString(System.Globalization.CultureInfo.InvariantCulture))], ("filter", filter)),
            "ValueSet",
            cancellationToken);
        JsonObject expansion = expanded["expansion"] as JsonObject
            ?? throw ToolRequestException.Error(TxServerClient.ErrorCode, "The terminology server's $expand answer has no expansion; nothing was concluded from it.");
        List<ExpansionCode> codes = [];
        Flatten(expansion["contains"] as JsonArray, codes);
        List<IssueItem> issues = [Used(tx, why)];
        List<OmittedItem> omitted = [];
        if (codes.Count > pageSize)
        {
            omitted.Add(new OmittedItem($"{codes.Count - pageSize} codes the server returned beyond the page size", "limit"));
            codes = [.. codes.Take(pageSize)];
        }

        int? total = expansion["total"] is JsonValue count && count.GetValueKind() == JsonValueKind.Number ? count.GetValue<int>() : null;
        if (total is null)
        {
            issues.Add(new IssueItem("information", "tx-server-total-unknown", "The terminology server did not say how many codes the expansion has; 'facts.total' counts only those up to this page, and more may follow.", Location: null));
        }

        string? reported = TxParameters.String(expanded, "version");
        string url = TxParameters.String(expanded, "url") ?? TerminologyStore.SplitCanonical(valueSet).Url;
        var facts = new TermExpandFacts(url, reported, TermExpand.Expanded, AnswerSource.Remote(tx.BaseUri, reported), null, filter, total ?? offset + codes.Count, offset, codes.Count, codes, null);
        List<NextCall> next = [];
        if ((total is { } known && offset + codes.Count < known) || (total is null && codes.Count == pageSize))
        {
            var args = new Dictionary<string, object> { ["valueSet"] = valueSet, ["offset"] = offset + codes.Count, ["count"] = pageSize };
            if (filter is not null)
            {
                args["filter"] = filter;
            }

            omitted.Add(new OmittedItem(total is { } all ? $"{all - offset - codes.Count} more codes" : "possibly more codes", "limit"));
            next.Add(new NextCall("terminology_expand", args));
        }

        return new TerminologyAnswer<TermExpandFacts>(facts, [], omitted, [], issues) { Next = next };
    }

    public static async Task<TerminologyAnswer<TermTranslateFacts>> TranslateAsync(TxServerClient tx, string system, string code, string? targetSystem, string? conceptMap, string why, CancellationToken cancellationToken)
    {
        // The parameter names differ by the SERVER's FHIR version (R4 code/targetsystem, R5 sourceCode/targetSystem); when it
        // does not say, both spellings are sent.
        string? serverVersion = await tx.FhirVersionAsync();
        bool r5 = serverVersion?.StartsWith('5') == true || serverVersion?.StartsWith('6') == true;
        bool both = serverVersion is null;
        List<IssueItem> issues = [Used(tx, why)];
        if (both)
        {
            issues.Add(new IssueItem("information", "tx-server-version-unknown", $"The terminology server {tx.BaseUri} did not report its FHIR version, so $translate was sent both the R4 (code, targetsystem) and the R5 (sourceCode, targetSystem) parameter names.", Location: null));
        }

        (string Name, string Value)[] request = both
            ? With([("system", system), ("code", code), ("sourceCode", code)], ("targetsystem", targetSystem), ("targetSystem", targetSystem), ("url", conceptMap))
            : With([("system", system), (r5 ? "sourceCode" : "code", code)], (r5 ? "targetSystem" : "targetsystem", targetSystem), ("url", conceptMap));
        JsonObject parameters = await tx.GetAsync("ConceptMap/$translate", request, "Parameters", cancellationToken);
        if (TxParameters.All(parameters, "result").FirstOrDefault()?["valueBoolean"] is not JsonValue result || result.GetValueKind() is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw ToolRequestException.Error(TxServerClient.ErrorCode, "The terminology server's $translate answer has no boolean 'result'; nothing was concluded from it.");
        }

        List<TermTranslation> translations = [];
        foreach (JsonObject match in TxParameters.All(parameters, "match"))
        {
            JsonObject? concept = TxParameters.Coding(match, "concept");
            string? map = TxParameters.Value(match, "source", parts: true) ?? TxParameters.Value(match, "originMap", parts: true);
            string? display = TxParameters.String(concept, "display");
            translations.Add(new TermTranslation(
                map ?? conceptMap ?? "(not named by the server)",
                null,
                AnswerSource.Remote(tx.BaseUri, TxParameters.String(concept, "version")),
                TxParameters.String(concept, "system"),
                TxParameters.String(concept, "version"),
                TxParameters.String(concept, "code"),
                display is null ? null : display[..Math.Min(display.Length, TermText.MaxShortText)],
                TxParameters.Value(match, "equivalence", parts: true),
                TxParameters.Value(match, "relationship", parts: true),
                null,
                false,
                "tx-server"));
        }

        string outcome = result.GetValue<bool>() && translations.Exists(TermTranslate.IsMapping) ? TermTranslate.Mapped : TermTranslate.NotMapped;
        List<OmittedItem> omitted = [];
        var facts = new TermTranslateFacts(outcome, system, code, targetSystem, conceptMap, 0, TermText.Take<TermTranslation>(translations, "translations", omitted));
        return new TerminologyAnswer<TermTranslateFacts>(facts, [], omitted, [], issues);
    }

    private static AbsentItem NotFoundAbsent(TxServerClient tx, string system, string code, DateTimeOffset checkedAt) =>
        new(TerminologyNames.Coded(system, code), $"the terminology server {tx.BaseUri}, which answered HTTP 404 with OperationOutcome issue code not-found", checkedAt);

    private static IssueItem NotFound(TxServerClient tx, string system, string code) => new(
        "warning",
        NotFoundOutcome,
        $"The terminology server {tx.BaseUri} does not know {TerminologyNames.Coded(system, code)} (HTTP 404, not-found). That is the server's answer, not proof the code does not exist: the server may lack that system or edition.",
        Location: null);

    private static IssueItem Used(TxServerClient tx, string why) => new(
        "information",
        UsedCode,
        $"Answered by the terminology server {tx.BaseUri} because {why}; it was sent only the codes, systems, versions, display, canonical and filter of this call.",
        Location: null);

    private static (string Name, string Value)[] With((string Name, string Value)[] required, params (string Name, string? Value)[] optional) =>
        [.. required, .. optional.Where(item => item.Value is not null).Select(item => (item.Name, item.Value!))];

    private static string? Coding(JsonObject? coding) => coding is null ? null : $"{TxParameters.String(coding, "system")}|{TxParameters.String(coding, "code")}";

    private static string ValueType(JsonObject part) =>
        part.Select(pair => pair.Key).FirstOrDefault(key => key.StartsWith("value", StringComparison.Ordinal)) is { Length: > 5 } key
            ? key == "valueCoding" ? "Coding" : char.ToLowerInvariant(key[5]) + key[6..]
            : "unknown";

    private static void Flatten(JsonArray? contains, List<ExpansionCode> codes)
    {
        foreach (JsonObject item in contains?.OfType<JsonObject>() ?? [])
        {
            if (TxParameters.String(item, "system") is { } itemSystem && TxParameters.String(item, "code") is { } itemCode)
            {
                string? display = TxParameters.String(item, "display");
                bool? inactive = item["inactive"] is JsonValue flag && flag.GetValueKind() is JsonValueKind.True or JsonValueKind.False ? flag.GetValue<bool>() : null;
                codes.Add(new ExpansionCode(itemSystem, TxParameters.String(item, "version"), itemCode, display is null ? null : display[..Math.Min(display.Length, TermText.MaxShortText)], inactive));
            }

            Flatten(item["contains"] as JsonArray, codes);
        }
    }
}
