// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Views;
using Shouldly;
using static Ignixa.CliOnFhir.Tests.SearchFixture;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>ViewDefinitions and helpers shared by the SQL-on-FHIR view tests.</summary>
internal static class ViewFixture
{
    public const string PatientView = """
        {"resourceType":"ViewDefinition","resource":"Patient","status":"active",
         "select":[{"column":[{"name":"id","path":"id"},{"name":"gender","path":"gender"},{"name":"birth_date","path":"birthDate"}]}]}
        """;

    public const string ObservationView = """
        {"resourceType":"ViewDefinition","resource":"Observation","status":"active",
         "select":[{"column":[{"name":"id","path":"id"},{"name":"patient","path":"subject.reference"},{"name":"code","path":"code.coding.first().code"},{"name":"value","path":"value.ofType(Quantity).value"}]}]}
        """;

    public const string ConditionView = """
        {"resourceType":"ViewDefinition","resource":"Condition","status":"active",
         "select":[{"column":[{"name":"id","path":"id"},{"name":"patient","path":"subject.reference"},{"name":"code","path":"code.coding.first().code"}]}]}
        """;

    /// <summary>Two Smiths and a Jones, a name each; p1 also has a second (maiden) name.</summary>
    public static TestFolder NamedPatients() => new TestFolder()
        .Write(PatientsFile, """
            {"resourceType":"Patient","id":"p1","name":[{"use":"official","family":"Smith","given":["Ann","Marie"]},{"use":"maiden","family":"Brown","given":["Ann"]}],"birthDate":"1975-03-04","gender":"female"}
            {"resourceType":"Patient","id":"p2","name":[{"family":"Jones","given":["Bo"]}],"birthDate":"1985-06-07","gender":"male"}
            """);

    /// <summary>Clinic's patients and observations, and conditions: p1 has two of 44054006 and one of 195967001, p2 one of 44054006, p3 one of 195967001.</summary>
    public static TestFolder ClinicWithConditions() => Clinic().Write("conditions.ndjson", """
        {"resourceType":"Condition","id":"c1","code":{"coding":[{"system":"http://snomed.info/sct","code":"44054006"}]},"subject":{"reference":"Patient/p1"}}
        {"resourceType":"Condition","id":"c2","code":{"coding":[{"system":"http://snomed.info/sct","code":"44054006"}]},"subject":{"reference":"Patient/p1"}}
        {"resourceType":"Condition","id":"c3","code":{"coding":[{"system":"http://snomed.info/sct","code":"195967001"}]},"subject":{"reference":"Patient/p1"}}
        {"resourceType":"Condition","id":"c4","code":{"coding":[{"system":"http://snomed.info/sct","code":"44054006"}]},"subject":{"reference":"Patient/p2"}}
        {"resourceType":"Condition","id":"c5","code":{"coding":[{"system":"http://snomed.info/sct","code":"195967001"}]},"subject":{"reference":"Patient/p3"}}
        """);

    public static async Task<ToolEnvelope<ViewFacts?>> RunAsync(TestFolder folder, ViewRequest request) =>
        await ViewRunner.RunAsync(Open(folder), request, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, CancellationToken.None);

    /// <summary>A run over the folder at <paramref name="folderPath"/>, for a folder that is not a whole <see cref="TestFolder"/>.</summary>
    public static async Task<ToolEnvelope<ViewFacts?>> RunInAsync(string folderPath, ViewRequest request, Microsoft.Extensions.Logging.ILogger? logger = null, TimeSpan? timeout = null) =>
        await ViewRunner.RunAsync(
            new Ignixa.CliOnFhir.Session.FolderSession(folderPath, new Ignixa.Specification.Generated.R4CoreSchemaProvider(), Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance),
            request,
            timeout,
            logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance,
            CancellationToken.None);

    public static ViewFacts Facts(ToolEnvelope<ViewFacts?> envelope)
    {
        envelope.Issues.Where(issue => issue.Severity == "error").ShouldBeEmpty();
        return envelope.Facts.ShouldNotBeNull();
    }

    public static string[] Names(ViewFacts facts) => [.. facts.Columns.Select(column => column.Name)];

    /// <summary>Every returned row as its cells' text, a null cell as null.</summary>
    public static string?[][] Cells(ViewFacts facts) =>
        [.. facts.Rows.ShouldNotBeNull().Select(row => row.Select(cell => cell?.ToString()).ToArray())];

    public static IssueItem Error(ToolEnvelope<ViewFacts?> envelope)
    {
        envelope.Facts.ShouldBeNull();
        return envelope.Issues.Single(issue => issue.Severity == "error");
    }
}
