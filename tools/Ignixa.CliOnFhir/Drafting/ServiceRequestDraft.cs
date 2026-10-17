// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Clock;

namespace Ignixa.CliOnFhir.Drafting;

/// <summary>
/// A draft ServiceRequest (R4) from its typed input: <c>patient</c>, <c>encounter?</c>, <c>note?</c>, <c>code</c> <c>{system, code,
/// display?}</c>, <c>status</c> (default <c>draft</c>), <c>intent</c> (default <c>order</c>), <c>priority?</c>, <c>occurrence?</c> (a
/// dateTime; none by default), <c>authoredOn</c> (default the session's instant), <c>reasonCode?</c> (codings) and
/// <c>reasonReference?</c> (<c>Type/id</c> of the folder's resources).
/// </summary>
internal static class ServiceRequestDraft
{
    public const string ResourceType = "ServiceRequest";

    public static DraftContent Build(DraftArguments input, AsOf asOf)
    {
        ArgumentNullException.ThrowIfNull(input);

        DraftSubject subject = DraftSubject.Read(input);
        DraftCoding code = DraftCoding.Read(input.RequiredObject("code"), $"{ResourceType}.code.coding[0]", primary: true);
        string status = input.OptionalString("status") ?? "draft";
        string intent = input.OptionalString("intent") ?? "order";
        string? priority = input.OptionalString("priority");
        string? occurrence = DraftTimes.OptionalDateTime(input, "occurrence");
        string authoredOn = DraftTimes.DateTimeOr(input, "authoredOn", asOf);
        List<DraftCoding> reasonCodes = [.. input.ObjectArray("reasonCode").Select((reason, index) => DraftCoding.Read(reason, $"{ResourceType}.reasonCode[{index}].coding[0]"))];
        List<DraftReference> reasonReferences = [.. input.StringArray("reasonReference").Select((text, index) => Reason(input, text, index))];
        input.Done();

        var resource = new JsonObject
        {
            ["resourceType"] = ResourceType,
            ["status"] = status,
            ["intent"] = intent,
        };
        if (priority is not null)
        {
            resource["priority"] = priority;
        }

        resource["code"] = code.ToCodeableConcept();
        subject.AddSubject(resource);
        if (occurrence is not null)
        {
            resource["occurrenceDateTime"] = occurrence;
        }

        resource["authoredOn"] = authoredOn;
        if (reasonCodes.Count > 0)
        {
            resource["reasonCode"] = new JsonArray([.. reasonCodes.Select(reason => (JsonNode)reason.ToCodeableConcept())]);
        }

        if (reasonReferences.Count > 0)
        {
            resource["reasonReference"] = new JsonArray([.. reasonReferences.Select(reason => (JsonNode)new JsonObject { ["reference"] = reason.Reference })]);
        }

        subject.AddNote(resource);
        return new DraftContent(resource, [code, .. reasonCodes], [.. subject.References(ResourceType), .. reasonReferences]);
    }

    private static DraftReference Reason(DraftArguments input, string text, int index)
    {
        int slash = text.IndexOf('/', StringComparison.Ordinal);
        string type = slash > 0 ? text[..slash] : string.Empty;
        if (type.Length == 0 || !char.IsAsciiLetterUpper(type[0]) || !type.All(char.IsAsciiLetter))
        {
            throw DraftArguments.Invalid($"'{input.Of("reasonReference")}[{index}]' must be Type/id of a resource in the folder (a Condition, Observation, DiagnosticReport or DocumentReference).");
        }

        return new DraftReference($"{ResourceType}.reasonReference[{index}]", type, DraftSubject.IdOf(input, $"reasonReference[{index}]", type, text), IsSubject: false);
    }
}
