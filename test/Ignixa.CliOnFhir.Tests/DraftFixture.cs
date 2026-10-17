// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Clock;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Drafting;
using Ignixa.CliOnFhir.Session;
using Ignixa.CliOnFhir.Terminology;
using Ignixa.Specification.Generated;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>
/// A folder for drafts: two patients, an encounter and a condition each, and the terminology a draft is checked with, all in the
/// folder (no package, no network): RxNorm, LOINC, UCUM and SNOMED CT fragments with the codes the tests use, and the R4 CodeSystems
/// and ValueSets of the required bindings a draft has (statuses, intents, priority, units of time).
/// </summary>
internal static class DraftFixture
{
    public const string Instant = "2024-05-01T10:00:00Z";
    public const string RxNorm = "http://www.nlm.nih.gov/research/umls/rxnorm";
    public const string Loinc = "http://loinc.org";
    public const string Ucum = "http://unitsofmeasure.org";
    public const string Snomed = "http://snomed.info/sct";
    public const string PotassiumTablet = "198051";
    public const string PotassiumTabletDisplay = "potassium chloride 20 MEQ Extended Release Oral Tablet";
    public const string Magnesium = "2601-3";
    public const string MagnesiumDisplay = "Magnesium [Mass/volume] in Serum or Plasma";
    public const string CategoryRequired = "http://example.org/fhir/StructureDefinition/observation-category-required";

    public const string PotassiumOrder = $$$"""
        {"patient":"Patient/p1","encounter":"Encounter/e1",
         "medication":{"system":"{{{RxNorm}}}","code":"{{{PotassiumTablet}}}","display":"{{{PotassiumTabletDisplay}}}"},
         "dosageText":"1 tablet by mouth twice daily","dose":{"value":20,"unit":"mEq","unitSystem":"{{{Ucum}}}","code":"meq"},
         "route":{"system":"{{{Snomed}}}","code":"26643006","display":"Oral route"},"frequency":{"count":2,"period":1,"periodUnit":"d"}}
        """;

    public const string MagnesiumOrder = $$$"""
        {"patient":"p1","code":{"system":"{{{Loinc}}}","code":"{{{Magnesium}}}","display":"{{{MagnesiumDisplay}}}"},"priority":"routine",
         "reasonReference":["Condition/c1"],"reasonCode":[{"system":"{{{Snomed}}}","code":"43339004","display":"Hypokalemia"}]}
        """;

    public const string PotassiumResult = $$$"""
        {"patient":"Patient/p1","code":{"system":"{{{Loinc}}}","code":"2823-3"},
         "category":{"system":"http://terminology.hl7.org/CodeSystem/observation-category","code":"laboratory"},
         "value":{"quantity":{"value":3.1,"unit":"mmol/L","system":"{{{Ucum}}}","code":"mmol/L"}}
        }
        """;

    public static TestFolder Clinic() => Clinic(new TestFolder(), string.Empty);

    /// <summary>A folder holding the clinic in <c>data/</c>, for tests that write beside it (<see cref="DeIdFixture.Data"/>, <see cref="DeIdFixture.Out"/>).</summary>
    public static TestFolder ClinicRoot() => Clinic(new TestFolder(), "data/");

    private static TestFolder Clinic(TestFolder folder, string prefix) => folder
        .Write(prefix + "clinic.ndjson", """
            {"resourceType":"Patient","id":"p1","gender":"female"}
            {"resourceType":"Patient","id":"p2","gender":"male"}
            {"resourceType":"Encounter","id":"e1","status":"finished","class":{"system":"http://terminology.hl7.org/CodeSystem/v3-ActCode","code":"AMB"},"subject":{"reference":"Patient/p1"}}
            {"resourceType":"Encounter","id":"e2","status":"finished","class":{"system":"http://terminology.hl7.org/CodeSystem/v3-ActCode","code":"AMB"},"subject":{"reference":"Patient/p2"}}
            {"resourceType":"Condition","id":"c1","code":{"coding":[{"system":"http://snomed.info/sct","code":"43339004"}]},"subject":{"reference":"Patient/p1"}}
            {"resourceType":"Condition","id":"c2","code":{"coding":[{"system":"http://snomed.info/sct","code":"43339004"}]},"subject":{"reference":"Patient/p2"}}
            """)
        .Write(prefix + "terminology.ndjson", string.Join('\n', Terminology()));

    /// <summary><see cref="Clinic"/> with a profile of Observation that requires a category.</summary>
    public static TestFolder ClinicWithProfile() => Clinic().Write("profile.json", $$$"""
        {"resourceType":"StructureDefinition","id":"observation-category-required","url":"{{{CategoryRequired}}}","version":"1.0.0","name":"CategoryRequired","status":"active","fhirVersion":"4.0.1",
         "kind":"resource","abstract":false,"type":"Observation","baseDefinition":"http://hl7.org/fhir/StructureDefinition/Observation","derivation":"constraint",
         "differential":{"element":[{"id":"Observation.category","path":"Observation.category","min":1}]}}
        """);

    public static FolderSession Open(TestFolder folder, TerminologyOptions? terminology = null, TimeProvider? clock = null) =>
        new(folder.Path, new R4CoreSchemaProvider(), NullLoggerFactory.Instance, timeProvider: clock, terminology: terminology ?? TerminologyOptions.None);

    public static Task<ToolEnvelope<DraftFacts?>> RunAsync(TestFolder folder, DraftRequest request, string? asOf = Instant) => RunAsync(Open(folder), request, asOf);

