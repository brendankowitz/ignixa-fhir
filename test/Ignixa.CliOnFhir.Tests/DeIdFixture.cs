// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.DeId;
using Ignixa.CliOnFhir.Output;
using Ignixa.CliOnFhir.Session;
using Ignixa.Specification.Generated;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>A folder at <c>root/data</c> with an output place beside it, configurations and helpers for the deid tests.</summary>
internal static class DeIdFixture
{
    public const string HashKey = "hash-key-6f1c2e9a4b7d";

    public const string DateKey = "date-key-3a8e5c1f9d20";

    public const string Mrn = "MRN-55501";

    /// <summary>Redacts names, shifts birth dates and hashes ids, identifier values and references, with keys of its own.</summary>
    public const string Config = $$$"""
        {"fhirVersion":"R4","fhirPathRules":[
          {"path":"Resource.id","method":"cryptoHash"},
          {"path":"Patient.identifier.value","method":"cryptoHash"},
          {"path":"Patient.name","method":"redact"},
          {"path":"Patient.birthDate","method":"dateShift"},
          {"path":"descendants().ofType(Reference).reference","method":"cryptoHash"}],
         "parameters":{"cryptoHashKey":"{{{HashKey}}}","dateShiftKey":"{{{DateKey}}}"}}
        """;

    /// <summary>Redacts every name; a resource with more than one name fails its rule, since single() gets more than one item.</summary>
    public const string OneNameConfig = """{"fhirPathRules":[{"path":"Patient.name.single()","method":"redact"}]}""";

    public const string Patients = $$$"""
        {"resourceType":"Patient","id":"p1","identifier":[{"system":"urn:mrn","value":"{{{Mrn}}}"}],"name":[{"family":"Smith","given":["Ann"]}],"birthDate":"1975-03-04","gender":"female"}
        {"resourceType":"Patient","id":"p2","identifier":[{"system":"urn:mrn","value":"MRN-55502"}],"name":[{"family":"Jones","given":["Bo"]},{"family":"Brown"}],"birthDate":"1985-06-07","gender":"male"}
        """;

    public const string Observations = """
        {"resourceType":"Observation","id":"o1","status":"final","code":{"text":"pulse"},"subject":{"reference":"Patient/p1"}}
        {"resourceType":"Observation","id":"o2","status":"final","code":{"text":"pulse"},"subject":{"reference":"Patient/p2"}}
        """;

    /// <summary>root/data with two patients and an observation each.</summary>
    public static TestFolder Root() => new TestFolder().Write("data/patients.ndjson", Patients).Write("data/obs.ndjson", Observations);

    public static string Data(TestFolder root) => Path.Combine(root.Path, "data");

    public static string Out(TestFolder root) => Path.Combine(root.Path, "out");

    public static FolderSession OpenSession(string folderPath) => new(folderPath, new R4CoreSchemaProvider(), NullLoggerFactory.Instance);

    public static Task<ToolEnvelope<DeIdFacts?>> RunAsync(string folderPath, DeIdRequest request, ILogger? logger = null, Action<OutputFile>? afterBegin = null, bool timedOut = false) =>
        DeIdRunner.RunAsync(OpenSession(folderPath), request, logger ?? NullLogger.Instance, afterBegin, new CancellationToken(canceled: timedOut), CancellationToken.None);

    public static DeIdFacts Facts(ToolEnvelope<DeIdFacts?> envelope)
    {
        envelope.Issues.Where(issue => issue.Severity == "error" && !issue.Code.StartsWith("deid-resource-", StringComparison.Ordinal)).ShouldBeEmpty();
        return envelope.Facts.ShouldNotBeNull();
    }

    public static IssueItem Error(ToolEnvelope<DeIdFacts?> envelope)
    {
        envelope.Facts.ShouldBeNull();
        return envelope.Issues.Single(issue => issue.Severity == "error");
    }

    public static DeIdTypeFacts Type(DeIdFacts facts, string resourceType) => facts.Types.Single(type => type.ResourceType == resourceType);

    /// <summary>The lines of an NDJSON file, each parsed.</summary>
    public static JsonObject[] Lines(string path) =>
        [.. File.ReadAllLines(path).Select(line => JsonNode.Parse(line)!.AsObject())];

    public static string Json(ToolEnvelope<DeIdFacts?> envelope) => JsonSerializer.Serialize(envelope, EnvelopeJson.Compact);

    /// <summary>Every file and directory under <paramref name="root"/>, files with their content's hash.</summary>
    public static string[] Snapshot(string root) =>
        [.. Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(entry => File.Exists(entry) ? $"{Path.GetRelativePath(root, entry)}:{Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(entry)))}" : Path.GetRelativePath(root, entry))];
}
