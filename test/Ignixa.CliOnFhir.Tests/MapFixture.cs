// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using System.Text.Json;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Mapping;
using Ignixa.CliOnFhir.Output;
using Ignixa.FhirMappingLanguage.Evaluation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>Maps and helpers for the map tests; the folder is <see cref="DeIdFixture.Root"/>'s (root/data, two patients and an observation each).</summary>
internal static class MapFixture
{
    /// <summary>Patient to Basic: the id, and the birth date as Basic.created.</summary>
    public const string PatientToBasic = """
        map "http://example.org/fml/patient-basic" = "PatientToBasic"
        uses "http://hl7.org/fhir/StructureDefinition/Patient" alias Patient as source
        uses "http://hl7.org/fhir/StructureDefinition/Basic" alias Basic as target
        group PatientToBasic(source src : Patient, target tgt : Basic) {
          src.id as id -> tgt.id = id;
          src.birthDate as b -> tgt.created = b;
        }
        """;

    /// <summary>A map that calls a group of an imported map.</summary>
    public const string Main = """
        map "http://example.org/fml/main" = "Main"
        imports "http://example.org/fml/helpers"
        group Main(source src : Patient, target tgt : Basic) {
          src -> tgt then Copy(src, tgt);
        }
        """;

    public const string Helpers = """
        map "http://example.org/fml/helpers" = "Helpers"
        group Copy(source src : Patient, target tgt : Basic) {
          src.id as id -> tgt.id = id;
        }
        """;

    public static Task<ToolEnvelope<MapFacts?>> RunAsync(
        string folderPath, MapRequest request, ILogger? logger = null, TimeSpan? transformTimeout = null, Action<OutputFile>? afterBegin = null, bool timedOut = false) =>
        MapRunner.RunAsync(
            DeIdFixture.OpenSession(folderPath),
            request,
            logger ?? NullLogger.Instance,
            transformTimeout ?? MappingEvaluatorOptions.Default.TransformTimeout,
            afterBegin,
            new CancellationToken(canceled: timedOut),
            CancellationToken.None);

    public static MapFacts Facts(ToolEnvelope<MapFacts?> envelope)
    {
        envelope.Issues.Where(issue => issue.Severity == "error" && !issue.Code.StartsWith("map-transform-", StringComparison.Ordinal)).ShouldBeEmpty();
        return envelope.Facts.ShouldNotBeNull();
    }

    public static IssueItem Error(ToolEnvelope<MapFacts?> envelope)
    {
        envelope.Facts.ShouldBeNull();
        return envelope.Issues.Single(issue => issue.Severity == "error");
    }

    public static string Json(ToolEnvelope<MapFacts?> envelope) => JsonSerializer.Serialize(envelope, EnvelopeJson.Compact);

    /// <summary>G1 to G<paramref name="depth"/>, each calling the next twice: 2^depth calls, which no limit but the transform timeout stops.</summary>
    public static string FanOut(int depth)
    {
        var map = new StringBuilder("map \"http://example.org/fml/fan\" = \"Fan\"\ngroup Fan(source src : Patient, target tgt : Basic) {\n  src -> tgt then G1(src, tgt);\n}\n");
        for (int i = 1; i < depth; i++)
        {
            map.Append($"group G{i}(source s, target t) {{\n  s -> t then G{i + 1}(s, t);\n  s -> t then G{i + 1}(s, t);\n}}\n");
        }

        return map.Append($"group G{depth}(source s, target t) {{\n  s.gender as g;\n}}\n").ToString();
    }

    /// <summary>A StructureMap with one rule whose source condition is <paramref name="condition"/>.</summary>
    public static string StructureMap(string id, string url, string condition = "$this.exists()") => $$$"""
        {"resourceType":"StructureMap","id":"{{{id}}}","url":"{{{url}}}","name":"SM{{{id}}}","status":"draft",
         "structure":[{"url":"http://hl7.org/fhir/StructureDefinition/Patient","mode":"source","alias":"Patient"},{"url":"http://hl7.org/fhir/StructureDefinition/Basic","mode":"target","alias":"Basic"}],
         "group":[{"name":"PatientToBasic","typeMode":"none","input":[{"name":"src","type":"Patient","mode":"source"},{"name":"tgt","type":"Basic","mode":"target"}],
           "rule":[{"name":"id","source":[{"context":"src","element":"id","variable":"id","condition":{{{JsonSerializer.Serialize(condition)}}}}],"target":[{"context":"tgt","element":"id","transform":"copy","parameter":[{"valueId":"id"}]}]}]}]}
        """.ReplaceLineEndings(string.Empty);
}