    /// <summary>A draft of the folder at <paramref name="folderPath"/>, with the output seams a test swaps directories at.</summary>
    public static Task<ToolEnvelope<DraftFacts?>> RunInAsync(string folderPath, DraftRequest request, DraftOutputSeams? seams = null)
    {
        var session = new FolderSession(folderPath, new R4CoreSchemaProvider(), NullLoggerFactory.Instance, terminology: TerminologyOptions.None);
        return DraftService.RunAsync(session, request, new AsOfClock(session, Instant), NullLogger.Instance, seams, CancellationToken.None, CancellationToken.None);
    }

    public static Task<ToolEnvelope<DraftFacts?>> RunAsync(FolderSession session, DraftRequest request, string? asOf = Instant) =>
        DraftService.RunAsync(session, request, new AsOfClock(session, asOf), NullLogger.Instance, CancellationToken.None);

    public static DraftFacts Facts(ToolEnvelope<DraftFacts?> envelope) => envelope.Facts.ShouldNotBeNull();

    public static IssueItem Error(ToolEnvelope<DraftFacts?> envelope)
    {
        envelope.Facts.ShouldBeNull();
        return envelope.Issues.Single(issue => issue.Severity == "error");
    }

    public static DraftCheck Check(DraftFacts facts, string check, string element) => facts.Checks.Single(item => item.Check == check && item.Element == element);

    public static string Text(JsonNode? node) => (string)node!;

    private static IEnumerable<string> Terminology()
    {
        yield return CodeSystem(RxNorm, "complete", $"{PotassiumTablet}:{PotassiumTabletDisplay}");
        yield return CodeSystem(Loinc, "fragment", $"{Magnesium}:{MagnesiumDisplay}", "2823-3:Potassium [Moles/volume] in Serum or Plasma");
        yield return CodeSystem(Ucum, "fragment", "meq:milliequivalent", "mmol/L:millimole per liter", "s:second", "min:minute", "h:hour", "d:day", "wk:week", "mo:month", "a:year");
        yield return CodeSystem(Snomed, "fragment", "26643006:Oral route", "43339004:Hypokalemia");
        yield return CodeSystem("http://terminology.hl7.org/CodeSystem/observation-category", "complete", "laboratory:Laboratory", "vital-signs:Vital Signs");
        foreach (string line in Bound("medicationrequest-status", "http://hl7.org/fhir/CodeSystem/medicationrequest-status", "active", "on-hold", "cancelled", "completed", "entered-in-error", "stopped", "draft", "unknown"))
        {
            yield return line;
        }

        foreach (string line in Bound("medicationrequest-intent", "http://hl7.org/fhir/CodeSystem/medicationrequest-intent", "proposal", "plan", "order", "original-order", "reflex-order", "filler-order", "instance-order", "option"))
        {
            yield return line;
        }

        foreach (string line in Bound("request-status", "http://hl7.org/fhir/request-status", "draft", "active", "on-hold", "revoked", "completed", "entered-in-error", "unknown"))
        {
            yield return line;
        }

        foreach (string line in Bound("request-intent", "http://hl7.org/fhir/request-intent", "proposal", "plan", "directive", "order", "original-order", "reflex-order", "filler-order", "instance-order", "option"))
        {
            yield return line;
        }

        foreach (string line in Bound("request-priority", "http://hl7.org/fhir/request-priority", "routine", "urgent", "asap", "stat"))
        {
            yield return line;
        }

        foreach (string line in Bound("observation-status", "http://hl7.org/fhir/observation-status", "registered", "preliminary", "final", "amended", "corrected", "cancelled", "entered-in-error", "unknown"))
        {
            yield return line;
        }

        yield return $$$"""{"resourceType":"ValueSet","id":"units-of-time","url":"http://hl7.org/fhir/ValueSet/units-of-time","version":"4.0.1","status":"active","compose":{"include":[{"system":"{{{Ucum}}}","concept":[{"code":"s"},{"code":"min"},{"code":"h"},{"code":"d"},{"code":"wk"},{"code":"mo"},{"code":"a"}]}]}}""";
    }

    // A CodeSystem of the binding's codes and the R4 (4.0.1) ValueSet of all of them, as the core definitions name it.
    private static IEnumerable<string> Bound(string id, string system, params string[] codes)
    {
        yield return $$$"""{"resourceType":"CodeSystem","id":"{{{id}}}","url":"{{{system}}}","version":"4.0.1","status":"active","content":"complete","caseSensitive":true,"concept":[{{{string.Join(',', codes.Select(CodeOnly))}}}]}""";
        yield return $$$"""{"resourceType":"ValueSet","id":"{{{id}}}","url":"http://hl7.org/fhir/ValueSet/{{{id}}}","version":"4.0.1","status":"active","compose":{"include":[{"system":"{{{system}}}"}]}}""";
    }

    private static string CodeSystem(string url, string content, params string[] concepts) =>
        $$$"""{"resourceType":"CodeSystem","id":"cs-{{{new string([.. url.Where(char.IsAsciiLetterOrDigit).TakeLast(60)])}}}","url":"{{{url}}}","status":"active","content":"{{{content}}}","caseSensitive":true,"concept":[{{{string.Join(',', concepts.Select(Concept))}}}]}""";

    private static string CodeOnly(string code) => "{\"code\":\"" + code + "\"}";

    private static string Concept(string concept)
    {
        int colon = concept.IndexOf(':', StringComparison.Ordinal);
        return $$$"""{"code":"{{{concept[..colon]}}}","display":"{{{concept[(colon + 1)..]}}}"}""";
    }
}
